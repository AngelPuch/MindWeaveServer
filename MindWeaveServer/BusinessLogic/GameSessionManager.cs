using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using NLog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class GameSessionManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ConcurrentDictionary<string, GameSession> activeSessions = new ConcurrentDictionary<string, GameSession>();
        private readonly IMatchmakingRepository matchmakingRepository;

        private readonly IPuzzleRepository puzzleRepository;
        private readonly PuzzleGenerator puzzleGenerator;

        public GameSessionManager(IPuzzleRepository puzzleRepository, IMatchmakingRepository matchmakingRepository)
        {
            this.puzzleRepository = puzzleRepository;
            this.matchmakingRepository = matchmakingRepository;
            this.puzzleGenerator = new PuzzleGenerator();
        }

        public async Task<GameSession> createGameSession(string lobbyId, int matchId, int puzzleId, 
            DifficultyLevels difficulty, ConcurrentDictionary<int, PlayerSessionData> players)
        {
            var puzzleData = await puzzleRepository.getPuzzleByIdAsync(puzzleId);
            if (puzzleData == null)
            {
                logger.Error("Failed to create game: PuzzleId {PuzzleId} not found.", puzzleId);
                throw new Exception("Puzzle not found.");
            }

            byte[] puzzleBytes = await getPuzzleBytes(puzzleData.image_path);

            PuzzleDefinitionDto puzzleDto = puzzleGenerator.generatePuzzle(
                puzzleBytes,
                difficulty
            );

            var gameSession = new GameSession(lobbyId, matchId, puzzleDto, matchmakingRepository);

            foreach (var player in players.Values)
            {
                gameSession.addPlayer(player.PlayerId, player.Username, player.Callback);
            }

            activeSessions[lobbyId] = gameSession;

            logger.Info("Game session created and ready for lobby {LobbyId}", lobbyId);

            return gameSession;
        }

        public GameSession getSession(string lobbyCode)
        {
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

        private async Task<byte[]> getPuzzleBytes(string imagePath)
        {
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imagePath);

            if (!File.Exists(fullPath) && !imagePath.StartsWith("puzzleDefault"))
            {
                fullPath = Path.Combine(
                   AppDomain.CurrentDomain.BaseDirectory,
                   "UploadedPuzzles",
                   imagePath
               );
            }

            if (!File.Exists(fullPath))
            {
                logger.Error("Could not find puzzle image file: {Path}", fullPath);
                throw new FileNotFoundException("Puzzle image not found.", fullPath);
            }

            return File.ReadAllBytes(fullPath);
        }

        private GameSession findGameForPlayer(string username)
        {
            foreach (var game in activeSessions.Values)
            {
                if (game.Players.Values.Any(p => p.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                {
                    return game;
                }
            }
            return null;
        }

        public void handlePlayerDisconnect(string username, int playerId)
        {
            foreach (var session in activeSessions.Values)
            {
                var player = session.removePlayer(playerId);
                if (player != null)
                {
                    logger.Info("Handling disconnect for {Username} in GameSession {LobbyId}", username, session.LobbyCode);

                    if (!session.Players.Any())
                    {
                        activeSessions.TryRemove(session.LobbyCode, out _);
                        logger.Info("Removed empty GameSession {LobbyId} from session manager.", session.LobbyCode);
                    }
                    break;
                }
            }
        }

    }
}