using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess.Abstractions;
using NLog;
using System;
using System.Collections.Concurrent;
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

        public ConcurrentDictionary<int, PlayerSessionData> Players { get; } = new ConcurrentDictionary<int, PlayerSessionData>();
        public ConcurrentDictionary<int, PuzzlePieceState> PieceStates { get; } = new ConcurrentDictionary<int, PuzzlePieceState>();
        private readonly IMatchmakingRepository matchmakingRepository;

        private const double SNAP_TOLERANCE = 10.0;

        public GameSession(string lobbyCode, int matchId, PuzzleDefinitionDto puzzleDefinition, IMatchmakingRepository matchmakingRepository)
        {
            LobbyCode = lobbyCode;
            MatchId = matchId;
            this.matchmakingRepository = matchmakingRepository;
            this.PuzzleDefinition = puzzleDefinition;

            logger.Info("Initializing GameSession for Lobby {LobbyCode}", LobbyCode);

            foreach (var pieceDef in this.PuzzleDefinition.Pieces)
            {
                var pieceState = new PuzzlePieceState
                {
                    PieceId = pieceDef.PieceId,
                    FinalX = pieceDef.CorrectX,
                    FinalY = pieceDef.CorrectY,
                    CurrentX = pieceDef.InitialX,
                    CurrentY = pieceDef.InitialY 
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
                Score = 0
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
                string username = Players[playerId].Username;
                broadcast(callback => callback.onPieceDragReleased(piece.PieceId, username));

            }
        }
        
        public void handlePieceDrag(int playerId, int pieceId)
        {
            if (!PieceStates.TryGetValue(pieceId, out var pieceState))
            {
                logger.Warn("Player {PlayerId} tried to drag non-existent piece {PieceId}", playerId, pieceId);
                return;
            }

            if (pieceState.IsPlaced || pieceState.HeldByPlayerId != null)
            {
                logger.Debug("Player {PlayerId} failed to drag piece {PieceId}: IsPlaced={IsPlaced}, HeldBy={HeldBy}",
                    playerId, pieceId, pieceState.IsPlaced, pieceState.HeldByPlayerId);
                return;
            }

            pieceState.HeldByPlayerId = playerId;

            logger.Info("GameSession {LobbyCode}: Player {PlayerId} started dragging piece {PieceId}", LobbyCode, playerId, pieceId);
            string username = Players[playerId].Username;
            broadcast(callback => callback.onPieceDragStarted(pieceId, username));
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
            
            bool isCorrect = !pieceState.IsPlaced &&
                             Math.Abs(pieceState.FinalX - newX) < SNAP_TOLERANCE &&
                             Math.Abs(pieceState.FinalY - newY) < SNAP_TOLERANCE;

            if (isCorrect)
            {
                pieceState.HeldByPlayerId = null;
                logger.Info("GameSession {LobbyCode}: Player {PlayerId} SNAPPED piece {PieceId}", LobbyCode, playerId, pieceId);

                pieceState.IsPlaced = true;
                var player = Players[playerId];
                player.Score += 10; // TODO: Make score dynamic

                try
                {
                    await matchmakingRepository.updatePlayerScoreAsync(MatchId, playerId, player.Score);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed to update score in DB for Match {MatchId}, Player {PlayerId}", MatchId, playerId);
                }
                string username = Players[playerId].Username;
                broadcast(callback => callback.onPiecePlaced(
                    pieceId,
                    pieceState.FinalX,
                    pieceState.FinalY,
                    username,
                    player.Score
                ));
            }
            else
            {
                pieceState.HeldByPlayerId = null;
                logger.Info("GameSession {LobbyCode}: Player {PlayerId} moved piece {PieceId} to ({NewX}, {NewY})", LobbyCode, playerId, newX, newY);

                pieceState.CurrentX = newX;
                pieceState.CurrentY = newY;
                string username = Players[playerId].Username;
                broadcast(callback => callback.onPieceMoved(pieceId, newX, newY, username));
                broadcast(callback => callback.onPieceDragReleased(pieceId, username));
            }
        }

        public void handlePieceRelease(int playerId, int pieceId)
        {
            if (!PieceStates.TryGetValue(pieceId, out var pieceState))
            {
                return;
            }

            if (pieceState.HeldByPlayerId == playerId)
            {
                logger.Info("GameSession {LobbyCode}: Player {PlayerId} released piece {PieceId}", LobbyCode, playerId, pieceId);
                pieceState.HeldByPlayerId = null;
                string username = Players[playerId].Username;
                broadcast(callback => callback.onPieceDragReleased(pieceId, username));
            }
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
                    logger.Warn(ex, "Failed to broadcast to player {Username}: {Message}", player.Username, ex.Message);
                }
            }
        }
    }
}
