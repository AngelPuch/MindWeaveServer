using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Game;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Timers;

namespace MindWeaveServer.BusinessLogic
{
    public class GameSession : IDisposable
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private const double SNAP_TOLERANCE = 30.0;
        private const double PENALTY_TOLERANCE = 60.0;
        private const int HOARDING_LIMIT_SECONDS = 10;
        private const int PENALTY_HOARDING = 15;
        private const int DEFAULT_MATCH_DURATION_SECONDS = 300;

        private const int MILLISECONDS_PER_SECOND = 1000;
        private const int MIN_PLAYERS_TO_CONTINUE = 2;
        private const int MIN_PLAYTIME_MINUTES = 1;
        private const int KICK_REASON_PROFANITY_ID = 2;

        private const string END_REASON_FORFEIT = "Forfeit";
        private const string END_REASON_PUZZLE_SOLVED = "PuzzleSolved";
        private const string END_REASON_TIMEOUT = "TimeOut";

        private const string PENALTY_REASON_HOARDING = "HOARDING";
        private const string PENALTY_REASON_WRONG_SPOT = "WRONG_SPOT";
        private const string PENALTY_REASON_MISS = "MISS";

        public string LobbyCode { get; }
        public int PuzzleId { get; }
        public int MatchId { get; }
        public PuzzleDefinitionDto PuzzleDefinition { get; }
        public DateTime StartTime { get; }
        public ConcurrentDictionary<int, PlayerSessionData> Players { get; } = new ConcurrentDictionary<int, PlayerSessionData>();
        public ConcurrentDictionary<int, PuzzlePieceState> PieceStates { get; } = new ConcurrentDictionary<int, PuzzlePieceState>();

        private readonly IMatchmakingRepository matchmakingRepository;
        private readonly StatsLogic statsLogic;
        private readonly IPuzzleRepository puzzleRepository;
        private readonly Action<string> onSessionEndedCleanup;
        private readonly IScoreCalculator scoreCalculator;

        private readonly Timer hoardingTimer;
        private Timer gameDurationTimer;
        private bool isGameEnded;
        private bool isFirstBloodClaimed;
        private bool isDisposed;
        private readonly object endGameLock = new object();

        public GameSession(
            string lobbyCode,
            int matchId,
            int puzzleId,
            PuzzleDefinitionDto puzzleDefinition,
            IMatchmakingRepository matchmakingRepository,
            StatsLogic statsLogic,
            IPuzzleRepository puzzleRepository,
            Action<string> onSessionEndedCleanup,
            IScoreCalculator scoreCalculator)
        {
            LobbyCode = lobbyCode;
            MatchId = matchId;
            PuzzleId = puzzleId;
            StartTime = DateTime.UtcNow;
            PuzzleDefinition = puzzleDefinition;

            this.matchmakingRepository = matchmakingRepository;
            this.statsLogic = statsLogic;
            this.puzzleRepository = puzzleRepository;
            this.onSessionEndedCleanup = onSessionEndedCleanup;
            this.scoreCalculator = scoreCalculator;

            initializePieceStates();

            hoardingTimer = new Timer(1000);
            hoardingTimer.Elapsed += checkHoarding;
            hoardingTimer.AutoReset = true;
            hoardingTimer.Start();
        }

        public void startMatchTimer(int durationSeconds)
        {
            int effectiveDuration = durationSeconds > 0 ? durationSeconds : DEFAULT_MATCH_DURATION_SECONDS;
            gameDurationTimer = new Timer(effectiveDuration * MILLISECONDS_PER_SECOND);
            gameDurationTimer.Elapsed += onMatchTimeExpired;
            gameDurationTimer.AutoReset = false;
            gameDurationTimer.Enabled = true;
        }

