using Autofac.Features.OwnedInstances;
using MindWeaveServer.BusinessLogic;
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
using System.Numerics;
using System.Threading.Tasks;
using System.Timers;

namespace MindWeaveServer.BusinessLogic
{
    public class PlayerSessionData
    {
        public int PlayerId { get; set; }
        public string Username { get; set; }
        public IMatchmakingCallback Callback { get; set; }
        public int Score { get; set; }
        public int PiecesPlaced { get; set; }
        public int CurrentStreak { get; set; }
        public int NegativeStreak { get; set; }
        public List<DateTime> RecentPlacementTimestamps { get; set; } = new List<DateTime>();
    }

    public class PuzzlePieceState
    {
        public int PieceId { get; set; }
        public double FinalX { get; set; }
        public double FinalY { get; set; }
        public double CurrentX { get; set; }
        public double CurrentY { get; set; }
        public bool IsPlaced { get; set; }
        public int? HeldByPlayerId { get; set; }
        public DateTime? GrabTime { get; set; }
    }

    public class GameSession
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

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

        private readonly Timer hoardingTimer;
        private Timer gameDurationTimer;
        private bool isGameEnded = false;
        private readonly object endGameLock = new object();
        private readonly Action<string> onSessionEndedCleanup;

        private const double SNAP_TOLERANCE = 30.0;
        private const double PENALTY_TOLERANCE = 60.0;
        private const int HOARDING_LIMIT_SECONDS = 10;

        private const int SCORE_EDGE_PIECE = 5;
        private const int SCORE_CENTER_PIECE = 10;
        private const int SCORE_STREAK_BONUS = 10;
        private const int SCORE_FRENZY_BONUS = 40;
        private const int SCORE_FIRST_BLOOD = 25;
        private const int SCORE_LAST_HIT = 50;

        private const int PENALTY_BASE_MISS = 5;
        private const int PENALTY_HOARDING = 15;

        private bool isFirstBloodClaimed = false;
        private const int STREAK_THRESHOLD = 3;
        private const int FRENZY_COUNT = 5;
        private const int FRENZY_TIME_WINDOW_SECONDS = 60;

        public GameSession(
            string lobbyCode,
            int matchId,
            int puzzleId,
            PuzzleDefinitionDto puzzleDefinition,
            Func<Owned<IMatchmakingRepository>> matchmakingFactory,
            Func<Owned<StatsLogic>> statsLogicFactory,
            Func<Owned<IPuzzleRepository>> puzzleFactory,
            Action<string> onSessionEndedCleanup)
        {
            LobbyCode = lobbyCode;
            MatchId = matchId;
            PuzzleId = puzzleId;
            this.matchmakingFactory = matchmakingFactory;
            this.PuzzleDefinition = puzzleDefinition;
            this.statsLogicFactory = statsLogicFactory;
            this.onSessionEndedCleanup = onSessionEndedCleanup;
            this.puzzleFactory = puzzleFactory;
            this.StartTime = DateTime.UtcNow;

            logger.Info("Initializing GameSession for Lobby {LobbyCode}", LobbyCode);

            foreach (var pieceDef in this.PuzzleDefinition.Pieces)
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

            hoardingTimer = new Timer(1000);
            hoardingTimer.Elapsed += checkHoarding;
            hoardingTimer.AutoReset = true;
            hoardingTimer.Start();
        }

