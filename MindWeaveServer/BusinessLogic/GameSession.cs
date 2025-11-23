using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess.Abstractions;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class PlayerSessionData
    {
        public int PlayerId { get; set; }
        public string Username { get; set; }
        public IMatchmakingCallback Callback { get; set; }
        public int Score { get; set; }
        public int CurrentStreak { get; set; }
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
    }

    public class GameSession
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public string LobbyCode { get; }
        public int MatchId { get; }
        public PuzzleDefinitionDto PuzzleDefinition { get; }
        public DateTime StartTime { get; }

        public ConcurrentDictionary<int, PlayerSessionData> Players { get; } = new ConcurrentDictionary<int, PlayerSessionData>();
        public ConcurrentDictionary<int, PuzzlePieceState> PieceStates { get; } = new ConcurrentDictionary<int, PuzzlePieceState>();
        private readonly IMatchmakingRepository matchmakingRepository;

        private const double SNAP_TOLERANCE = 30.0;
        private bool isFirstBloodClaimed = false;

        private const int SCORE_EDGE_PIECE = 5;
        private const int SCORE_CENTER_PIECE = 10;
        private const int SCORE_STREAK_BONUS = 10;
        private const int SCORE_FRENZY_BONUS = 40;
        private const int SCORE_FIRST_BLOOD = 25;
        private const int SCORE_LAST_HIT = 50;

        private const int STREAK_THRESHOLD = 3;
        private const int FRENZY_COUNT = 5;
        private const int FRENZY_TIME_WINDOW_SECONDS = 60;

        public GameSession(string lobbyCode, int matchId, PuzzleDefinitionDto puzzleDefinition, IMatchmakingRepository matchmakingRepository)
        {
            LobbyCode = lobbyCode;
            MatchId = matchId;
            this.matchmakingRepository = matchmakingRepository;
            this.PuzzleDefinition = puzzleDefinition;
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
                    HeldByPlayerId = null
                };
                PieceStates.TryAdd(pieceDef.PieceId, pieceState);
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
                CurrentStreak = 0,
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
                logger.Warn("Piece {PieceId} was force-released due to player {PlayerId} disconnect.", piece.PieceId, playerId);

                if (Players.TryGetValue(playerId, out var player))
                {
                    broadcast(callback => callback.onPieceDragReleased(piece.PieceId, player.Username));
                }
            }
        }

        public void handlePieceDrag(int playerId, int pieceId)
        {
            logger.Info("handlePieceDrag called: PlayerId={PlayerId}, PieceId={PieceId}. Players in session: [{PlayerIds}]",
                playerId, pieceId, string.Join(", ", Players.Keys));

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

            logger.Info("GameSession {LobbyCode}: Player {PlayerId} started dragging piece {PieceId}",
                LobbyCode, playerId, pieceId);

            broadcast(callback => callback.onPieceDragStarted(pieceId, player.Username));

        }

        public void handlePieceMove(int playerId, int pieceId, double newX, double newY)
        {
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

            bool isCorrect = !pieceState.IsPlaced &&
                             Math.Abs(pieceState.FinalX - newX) < SNAP_TOLERANCE &&
                             Math.Abs(pieceState.FinalY - newY) < SNAP_TOLERANCE;

            Console.WriteLine($"[DEBUG DROP] Pieza {pieceId}: ClientPos({newX:F2}, {newY:F2}) vs ServerTarget({pieceState.FinalX:F2}, {pieceState.FinalY:F2}). DiffX: {Math.Abs(pieceState.FinalX - newX):F2}, DiffY: {Math.Abs(pieceState.FinalY - newY):F2}. Tolerance: {SNAP_TOLERANCE}. Resultado: {(isCorrect ? "ENCJAÓ" : "FALLÓ")}");

            if (isCorrect)
            {
                logger.Info("GameSession {LobbyCode}: Player {PlayerId} SNAPPED piece {PieceId}",
                    LobbyCode, playerId, pieceId);

                pieceState.IsPlaced = true;
                pieceState.CurrentX = pieceState.FinalX;
                pieceState.CurrentY = pieceState.FinalY;

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

                    await saveAllScoresToDatabase();

                    broadcast(callback => callback.onGameEnded(MatchId));
                }
            }
            else
            {
                logger.Info("GameSession {LobbyCode}: Player {PlayerId} moved piece {PieceId} to ({NewX}, {NewY})",
                    LobbyCode, playerId, newX, newY);

                pieceState.CurrentX = newX;
                pieceState.CurrentY = newY;

                broadcast(callback => callback.onPieceMoved(pieceId, newX, newY, player.Username));
            }
        }

        public void handlePieceRelease(int playerId, int pieceId)
        {
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
            if (isEdge)
            {
                points += SCORE_EDGE_PIECE;
            }
            else
            {
                points += SCORE_CENTER_PIECE;
            }

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

        private async Task saveAllScoresToDatabase()
        {
            var saveTasks = new List<Task>();

            foreach (var p in Players.Values)
            {
                saveTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await matchmakingRepository.updatePlayerScoreAsync(MatchId, p.PlayerId, p.Score);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Failed to save final score for Player {PlayerId} in Match {MatchId}", p.PlayerId, MatchId);
                    }
                }));
            }

            await Task.WhenAll(saveTasks);
        }


        public bool isPuzzleComplete()
        {
            return PieceStates.Values.All(p => p.IsPlaced);
        }
    }
}