        public void addPlayer(int playerId, string username, string avatarPath, IMatchmakingCallback callback)
        {
            var playerData = new PlayerSessionData
            {
                PlayerId = playerId,
                Username = username,
                AvatarPath = avatarPath,
                Callback = callback,
                Score = 0,
                PiecesPlaced = 0,
                CurrentStreak = 0,
                NegativeStreak = 0,
                RecentPlacementTimestamps = new List<DateTime>()
            };

            Players.TryAdd(playerId, playerData);
        }

        public PlayerSessionData removePlayer(int playerId)
        {
            if (!Players.TryRemove(playerId, out var playerData))
            {
                return null;
            }

            releaseHeldPieces(playerId);

            return playerData;
        }

        public void releaseHeldPieces(int playerId)
        {
            var heldPieces = PieceStates.Values.Where(p => p.HeldByPlayerId == playerId).ToList();

            foreach (var piece in heldPieces)
            {
                piece.HeldByPlayerId = null;
                piece.GrabTime = null;
                logger.Warn("Piece {PieceId} was force-released due to player {PlayerId} disconnect.",
                    piece.PieceId, playerId);

                if (Players.TryGetValue(playerId, out var player))
                {
                    broadcast(callback => callback.onPieceDragReleased(piece.PieceId, player.Username));
                }
            }
        }

        public void handlePieceDrag(int playerId, int pieceId)
        {
            if (isGameEnded) return;

            if (!PieceStates.TryGetValue(pieceId, out var pieceState))
            {
                logger.Warn("Player {PlayerId} tried to drag non-existent piece {PieceId}", playerId, pieceId);
                return;
            }

            if (!Players.TryGetValue(playerId, out var player))
            {
                logger.Error("Player {PlayerId} not found in session", playerId);
                return;
            }

            lock (pieceState)
            {
                if (pieceState.IsPlaced)
                {
                    logger.Warn("Player {PlayerId} tried to drag placed piece {PieceId}", playerId, pieceId);
                    return;
                }

                if (pieceState.HeldByPlayerId.HasValue && pieceState.HeldByPlayerId.Value != playerId)
                {
                    logger.Warn("Player {PlayerId} tried to drag piece {PieceId} already held by {OtherPlayerId}",
                        playerId, pieceId, pieceState.HeldByPlayerId.Value);
                    return;
                }

                pieceState.HeldByPlayerId = playerId;
                pieceState.GrabTime = DateTime.UtcNow;
            }

            broadcast(callback => callback.onPieceDragStarted(pieceId, player.Username));
        }

        public void handlePieceMove(int playerId, int pieceId, double newX, double newY)
        {
            if (isGameEnded) return;

            if (!PieceStates.TryGetValue(pieceId, out var pieceState))
            {
                return;
            }

            if (pieceState.HeldByPlayerId != playerId)
            {
                return;
            }

            pieceState.CurrentX = newX;
            pieceState.CurrentY = newY;

            if (Players.TryGetValue(playerId, out var player))
            {
                broadcast(callback => callback.onPieceMoved(pieceId, newX, newY, player.Username));
            }
        }

        public async Task handlePieceDrop(int playerId, int pieceId, double newX, double newY)
        {
            if (isGameEnded) return;

            if (!PieceStates.TryGetValue(pieceId, out var pieceState))
            {
                return;
            }

            if (pieceState.HeldByPlayerId != playerId)
            {
                return;
            }

            if (!Players.TryGetValue(playerId, out var player))
            {
                return;
            }

            pieceState.HeldByPlayerId = null;
            pieceState.GrabTime = null;

            double distanceToOwnTarget = calculateDistance(newX, newY, pieceState.FinalX, pieceState.FinalY);
            bool isNearOwnTarget = distanceToOwnTarget < SNAP_TOLERANCE;
            bool isClosestToOwn = isClosestToOwnPosition(pieceId, newX, newY, distanceToOwnTarget);

            bool isCorrect = !pieceState.IsPlaced && isNearOwnTarget && isClosestToOwn;

            if (isCorrect)
            {
                await handleCorrectPlacementAsync(player, pieceState);
            }
            else
            {
                handleIncorrectPlacement(player, pieceState, newX, newY);
            }
        }