        public void startMatchTimer(int durationSeconds)
        {
            if (durationSeconds <= 0) durationSeconds = 300; // Fallback 5 min

            logger.Info($"Starting Match Timer for Lobby {LobbyCode}: {durationSeconds} seconds.");

            gameDurationTimer = new Timer(durationSeconds * 1000);
            gameDurationTimer.Elapsed += (s, e) => endGame("TimeOut");
            gameDurationTimer.AutoReset = false;
            gameDurationTimer.Enabled = true;
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

                    logger.Info($"Player {player.Username} penalized for Hoarding piece {piece.PieceId}");

                    broadcast(cb => cb.onPieceDragReleased(piece.PieceId, player.Username));
                    broadcast(cb => cb.onPlayerPenalty(player.Username, PENALTY_HOARDING, player.Score, "HOARDING"));
                }
            }
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
            logger.Debug("Player {Username} (ID: {PlayerId}) added to GameSession {LobbyCode}", username, playerId, LobbyCode);
        }

        public PlayerSessionData removePlayer(int playerId)
        {
            Players.TryRemove(playerId, out var playerData);
            if (playerData != null)
            {
                logger.Debug("Player {Username} (ID: {PlayerId}) removed from GameSession {LobbyCode}", playerData.Username, playerId, LobbyCode);
                releaseHeldPieces(playerId);

                // Opcional: Si un jugador se va antes de terminar, ¿guardamos su score parcial?
                // Si quisieras guardarlo al salir, descomenta esto:
                /*
                Task.Run(async () => {
                    try { await matchmakingRepository.updatePlayerScoreAsync(MatchId, playerId, playerData.Score); }
                    catch(Exception ex) { logger.Error(ex, "Error saving score on disconnect"); }
                });
                */
            }
            return playerData;
        }

        public void releaseHeldPieces(int playerId)
        {
            var heldPieces = PieceStates.Values.Where(p => p.HeldByPlayerId == playerId).ToList();
            foreach (var piece in heldPieces)
            {
                piece.HeldByPlayerId = null;
                piece.GrabTime = null;
                logger.Warn("Piece {PieceId} was force-released due to player {PlayerId} disconnect.", piece.PieceId, playerId);

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

            if (!Players.TryGetValue(playerId, out var player))
            {
                return;
            }
            broadcast(callback => callback.onPieceMoved(pieceId, newX, newY, player.Username));
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

            double distanceToTarget = calculateDistance(newX, newY, pieceState.FinalX, pieceState.FinalY);
            bool isCorrect = !pieceState.IsPlaced &&
                             Math.Abs(pieceState.FinalX - newX) < SNAP_TOLERANCE &&
                             Math.Abs(pieceState.FinalY - newY) < SNAP_TOLERANCE;

            if (isCorrect)
            {
                logger.Info("GameSession {LobbyCode}: Player {PlayerId} SNAPPED piece {PieceId}",
                    LobbyCode, playerId, pieceId);

                pieceState.IsPlaced = true;
                pieceState.CurrentX = pieceState.FinalX;
                pieceState.CurrentY = pieceState.FinalY;
                player.NegativeStreak = 0;
                player.PiecesPlaced++;

                var (points, bonusType) = calculatePointsForPlacement(player, pieceId);
                player.Score += points;

                broadcast(callback => callback.onPiecePlaced(
                    pieceId,
                    pieceState.FinalX,
                    pieceState.FinalY,
                    player.Username,
                    player.Score,
                    bonusType
                ));

                if (isPuzzleComplete())
                {
                    logger.Info("Puzzle completed in Lobby {LobbyCode}. Saving all scores to DB.", LobbyCode);

                    endGame("PuzzleSolved");
                }
            }
            else
            {
                pieceState.CurrentX = newX;
                pieceState.CurrentY = newY;
                broadcast(callback => callback.onPieceMoved(pieceId, newX, newY, player.Username));

                bool isNearMiss = distanceToTarget < PENALTY_TOLERANCE;
                bool isWrongHole = checkIfDroppedOnWrongPiece(newX, newY, pieceId);

                if (isNearMiss || isWrongHole)
                {
                    player.NegativeStreak++;

                    int penaltyPoints = PENALTY_BASE_MISS * player.NegativeStreak;

                    player.Score -= penaltyPoints;

                    string reason = isWrongHole ? "WRONG_SPOT" : "MISS";

                    logger.Info($"Player {player.Username} penalized: {reason} (-{penaltyPoints}). Streak: {player.NegativeStreak}");

                    broadcast(cb => cb.onPlayerPenalty(player.Username, penaltyPoints, player.Score, reason));
                }
            }
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

            if (!Players.TryGetValue(playerId, out var player))
            {
                return;
            }

            broadcast(callback => callback.onPieceDragReleased(pieceId, player.Username));
        }

        public void broadcast(Action<IMatchmakingCallback> action)
        {
            foreach (var player in Players.Values)
            {
                try
                {
                    if (player.Callback != null)
                    {
                        action(player.Callback);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Failed to broadcast to player {Username}: {Message}",
                        player.Username, ex.Message);
                }
            }
        }

        private (int points, string bonusType) calculatePointsForPlacement(PlayerSessionData player, int pieceId)
        {
            int points = 0;
            List<string> bonuses = new List<string>();
            DateTime now = DateTime.UtcNow;

            bool isEdge = isEdgePiece(pieceId);

            points += isEdge ? SCORE_EDGE_PIECE : SCORE_CENTER_PIECE;

            if (!isFirstBloodClaimed)
            {
                points += SCORE_FIRST_BLOOD;
                isFirstBloodClaimed = true;
                bonuses.Add("FIRST_BLOOD");
            }

            player.CurrentStreak++;
            if (player.CurrentStreak % STREAK_THRESHOLD == 0)
            {
                points += SCORE_STREAK_BONUS;
                bonuses.Add("STREAK");
            }

            player.RecentPlacementTimestamps.Add(now);
            if (player.RecentPlacementTimestamps.Count >= FRENZY_COUNT)
            {
                int count = player.RecentPlacementTimestamps.Count;
                DateTime startWindow = player.RecentPlacementTimestamps[count - FRENZY_COUNT];

                if ((now - startWindow).TotalSeconds <= FRENZY_TIME_WINDOW_SECONDS)
                {
                    points += SCORE_FRENZY_BONUS;
                    bonuses.Add("FRENZY");
                    player.RecentPlacementTimestamps.Clear();
                }
            }

            if (isPuzzleComplete())
            {
                points += SCORE_LAST_HIT;
                bonuses.Add("LAST_HIT");
            }

            string bonusString = bonuses.Count > 0 ? string.Join(",", bonuses) : null;
            return (points, bonusString);
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




        public bool isPuzzleComplete()
        {
            return PieceStates.Values.All(p => p.IsPlaced);
        }

        private async void endGame(string reason)
        {
            if (!trySetGameEnded()) { return; }

            stopTimers();
            logger.Info($"Ending Game for Lobby {LobbyCode}. Reason: {reason}");


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
                        MatchId = this.MatchId,
                        MinutesPlayed = minutesPlayed,
                        TotalParticipants = rankedPlayers.Count
                    };


                    clientResults = await processAllPlayersAsync(rankedPlayers, context);

                    await matchRepo.finishMatchAsync(MatchId);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Critical error during endGame execution for Lobby {0}", LobbyCode);
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
            int minutes = (int)duration.TotalMinutes;
            if (minutes < 1) minutes = 1;

            return (minutes, duration.TotalSeconds);
        }

        private List<PlayerSessionData> getRankedPlayers()
        {
            return Players.Values
                .OrderByDescending(p => p.Score)
                .ThenByDescending(p => p.PiecesPlaced)
                .ToList();
        }

        private async Task<(Matches match, Puzzles puzzle)> fetchGameEntitiesAsync (IMatchmakingRepository matchRepo, IPuzzleRepository puzzleRepo)
        {
            Matches matchEntity = null;
            Puzzles puzzleEntity = null;

            try
            {
                matchEntity = await matchRepo.getMatchByIdAsync(this.MatchId);
                if (matchEntity != null)
                {
                    matchEntity.end_time = DateTime.UtcNow;
                }

                puzzleEntity = await puzzleRepo.getPuzzleByIdAsync(this.PuzzleId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Could not fetch Match/Puzzle entities.");
            }

            return (matchEntity, puzzleEntity);
        }

        private async Task<List<PlayerResultDto>> processAllPlayersAsync (List<PlayerSessionData> rankedPlayers, EndGameProcessingContext context)
        {
            var results = new List<PlayerResultDto>();
            int currentRank = 1;

            foreach (var player in rankedPlayers)
            {
                // Parametros: 3 (Cumple <= 3)
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
            bool isWinner = (rank == 1);
            List<int> unlockedIds = new List<int>();

            if (player.PlayerId > 0)
            {
                try
                {
                    unlockedIds = await handlePlayerStatsAndAchievementsAsync(player, rank, context);

                    // 2. GUARDADO EN BD (Usando el DTO que creamos antes)
                    var updateDto = new MatchParticipantStatsUpdateDto
                    {
                        MatchId = context.MatchId,
                        PlayerId = player.PlayerId,
                        Score = player.Score,
                        PiecesPlaced = player.PiecesPlaced,
                        Rank = rank
                    };

                    // Usamos el repo desde el contexto
                    await context.MatchRepo.updateMatchParticipantStatsAsync(updateDto);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error processing/saving data for player {0}", player.Username);
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
            bool isWinner = (rank == 1); // Calculamos esto aquí o lo pasamos, rank es suficiente

            var statsDto = new PlayerMatchStatsDto
            {
                PlayerId = player.PlayerId,
                Score = player.Score,
                Rank = rank,
                IsWin = isWinner,
                PlaytimeMinutes = context.MinutesPlayed
            };

            // Usamos StatsService desde el contexto
            var historicalStats = await context.StatsService.GetPlayerStatsAsync(player.PlayerId);

            await context.StatsService.processMatchResultsAsync(statsDto);

            // Usamos Entidades desde el contexto
            if (context.MatchEntity != null && context.PuzzleEntity != null && historicalStats != null)
            {
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
                newUnlockedIds = await context.StatsService.UnlockAchievementsAsync(player.PlayerId, qualifiedAchievements);

                if (newUnlockedIds.Any())
                    logger.Info($"User {player.Username} unlocked {newUnlockedIds.Count} achievements.");
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
        public async Task kickPlayerAsync(int playerId, int reasonId, int hostPlayerId)
        {
         
            if (Players.TryGetValue(playerId, out var playerSession))
            {
                try
                {
                    string kickMessage = (reasonId == 2)
                        ? Lang.KickMessageProfanity
                        : Lang.KickedByHost;

                    playerSession.Callback.kickedFromLobby(kickMessage);
                }
                catch (Exception ex)
                {
                    logger.Warn("No se pudo notificar al jugador {0} de su expulsión: {1}", playerSession.Username, ex.Message);
                }
            }

            try
            {
                using (var scope = matchmakingFactory())
                {
                    var repo = scope.Value;
                    var expulsionDto = new ExpulsionDto
                    {
                        MatchId = this.MatchId,
                        PlayerId = playerId,
                        ReasonId = reasonId,
                        HostPlayerId = hostPlayerId
                    };

                    await repo.registerExpulsionAsync(expulsionDto);
                    logger.Info($"Expulsion recorded for Player {playerId} in Match {MatchId}. Reason: {reasonId}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to register expulsion in DB for player {0}", playerId);
            }
            removePlayer(playerId);
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