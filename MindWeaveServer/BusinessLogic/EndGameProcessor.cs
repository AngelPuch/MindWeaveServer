using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Game;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class EndGameProcessor
    {
        private readonly IMatchmakingRepository matchmakingRepository;
        private readonly StatsLogic statsLogic;
        private readonly IPuzzleRepository puzzleRepository;
        private readonly Logger logger;

        public EndGameProcessor(
            IMatchmakingRepository matchmakingRepository,
            StatsLogic statsLogic,
            IPuzzleRepository puzzleRepository,
            Logger logger)
        {
            this.matchmakingRepository = matchmakingRepository;
            this.statsLogic = statsLogic;
            this.puzzleRepository = puzzleRepository;
            this.logger = logger;
        }

        public async Task<MatchEndResultDto> processEndGameAsync(GameSession session, string reason)
        {
            var duration = calculateDuration(session.StartTime);
            var rankedPlayers = getRankedPlayers(session.Players.Values);
            var clientResults = new List<PlayerResultDto>();

            try
            {
                clientResults = await processWithRepositoriesAsync(session, rankedPlayers, duration.minutes);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Critical error during end game processing for session {LobbyCode}",
                    session.LobbyCode);
            }

            return buildMatchEndResult(session.MatchId, reason, duration.totalSeconds, clientResults);
        }

        private async Task<List<PlayerResultDto>> processWithRepositoriesAsync(
            GameSession session,
            List<PlayerSessionData> rankedPlayers,
            int minutesPlayed)
        {

            var entities = await fetchGameEntitiesAsync(matchmakingRepository, puzzleRepository, session.MatchId, session.PuzzleId);

            var context = new EndGameProcessingContext
            {
                MatchRepo = matchmakingRepository,
                StatsService = statsLogic,
                MatchEntity = entities.match,
                PuzzleEntity = entities.puzzle,
                MatchId = session.MatchId,
                MinutesPlayed = minutesPlayed,
                TotalParticipants = rankedPlayers.Count
            };

            var results = await processAllPlayersAsync(rankedPlayers, context);
            await matchmakingRepository.finishMatchAsync(session.MatchId);

            return results;
        }

        private async Task<(Matches match, Puzzles puzzle)> fetchGameEntitiesAsync(
            IMatchmakingRepository matchRepo,
            IPuzzleRepository puzzleRepo,
            int matchId,
            int puzzleId)
        {
            Matches matchEntity = null;
            Puzzles puzzleEntity = null;

            try
            {
                matchEntity = await matchRepo.getMatchByIdAsync(matchId);
                if (matchEntity != null)
                {
                    matchEntity.end_time = DateTime.UtcNow;
                }

                puzzleEntity = await puzzleRepo.getPuzzleByIdAsync(puzzleId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Could not fetch Match/Puzzle entities for MatchId {MatchId}", matchId);
            }

            return (matchEntity, puzzleEntity);
        }

        private async Task<List<PlayerResultDto>> processAllPlayersAsync(
            List<PlayerSessionData> rankedPlayers,
            EndGameProcessingContext context)
        {
            var results = new List<PlayerResultDto>();
            int currentRank = 1;

            foreach (var player in rankedPlayers)
            {
                var resultDto = await processSinglePlayerAsync(player, currentRank, context);
                results.Add(resultDto);
                currentRank++;
            }

            return results;
        }

        private async Task<PlayerResultDto> processSinglePlayerAsync(
            PlayerSessionData player,
            int rank,
            EndGameProcessingContext context)
        {
            bool isWinner = rank == 1;
            var unlockedIds = new List<int>();

            if (player.PlayerId > 0)
            {
                try
                {
                    unlockedIds = await handlePlayerStatsAndAchievementsAsync(player, rank, context);
                    await saveMatchParticipantStatsAsync(player, rank, context);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error processing data for player {Username}", player.Username);
                }
            }

            return new PlayerResultDto
            {
                PlayerId = player.PlayerId,
                Username = player.Username,
                Score = player.Score,
                PiecesPlaced = player.PiecesPlaced,
                Rank = rank,
                IsWinner = isWinner,
                UnlockedAchievementIds = unlockedIds
            };
        }

        private async Task<List<int>> handlePlayerStatsAndAchievementsAsync(
            PlayerSessionData player,
            int rank,
            EndGameProcessingContext context)
        {
            var newUnlockedIds = new List<int>();
            bool isWinner = rank == 1;

            var statsDto = new PlayerMatchStatsDto
            {
                PlayerId = player.PlayerId,
                Score = player.Score,
                Rank = rank,
                IsWin = isWinner,
                PlaytimeMinutes = context.MinutesPlayed
            };

            var historicalStats = await context.StatsService.getPlayerStatsAsync(player.PlayerId);
            await context.StatsService.processMatchResultsAsync(statsDto);

            if (context.MatchEntity == null || context.PuzzleEntity == null || historicalStats == null)
            {
                return newUnlockedIds;
            }

            var achievementContext = buildAchievementContext(player, rank, context, historicalStats);
            var qualifiedAchievements = AchievementEvaluator.evaluate(achievementContext);
            newUnlockedIds = await context.StatsService.unlockAchievementsAsync(player.PlayerId, qualifiedAchievements);

            if (newUnlockedIds.Any())
            {
                logger.Info("Player {Username} unlocked {Count} achievements.",
                    player.Username, newUnlockedIds.Count);
            }

            return newUnlockedIds;
        }

        private static AchievementContext buildAchievementContext(
            PlayerSessionData player,
            int rank,
            EndGameProcessingContext context,
            PlayerStats historicalStats)
        {
            return new AchievementContext
            {
                PlayerStats = historicalStats,
                CurrentMatchStats = new MatchParticipants
                {
                    player_id = player.PlayerId,
                    score = player.Score,
                    final_rank = rank,
                    pieces_placed = player.PiecesPlaced
                },
                MatchInfo = context.MatchEntity,
                PuzzleInfo = context.PuzzleEntity,
                TotalParticipants = context.TotalParticipants
            };
        }

        private async Task saveMatchParticipantStatsAsync(
            PlayerSessionData player,
            int rank,
            EndGameProcessingContext context)
        {
            var updateDto = new MatchParticipantStatsUpdateDto
            {
                MatchId = context.MatchId,
                PlayerId = player.PlayerId,
                Score = player.Score,
                PiecesPlaced = player.PiecesPlaced,
                Rank = rank
            };

            await context.MatchRepo.updateMatchParticipantStatsAsync(updateDto);
        }

        private static (int minutes, double totalSeconds) calculateDuration(DateTime startTime)
        {
            var duration = DateTime.UtcNow - startTime;
            int minutes = Math.Max(1, (int)duration.TotalMinutes);
            return (minutes, duration.TotalSeconds);
        }

        private static List<PlayerSessionData> getRankedPlayers(IEnumerable<PlayerSessionData> players)
        {
            return players
                .OrderByDescending(p => p.Score)
                .ThenByDescending(p => p.PiecesPlaced)
                .ToList();
        }

        private static MatchEndResultDto buildMatchEndResult(
            int matchId,
            string reason,
            double totalSeconds,
            List<PlayerResultDto> results)
        {
            return new MatchEndResultDto
            {
                MatchId = matchId,
                Reason = reason,
                TotalTimeElapsedSeconds = totalSeconds,
                PlayerResults = results
            };
        }

        public class EndGameProcessingContext
        {
            public IMatchmakingRepository MatchRepo { get; set; }
            public StatsLogic StatsService { get; set; }
            public Matches MatchEntity { get; set; }
            public Puzzles PuzzleEntity { get; set; }
            public int MatchId { get; set; }
            public int MinutesPlayed { get; set; }
            public int TotalParticipants { get; set; }
        }
    }
}