        private bool isClosestToOwnPosition(int pieceId, double dropX, double dropY, double distanceToOwn)
        {
            foreach (var otherState in PieceStates.Values)
            {
                if (otherState.PieceId == pieceId) continue;

                double distanceToOther = calculateDistance(dropX, dropY, otherState.FinalX, otherState.FinalY);

                if (distanceToOther <= distanceToOwn)
                {
                    return false;
                }
            }
            return true;
        }

        

        
        private bool isClosestToCorrectPosition(int pieceId, double dropX, double dropY, double distanceToOwn)
        {
            foreach (var otherState in PieceStates.Values)
            {
                if (otherState.PieceId == pieceId) continue;
                if (otherState.IsPlaced) continue; 

                double distanceToOther = calculateDistance(dropX, dropY, otherState.FinalX, otherState.FinalY);

                if (distanceToOther < distanceToOwn)
                {
                    logger.Debug("Piece {PieceId} dropped closer to position of piece {OtherId} ({DistOther:F1}) than own ({DistOwn:F1})",
                        pieceId, otherState.PieceId, distanceToOther, distanceToOwn);
                    return false;
                }
            }
            return true;
        }

        
        public void handlePieceRelease(int playerId, int pieceId)
        {
            if (isGameEnded) return;

            if (!PieceStates.TryGetValue(pieceId, out var pieceState))
            {
                return;
            }

            if (pieceState.HeldByPlayerId != playerId)
            {
                logger.Warn("Player {PlayerId} tried to release piece {PieceId} held by {OtherPlayerId}",
                    playerId, pieceId, pieceState.HeldByPlayerId);
                return;
            }

            logger.Info("GameSession {LobbyCode}: Player {PlayerId} released piece {PieceId}",
                LobbyCode, playerId, pieceId);

            pieceState.HeldByPlayerId = null;
            pieceState.GrabTime = null;

            if (Players.TryGetValue(playerId, out var player))
            {
                broadcast(callback => callback.onPieceDragReleased(pieceId, player.Username));
            }
        }

        public async Task kickPlayerAsync(int playerId, int reasonId, int hostPlayerId)
        {
            if (Players.TryGetValue(playerId, out var playerSession))
            {
                try
                {
                    string kickMessageCode = reasonId == KICK_REASON_PROFANITY_ID
                        ? MessageCodes.NOTIFY_KICKED_PROFANITY
                        : MessageCodes.NOTIFY_KICKED_BY_HOST;

                    playerSession.Callback?.kickedFromLobby(kickMessageCode);
                }
                catch (CommunicationException ex)
                {
                    logger.Warn(ex, "Could not notify player {0} of kick", playerSession.Username);
                }
                catch (ObjectDisposedException ex)
                {
                    logger.Warn(ex, "Channel disposed for player {0}", playerSession.Username);
                }
                catch (TimeoutException ex)
                {
                    logger.Warn(ex, "Timeout notifying player {0} of kick", playerSession.Username);
                }
            }

            var expulsionDto = new ExpulsionDto
            {
                MatchId = MatchId,
                PlayerId = playerId,
                ReasonId = reasonId,
                HostPlayerId = hostPlayerId
            };

            await matchmakingRepository.registerExpulsionAsync(expulsionDto);

            removePlayer(playerId);
        }

        public bool isPuzzleComplete()
        {
            return PieceStates.Values.All(p => p.IsPlaced);
        }

        public void broadcast(Action<IMatchmakingCallback> action)
        {
            var players = Players.Values;

            if (action == null)
            {
                return;
            }

            foreach (var player in players)
            {
                if (player?.Callback == null)
                {
                    continue;
                }

                try
                {
                    action(player.Callback);
                }
                catch (CommunicationException ex)
                {
                    logger.Warn(ex, "Failed to broadcast to {0}", player.Username);
                }
                catch (ObjectDisposedException ex)
                {
                    logger.Warn(ex, "Channel disposed for {0}", player.Username);
                }
                catch (TimeoutException ex)
                {
                    logger.Warn(ex, "Timeout broadcasting to {0}", player.Username);
                }
            }
        }

