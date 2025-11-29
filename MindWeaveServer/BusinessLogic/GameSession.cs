using Autofac.Features.OwnedInstances;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Game;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

        public string LobbyCode { get; }
        public int PuzzleId { get; }
        public int MatchId { get; }
        public PuzzleDefinitionDto PuzzleDefinition { get; }
        public DateTime StartTime { get; }
        public ConcurrentDictionary<int, PlayerSessionData> Players { get; } = new ConcurrentDictionary<int, PlayerSessionData>();
        public ConcurrentDictionary<int, PuzzlePieceState> PieceStates { get; } = new ConcurrentDictionary<int, PuzzlePieceState>();

        private readonly Func<Owned<IMatchmakingRepository>> matchmakingFactory;
        private readonly Func<Owned<StatsLogic>> statsLogicFactory;
        private readonly Func<Owned<IPuzzleRepository>> puzzleFactory;
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
            Func<Owned<IMatchmakingRepository>> matchmakingFactory,
            Func<Owned<StatsLogic>> statsLogicFactory,
            Func<Owned<IPuzzleRepository>> puzzleFactory,
            Action<string> onSessionEndedCleanup,
            IScoreCalculator scoreCalculator)
        {
            LobbyCode = lobbyCode ?? throw new ArgumentNullException(nameof(lobbyCode));
            MatchId = matchId;
            PuzzleId = puzzleId;
            PuzzleDefinition = puzzleDefinition ?? throw new ArgumentNullException(nameof(puzzleDefinition));
            StartTime = DateTime.UtcNow;

            this.matchmakingFactory = matchmakingFactory ?? throw new ArgumentNullException(nameof(matchmakingFactory));
            this.statsLogicFactory = statsLogicFactory ?? throw new ArgumentNullException(nameof(statsLogicFactory));
            this.puzzleFactory = puzzleFactory ?? throw new ArgumentNullException(nameof(puzzleFactory));
            this.onSessionEndedCleanup = onSessionEndedCleanup;
            this.scoreCalculator = scoreCalculator ?? throw new ArgumentNullException(nameof(scoreCalculator));

            logger.Info("Initializing GameSession for Lobby {LobbyCode}", LobbyCode);

            initializePieceStates();

            hoardingTimer = new Timer(1000);
            hoardingTimer.Elapsed += checkHoarding;
            hoardingTimer.AutoReset = true;
            hoardingTimer.Start();
        }

        public void startMatchTimer(int durationSeconds)
        {
            int effectiveDuration = durationSeconds > 0 ? durationSeconds : DEFAULT_MATCH_DURATION_SECONDS;

            logger.Info("Starting Match Timer for Lobby {LobbyCode}: {Duration} seconds.", LobbyCode, effectiveDuration);

            gameDurationTimer = new Timer(effectiveDuration * 1000);
            gameDurationTimer.Elapsed += onMatchTimeExpired;
            gameDurationTimer.AutoReset = false;
            gameDurationTimer.Enabled = true;
        }

        public void addPlayer(int playerId, string username, IMatchmakingCallback callback)
        {
            var playerData = new PlayerSessionData
            {
                PlayerId = playerId,
                Username = username,
                Callback = callback,
                Score = 0,
                PiecesPlaced = 0,
                CurrentStreak = 0,
                NegativeStreak = 0,
                RecentPlacementTimestamps = new List<DateTime>()
            };

            Players.TryAdd(playerId, playerData);
            logger.Debug("Player {Username} (ID: {PlayerId}) added to GameSession {LobbyCode}",
                username, playerId, LobbyCode);
        }

        public PlayerSessionData removePlayer(int playerId)
        {
            if (!Players.TryRemove(playerId, out var playerData))
            {
                return null;
            }

            logger.Debug("Player {Username} (ID: {PlayerId}) removed from GameSession {LobbyCode}",
                playerData.Username, playerId, LobbyCode);

            releaseHeldPieces(playerId);

            Task.Run(async () =>
            {
                try
                {
                    using (var scope = matchmakingFactory())
                    {
                        await scope.Value.updatePlayerScoreAsync(MatchId, playerId, playerData.Score);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error saving score on disconnect for player {PlayerId}", playerId);
                }
            });

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
                logger.Warn("Player {PlayerId} tried to drop non-existent piece {PieceId}", playerId, pieceId);
                return;
            }

            if (pieceState.HeldByPlayerId != playerId)
            {
                logger.Warn("Player {PlayerId} tried to drop piece {PieceId} but it's held by {HeldBy}",
                    playerId, pieceId, pieceState.HeldByPlayerId);
                return;
            }

            if (!Players.TryGetValue(playerId, out var player))
            {
                return;
            }

            pieceState.HeldByPlayerId = null;
            pieceState.GrabTime = null;

            bool isCorrect = !pieceState.IsPlaced &&
                             Math.Abs(pieceState.FinalX - newX) < SNAP_TOLERANCE &&
                             Math.Abs(pieceState.FinalY - newY) < SNAP_TOLERANCE;

            if (isCorrect)
            {
                await handleCorrectPlacementAsync(player, pieceState);
            }
            else
            {
                handleIncorrectPlacement(player, pieceState, newX, newY);
            }
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
                    string kickMessage = reasonId == 2 ? Lang.KickMessageProfanity : Lang.KickedByHost;
                    playerSession.Callback?.kickedFromLobby(kickMessage);
                }
                catch (Exception ex)
                {
                    logger.Warn("No se pudo notificar al jugador {Username} de su expulsión: {Message}",
                        playerSession.Username, ex.Message);
                }
            }

            try
            {
                using (var scope = matchmakingFactory())
                {
                    var repo = scope.Value;
                    var expulsionDto = new ExpulsionDto
                    {
                        MatchId = MatchId,
                        PlayerId = playerId,
                        ReasonId = reasonId,
                        HostPlayerId = hostPlayerId
                    };

                    await repo.registerExpulsionAsync(expulsionDto);
                    logger.Info("Expulsion recorded for Player {PlayerId} in Match {MatchId}. Reason: {ReasonId}",
                        playerId, MatchId, reasonId);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to register expulsion in DB for player {PlayerId}", playerId);
            }

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
                catch (Exception ex)
                {
                    logger.Warn(ex, "Failed to broadcast to player {Username}", player.Username);
                }
            }
        }

        public void Dispose()
        {
            dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void dispose(bool disposing)
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
            logger.Debug("GameSession {LobbyCode} disposed.", LobbyCode);
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

                    logger.Info("Player {Username} penalized for Hoarding piece {PieceId}",
                        player.Username, piece.PieceId);

                    broadcast(cb => cb.onPieceDragReleased(piece.PieceId, player.Username));
                    broadcast(cb => cb.onPlayerPenalty(player.Username, PENALTY_HOARDING, player.Score, "HOARDING"));
                }
            }
        }

        private async Task handleCorrectPlacementAsync(PlayerSessionData player, PuzzlePieceState pieceState)
        {
            logger.Info("GameSession {LobbyCode}: Player {PlayerId} SNAPPED piece {PieceId}",
                LobbyCode, player.PlayerId, pieceState.PieceId);

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
                logger.Info("Puzzle completed in Lobby {LobbyCode}. Saving all scores to DB.", LobbyCode);
                await endGameAsync("PuzzleSolved");
            }
        }

        private void handleIncorrectPlacement(PlayerSessionData player, PuzzlePieceState pieceState, double newX, double newY)
        {
            pieceState.CurrentX = newX;
            pieceState.CurrentY = newY;

            broadcast(callback => callback.onPieceMoved(pieceState.PieceId, newX, newY, player.Username));
            broadcast(callback => callback.onPieceDragReleased(pieceState.PieceId, player.Username));

            double distanceToTarget = calculateDistance(newX, newY, pieceState.FinalX, pieceState.FinalY);
            bool isNearMiss = distanceToTarget < PENALTY_TOLERANCE;
            bool isWrongHole = checkIfDroppedOnWrongPiece(newX, newY, pieceState.PieceId);

            if (!isNearMiss && !isWrongHole)
            {
                return;
            }

            player.NegativeStreak++;
            int penaltyPoints = scoreCalculator.calculatePenaltyPoints(player.NegativeStreak);
            player.Score -= penaltyPoints;

            string reason = isWrongHole ? "WRONG_SPOT" : "MISS";

            logger.Info("Player {Username} penalized: {Reason} (-{Points}). Streak: {Streak}",
                player.Username, reason, penaltyPoints, player.NegativeStreak);

            broadcast(cb => cb.onPlayerPenalty(player.Username, penaltyPoints, player.Score, reason));
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

        private bool checkIfDroppedOnWrongPiece(double x, double y, int currentPieceId)
        {
            foreach (var otherState in PieceStates.Values)
            {
                if (otherState.PieceId == currentPieceId) continue;
                if (otherState.IsPlaced) continue;

                double dist = calculateDistance(x, y, otherState.FinalX, otherState.FinalY);
                if (dist < SNAP_TOLERANCE)
                {
                    return true;
                }
            }
            return false;
        }

        private double calculateDistance(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        }

        private void onMatchTimeExpired(object sender, ElapsedEventArgs e)
        {
            Task.Run(() => endGameAsync("TimeOut")).ConfigureAwait(false);
        }

        private async Task endGameAsync(string reason)
        {
            if (!trySetGameEnded())
            {
                return;
            }

            stopTimers();
            logger.Info("Ending Game for Lobby {LobbyCode}. Reason: {Reason}", LobbyCode, reason);

            var (minutesPlayed, totalSeconds) = calculateDuration();
            var rankedPlayers = getRankedPlayers();
            var clientResults = new List<PlayerResultDto>();

            try
            {
                using (var matchScope = matchmakingFactory())
                using (var puzzleScope = puzzleFactory())
                using (var statsScope = statsLogicFactory())
                {
                    var matchRepo = matchScope.Value;
                    var puzzleRepo = puzzleScope.Value;
                    var statsService = statsScope.Value;

                    var (matchEntity, puzzleEntity) = await fetchGameEntitiesAsync(matchRepo, puzzleRepo);

                    var context = new EndGameProcessingContext
                    {
                        MatchRepo = matchRepo,
                        StatsService = statsService,
                        MatchEntity = matchEntity,
                        PuzzleEntity = puzzleEntity,
                        MatchId = MatchId,
                        MinutesPlayed = minutesPlayed,
                        TotalParticipants = rankedPlayers.Count
                    };

                    clientResults = await processAllPlayersAsync(rankedPlayers, context);
                    await matchRepo.finishMatchAsync(MatchId);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Critical error during endGame execution for Lobby {LobbyCode}", LobbyCode);
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
            int minutes = Math.Max(1, (int)duration.TotalMinutes);
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
            Matches matchEntity = null;
            Puzzles puzzleEntity = null;

            try
            {
                matchEntity = await matchRepo.getMatchByIdAsync(MatchId);
                if (matchEntity != null)
                {
                    matchEntity.end_time = DateTime.UtcNow;
                }

                puzzleEntity = await puzzleRepo.getPuzzleByIdAsync(PuzzleId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Could not fetch Match/Puzzle entities.");
            }

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
                try
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
                catch (Exception ex)
                {
                    logger.Error(ex, "Error processing/saving data for player {Username}", player.Username);
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

            var qualifiedAchievements = AchievementEvaluator.Evaluate(achievementContext);
            newUnlockedIds = await context.StatsService.unlockAchievementsAsync(player.PlayerId, qualifiedAchievements);

            if (newUnlockedIds.Any())
            {
                logger.Info("User {Username} unlocked {Count} achievements.", player.Username, newUnlockedIds.Count);
            }

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

        ~GameSession()
        {
            dispose(false);
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