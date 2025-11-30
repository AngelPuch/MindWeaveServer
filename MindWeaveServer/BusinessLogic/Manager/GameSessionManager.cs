using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac.Features.OwnedInstances;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using NLog;

namespace MindWeaveServer.BusinessLogic.Manager
{
    public class GameSessionManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ConcurrentDictionary<string, GameSession> activeSessions = new ConcurrentDictionary<string, GameSession>();

        private readonly Func<Owned<IMatchmakingRepository>> matchmakingRepositoryFactory;
        private readonly Func<Owned<IPuzzleRepository>> puzzleRepositoryFactory;
        private readonly Func<Owned<StatsLogic>> statsLogicFactory;
        private readonly PuzzleGenerator puzzleGenerator;
        private readonly IScoreCalculator scoreCalculator;

        public GameSessionManager(
            Func<Owned<IPuzzleRepository>> puzzleRepositoryFactory,
            Func<Owned<IMatchmakingRepository>> matchmakingRepositoryFactory,
            Func<Owned<StatsLogic>> statsLogicFactory,
            PuzzleGenerator puzzleGenerator,
            IScoreCalculator scoreCalculator)
        {
            this.puzzleRepositoryFactory = puzzleRepositoryFactory
                ?? throw new ArgumentNullException(nameof(puzzleRepositoryFactory));
            this.matchmakingRepositoryFactory = matchmakingRepositoryFactory
                ?? throw new ArgumentNullException(nameof(matchmakingRepositoryFactory));
            this.statsLogicFactory = statsLogicFactory
                ?? throw new ArgumentNullException(nameof(statsLogicFactory));
            this.puzzleGenerator = puzzleGenerator
                ?? throw new ArgumentNullException(nameof(puzzleGenerator));
            this.scoreCalculator = scoreCalculator
                ?? throw new ArgumentNullException(nameof(scoreCalculator));
        }

        public async Task<GameSession> createGameSession(
            string lobbyId,
            int matchId,
            int puzzleId,
            DifficultyLevels difficulty,
            ConcurrentDictionary<int, PlayerSessionData> players)
        {
            if (string.IsNullOrWhiteSpace(lobbyId))
            {
                throw new ArgumentException("LobbyId cannot be null or empty.", nameof(lobbyId));
            }

            string imagePath = await getImagePathAsync(puzzleId);
            byte[] puzzleBytes = getPuzzleBytes(imagePath);

            PuzzleDefinitionDto puzzleDto = puzzleGenerator.generatePuzzle(puzzleBytes, difficulty);

            var gameSession = new GameSession(
                lobbyId,
                matchId,
                puzzleId,
                puzzleDto,
                matchmakingRepositoryFactory,
                statsLogicFactory,
                puzzleRepositoryFactory,
                removeSession,
                scoreCalculator);

            foreach (var player in players.Values)
            {
                gameSession.addPlayer(player.PlayerId, player.Username, player.Callback);
            }

            activeSessions[lobbyId] = gameSession;

            logger.Info("Game session created for lobby {LobbyId} with {PlayerCount} players.",
                lobbyId, players.Count);

            return gameSession;
        }

        public bool isPlayerInAnySession(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;

            return activeSessions.Values.Any(session =>
                session.Players.Values.Any(p => p.Username.Equals(username, StringComparison.OrdinalIgnoreCase)));
        }
        public GameSession getSession(string lobbyCode)
        {
            if (string.IsNullOrWhiteSpace(lobbyCode))
            {
                return null;
            }

            activeSessions.TryGetValue(lobbyCode, out var session);
            return session;
        }

        public void handlePieceDrag(string lobbyCode, int playerId, int pieceId)
        {
            var session = getSession(lobbyCode);

            if (session != null)
            {
                session.handlePieceDrag(playerId, pieceId);
            }
            else
            {
                logger.Warn("HandlePieceDrag: No active session found for lobby {LobbyCode}", lobbyCode);
            }
        }

        public void handlePieceMove(string lobbyCode, int playerId, int pieceId, double newX, double newY)
        {
            var session = getSession(lobbyCode);

            if (session != null)
            {
                session.handlePieceMove(playerId, pieceId, newX, newY);
            }
        }

        public async Task handlePieceDrop(string lobbyCode, int playerId, int pieceId, double newX, double newY)
        {
            var session = getSession(lobbyCode);

            if (session != null)
            {
                await session.handlePieceDrop(playerId, pieceId, newX, newY);
            }
            else
            {
                logger.Warn("HandlePieceDrop: No active session found for lobby {LobbyCode}", lobbyCode);
            }
        }

        public void handlePieceRelease(string lobbyCode, int playerId, int pieceId)
        {
            var session = getSession(lobbyCode);

            if (session != null)
            {
                session.handlePieceRelease(playerId, pieceId);
            }
            else
            {
                logger.Warn("HandlePieceRelease: No active session found for lobby {LobbyCode}", lobbyCode);
            }
        }

        public void handlePlayerDisconnect(string username, int playerId)
        {
            foreach (var session in activeSessions.Values)
            {
                var player = session.removePlayer(playerId);

                if (player == null)
                {
                    continue;
                }

                logger.Info("Handling disconnect for {Username} in GameSession {LobbyId}",
                    username, session.LobbyCode);

                session.releaseHeldPieces(playerId);

                if (!session.Players.Any())
                {
                    activeSessions.TryRemove(session.LobbyCode, out _);
                    logger.Info("Removed empty GameSession {LobbyId} from session manager.", session.LobbyCode);
                }

                break;
            }
        }

        private void removeSession(string lobbyCode)
        {
            if (activeSessions.TryRemove(lobbyCode, out var removedSession))
            {
                removedSession?.Dispose();
                logger.Info("GameSession {LobbyCode} removed from manager (Ended).", lobbyCode);
            }
        }

        private async Task<string> getImagePathAsync(int puzzleId)
        {
            using (var puzzleScope = puzzleRepositoryFactory())
            {
                var puzzleRepo = puzzleScope.Value;
                var puzzleData = await puzzleRepo.getPuzzleByIdAsync(puzzleId);

                if (puzzleData == null)
                {
                    logger.Error("Failed to create game: PuzzleId {PuzzleId} not found.", puzzleId);
                    throw new InvalidOperationException($"Puzzle with ID {puzzleId} was not found.");
                }

                return puzzleData.image_path;
            }
        }

        private byte[] getPuzzleBytes(string imagePath)
        {
            string fullPath = resolvePuzzlePath(imagePath);

            if (!File.Exists(fullPath))
            {
                logger.Error("Could not find puzzle image file: {Path}", fullPath);
                throw new FileNotFoundException("Puzzle image not found.", fullPath);
            }

            return File.ReadAllBytes(fullPath);
        }

        private string resolvePuzzlePath(string imagePath)
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;

            string directPath = Path.Combine(basePath, imagePath);
            if (File.Exists(directPath))
            {
                return directPath;
            }

            if (!imagePath.StartsWith("puzzleDefault", StringComparison.OrdinalIgnoreCase))
            {
                string uploadedPath = Path.Combine(basePath, "UploadedPuzzles", imagePath);
                if (File.Exists(uploadedPath))
                {
                    return uploadedPath;
                }
            }

            return directPath;
        }
    }
}