        public int? getPlayerIdByUsername(string username)
        {
            var playerEntry = Players.FirstOrDefault(p => p.Value.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (playerEntry.Value != null)
            {
                return playerEntry.Key;
            }
            return null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async Task handlePlayerVoluntaryLeaveAsync(string username)
        {
            var playerEntry = Players.FirstOrDefault(p => p.Value.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

            if (playerEntry.Value == null)
            {
                logger.Warn("Player {Username} not found in session {LobbyCode} during leave request.", username, LobbyCode);
                return;
            }

            int playerId = playerEntry.Key;
            var duration = DateTime.UtcNow - StartTime;
            int minutes = Math.Max(MIN_PLAYTIME_MINUTES, (int)duration.TotalMinutes);

            await statsLogic.updatePlaytimeOnly(username, minutes);

            if (Players.TryRemove(playerId, out _))
            {
                releaseHeldPieces(playerId);
                broadcast(callback => callback.onPlayerLeftMatch(username));
            }

            if (Players.Count < MIN_PLAYERS_TO_CONTINUE)
            {
                if (Players.Count == 1)
                {
                    await endGameAsync(END_REASON_FORFEIT);
                }
                else
                {
                    logger.Info("All players left Lobby {LobbyCode}. Disposing session.", LobbyCode);
                    stopTimers();
                    onSessionEndedCleanup?.Invoke(LobbyCode);
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (isDisposed) return;

            if (disposing)
            {
                hoardingTimer?.Stop();
                hoardingTimer?.Dispose();
                gameDurationTimer?.Stop();
                gameDurationTimer?.Dispose();
            }

            isDisposed = true;
        }

        private void initializePieceStates()
        {
            foreach (var pieceDef in PuzzleDefinition.Pieces)
            {
                var pieceState = new PuzzlePieceState
                {
                    PieceId = pieceDef.PieceId,
                    FinalX = pieceDef.CorrectX,
                    FinalY = pieceDef.CorrectY,
                    CurrentX = pieceDef.InitialX,
                    CurrentY = pieceDef.InitialY,
                    IsPlaced = false,
                    HeldByPlayerId = null,
                    GrabTime = null
                };

                PieceStates.TryAdd(pieceDef.PieceId, pieceState);
            }
        }

        private void checkHoarding(object sender, ElapsedEventArgs e)
        {
            if (isGameEnded) return;

            var now = DateTime.UtcNow;
            var hoardingPieces = PieceStates.Values
                .Where(p => p.HeldByPlayerId.HasValue && p.GrabTime.HasValue &&
                            (now - p.GrabTime.Value).TotalSeconds > HOARDING_LIMIT_SECONDS)
                .ToList();

            foreach (var piece in hoardingPieces)
            {
                int playerId = piece.HeldByPlayerId.Value;

                piece.HeldByPlayerId = null;
                piece.GrabTime = null;

                if (Players.TryGetValue(playerId, out var player))
                {
                    player.Score -= PENALTY_HOARDING;
                    player.CurrentStreak = 0;

                    broadcast(cb => cb.onPieceDragReleased(piece.PieceId, player.Username));
                    broadcast(cb => cb.onPlayerPenalty(player.Username, PENALTY_HOARDING, player.Score, PENALTY_REASON_HOARDING));
                }
            }
        }

        private async Task handleCorrectPlacementAsync(PlayerSessionData player, PuzzlePieceState pieceState)
        {
            pieceState.IsPlaced = true;
            pieceState.CurrentX = pieceState.FinalX;
            pieceState.CurrentY = pieceState.FinalY;
            player.NegativeStreak = 0;
            player.PiecesPlaced++;

            var scoreContext = new ScoreCalculationContext
            {
                Player = player,
                PieceId = pieceState.PieceId,
                IsEdgePiece = isEdgePiece(pieceState.PieceId),
                IsFirstBloodAvailable = !isFirstBloodClaimed,
                IsPuzzleComplete = isPuzzleComplete()
            };

            var scoreResult = scoreCalculator.calculatePointsForPlacement(scoreContext);

            if (scoreResult.ClaimedFirstBlood)
            {
                isFirstBloodClaimed = true;
            }

            player.Score += scoreResult.Points;

            broadcast(callback => callback.onPiecePlaced(
                pieceState.PieceId,
                pieceState.FinalX,
                pieceState.FinalY,
                player.Username,
                player.Score,
                scoreResult.BonusType));

            if (isPuzzleComplete())
            {
                await endGameAsync(END_REASON_PUZZLE_SOLVED);
            }
        }

        private void handleIncorrectPlacement(PlayerSessionData player, PuzzlePieceState pieceState, double newX, double newY)
        {
            pieceState.CurrentX = newX;
            pieceState.CurrentY = newY;

            broadcast(callback => callback.onPieceMoved(pieceState.PieceId, newX, newY, player.Username));
            broadcast(callback => callback.onPieceDragReleased(pieceState.PieceId, player.Username));

            bool isNearAnyTarget = isNearAnyPiecePosition(newX, newY);

            if (!isNearAnyTarget)
            {
                return;
            }

            player.NegativeStreak++;
            int penaltyPoints = scoreCalculator.calculatePenaltyPoints(player.NegativeStreak);
            player.Score -= penaltyPoints;

            double distanceToTarget = calculateDistance(newX, newY, pieceState.FinalX, pieceState.FinalY);
            bool isNearOwnSpot = distanceToTarget < PENALTY_TOLERANCE;
            string reason = isNearOwnSpot ? PENALTY_REASON_MISS : PENALTY_REASON_WRONG_SPOT;

            broadcast(cb => cb.onPlayerPenalty(player.Username, penaltyPoints, player.Score, reason));
        }
        private bool isNearAnyPiecePosition(double x, double y)
        {
            foreach (var state in PieceStates.Values)
            {
                double dist = calculateDistance(x, y, state.FinalX, state.FinalY);
                if (dist < PENALTY_TOLERANCE)
                {
                    return true;
                }
            }
            return false;
        }

        private bool isEdgePiece(int pieceId)
        {
            var pieceDef = PuzzleDefinition.Pieces.FirstOrDefault(p => p.PieceId == pieceId);
            if (pieceDef == null) return false;

            return pieceDef.TopNeighborId == null ||
                   pieceDef.BottomNeighborId == null ||
                   pieceDef.LeftNeighborId == null ||
                   pieceDef.RightNeighborId == null;
        }

     

        private static double calculateDistance(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        }

        private void onMatchTimeExpired(object sender, ElapsedEventArgs e)
        {
            Task.Run(() => endGameAsync(END_REASON_TIMEOUT)).ConfigureAwait(false);
        }

        private async Task endGameAsync(string reason)
        {
            if (!trySetGameEnded())
            {
                return;
            }

            stopTimers();

            var (minutesPlayed, totalSeconds) = calculateDuration();
            var rankedPlayers = getRankedPlayers();
            var clientResults = new List<PlayerResultDto>();

            try
            {
                var (matchEntity, puzzleEntity) = await fetchGameEntitiesAsync(matchmakingRepository, puzzleRepository);

                var context = new EndGameProcessingContext
                {
                    MatchRepo = matchmakingRepository,
                    StatsService = statsLogic,
                    MatchEntity = matchEntity,
                    PuzzleEntity = puzzleEntity,
                    MatchId = MatchId,
                    MinutesPlayed = minutesPlayed,
                    TotalParticipants = rankedPlayers.Count
                };

                clientResults = await processAllPlayersAsync(rankedPlayers, context);

                await matchmakingRepository.finishMatchAsync(MatchId);
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "Error during endGame execution for Lobby {0}", LobbyCode);
            }

            broadcastGameEnd(reason, totalSeconds, clientResults);
            onSessionEndedCleanup?.Invoke(LobbyCode);
        }

        private bool trySetGameEnded()
        {
            lock (endGameLock)
            {
                if (isGameEnded) return false;
                isGameEnded = true;
                return true;
            }
        }

        private void stopTimers()
        {
            gameDurationTimer?.Stop();
            hoardingTimer?.Stop();
        }

        private (int minutes, double totalSeconds) calculateDuration()
        {
            var duration = DateTime.UtcNow - StartTime;
            int minutes = Math.Max(MIN_PLAYTIME_MINUTES, (int)duration.TotalMinutes);
            return (minutes, duration.TotalSeconds);
        }

        private List<PlayerSessionData> getRankedPlayers()
        {
            return Players.Values
                .OrderByDescending(p => p.Score)
                .ThenByDescending(p => p.PiecesPlaced)
                .ToList();
        }

        private async Task<(Matches match, Puzzles puzzle)> fetchGameEntitiesAsync(
            IMatchmakingRepository matchRepo,
            IPuzzleRepository puzzleRepo)
        {
            var matchEntity = await matchRepo.getMatchByIdAsync(MatchId);
            if (matchEntity != null)
            {
                matchEntity.end_time = DateTime.UtcNow;
            }

            var puzzleEntity = await puzzleRepo.getPuzzleByIdAsync(PuzzleId);

            return (matchEntity, puzzleEntity);
        }

        private async Task<List<PlayerResultDto>> processAllPlayersAsync(
            List<PlayerSessionData> rankedPlayers,
            EndGameProcessingContext context)
        {
            var results = new List<PlayerResultDto>();

            bool isZeroActionDraw = rankedPlayers.Any() && rankedPlayers.All(p => p.Score == 0 && p.PiecesPlaced == 0);

            int currentRank = 1;

            foreach (var player in rankedPlayers)
            {
                int effectiveRank = isZeroActionDraw ? 1 : currentRank;
                bool isWinner = !isZeroActionDraw && (currentRank == 1);

                var resultDto = await processSinglePlayerAsync(player, effectiveRank, isWinner, context);
                results.Add(resultDto);

                if (!isZeroActionDraw)
                {
                    currentRank++;
                }
            }

            return results;
        }

        private async Task<PlayerResultDto> processSinglePlayerAsync(
            PlayerSessionData player,
            int rank,
            bool isWinner,
            EndGameProcessingContext context)
        {
            var unlockedIds = new List<int>();

            if (player.PlayerId > 0)
            {
                unlockedIds = await handlePlayerStatsAndAchievementsAsync(player, rank, isWinner, context);

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

            return new PlayerResultDto
            {
                PlayerId = player.PlayerId,
                Username = player.Username,
                AvatarPath = player.AvatarPath,
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
            bool isWinner,
            EndGameProcessingContext context)
        {
            var newUnlockedIds = new List<int>();

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

            var achievementContext = new AchievementContext
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

            var qualifiedAchievements = AchievementEvaluator.evaluate(achievementContext);
            newUnlockedIds = await context.StatsService.unlockAchievementsAsync(player.PlayerId, qualifiedAchievements);

            return newUnlockedIds;
        }

        private void broadcastGameEnd(string reason, double totalSeconds, List<PlayerResultDto> results)
        {
            var matchEndResult = new MatchEndResultDto
            {
                MatchId = MatchId,
                Reason = reason,
                TotalTimeElapsedSeconds = totalSeconds,
                PlayerResults = results
            };

            broadcast(callback => callback.onGameEnded(matchEndResult));
        }
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