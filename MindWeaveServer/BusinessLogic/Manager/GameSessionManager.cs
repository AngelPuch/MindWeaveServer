using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using NLog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic.Manager
{
    public class GameSessionManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ConcurrentDictionary<string, GameSession> activeSessions = new ConcurrentDictionary<string, GameSession>();

        private readonly IMatchmakingRepository matchmakingRepository;
        private readonly IPuzzleRepository puzzleRepository;
        private readonly StatsLogic statsLogic;
        private readonly IScoreCalculator scoreCalculator;

        private const string DEFAULT_PUZZLES_FOLDER_NAME = "DefaultPuzzles";
        private const string UPLOADED_PUZZLES_FOLDER_NAME = "UploadedPuzzles";
        private const string RESOURCES_FOLDER_NAME = "Resources";
        private const string IMAGES_FOLDER_NAME = "Images";
        private const string PUZZLES_FOLDER_NAME = "Puzzles";

        public GameSessionManager(
            IPuzzleRepository puzzleRepository,
            IMatchmakingRepository matchmakingRepository,
            StatsLogic statsLogic,
            IScoreCalculator scoreCalculator)
        {
            this.puzzleRepository = puzzleRepository;
            this.matchmakingRepository = matchmakingRepository;
            this.statsLogic = statsLogic;
            this.scoreCalculator = scoreCalculator;
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

            PuzzleDefinitionDto puzzleDto = PuzzleGenerator.generatePuzzle(puzzleBytes, difficulty);

            var sessionConfig = new GameSessionConfiguration
            {
                LobbyCode = lobbyId,
                MatchId = matchId,
                PuzzleId = puzzleId,
                PuzzleDefinition = puzzleDto,
                OnSessionEndedCleanup = removeSession
            };

            var gameSession = new GameSession(
                sessionConfig,
                matchmakingRepository,
                statsLogic,
                puzzleRepository,
                scoreCalculator);

            foreach (var player in players.Values)
            {
                gameSession.addPlayer(player.PlayerId, player.Username, player.AvatarPath, player.Callback);
            }

            activeSessions[lobbyId] = gameSession;

            logger.Info("Game session created for lobby {LobbyId} with {PlayerCount} players.",
                lobbyId, players.Count);

            return gameSession;
        }

        public bool isPlayerInAnySession(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return false;
            }

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

        public void handlePieceMove(PieceMovementContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            var session = getSession(context.LobbyCode);

            if (session != null)
            {
                session.handlePieceMove(context);
            }
        }

        public async Task handlePieceDrop(PieceMovementContext context)
        {
            var session = getSession(context.LobbyCode);

            if (session != null)
            {
                await session.handlePieceDrop(context);
            }
            else
            {
                logger.Warn("HandlePieceDrop: No active session found for lobby {LobbyCode}", context.LobbyCode);
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

                logger.Info("Handling disconnect for player {PlayerId} in GameSession {LobbyId}",
                    playerId, session.LobbyCode);

                session.releaseHeldPieces(playerId);

                if (!session.Players.Any() && activeSessions.TryRemove(session.LobbyCode, out var removedSession))
                {
                    removedSession.Dispose();
                    logger.Info("Removed and disposed empty GameSession {LobbyId} from session manager.", session.LobbyCode);
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
            var puzzleData = await puzzleRepository.getPuzzleByIdAsync(puzzleId);

            if (puzzleData == null)
            {
                logger.Error("Failed to create game: PuzzleId {PuzzleId} not found.", puzzleId);
                throw new InvalidOperationException($"Puzzle with ID {puzzleId} was not found.");
            }

            return puzzleData.image_path;
        }

        private static byte[] getPuzzleBytes(string imagePath)
        {
            string fullPath = resolvePuzzlePath(imagePath);

            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                logger.Error("FATAL: Could not find puzzle image file. Requested: '{ImagePath}'. Resolves to: '{FullPath}'", imagePath, fullPath);

                throw new FileNotFoundException($"Puzzle image not found: {imagePath}", fullPath);
            }

            return File.ReadAllBytes(fullPath);
        }

        private static string resolvePuzzlePath(string imagePath)
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;

            string defaultPuzzlePath = Path.Combine(basePath, DEFAULT_PUZZLES_FOLDER_NAME, imagePath);
            if (File.Exists(defaultPuzzlePath))
            {
                return defaultPuzzlePath;
            }

            string uploadedPath = Path.Combine(basePath, UPLOADED_PUZZLES_FOLDER_NAME, imagePath);
            if (File.Exists(uploadedPath))
            {
                return uploadedPath;
            }

            string resourcesPath = Path.Combine(basePath, RESOURCES_FOLDER_NAME, IMAGES_FOLDER_NAME, PUZZLES_FOLDER_NAME, imagePath);
            if (File.Exists(resourcesPath))
            {
                return resourcesPath;
            }

            string directPath = Path.Combine(basePath, imagePath);
            return directPath;
        }

        public int? getPlayerIdInLobby(string lobbyCode, string username)
        {
            var session = getSession(lobbyCode);
            if (session != null)
            {
                return session.getPlayerIdByUsername(username);
            }
            return null;
        }

        public async Task handlePlayerLeaveAsync(string lobbyCode, string username)
        {
            var session = getSession(lobbyCode);

            if (session != null)
            {
                await session.handlePlayerVoluntaryLeaveAsync(username);
            }
            else
            {
                logger.Warn("HandlePlayerLeave: No active session found for lobby {LobbyCode}", lobbyCode);
            }
        }

        public GameSession findSessionByUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            foreach (var session in activeSessions.Values)
            {
                var playerId = session.getPlayerIdByUsername(username);
                if (playerId.HasValue)
                {
                    return session;
                }
            }

            return null;
        }
    }
}