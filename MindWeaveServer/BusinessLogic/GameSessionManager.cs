using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.DataAccess.Repositories;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class GameSessionManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ConcurrentDictionary<string, GameLogic> activeGames = new ConcurrentDictionary<string, GameLogic>();

        private readonly IPuzzleRepository puzzleRepository;
        private readonly PuzzleGenerator puzzleGenerator;

        public GameSessionManager(IPuzzleRepository puzzleRepository)
        {
            this.puzzleRepository = puzzleRepository;
            this.puzzleGenerator = new PuzzleGenerator();
            logger.Info("GameSessionManager initialized.");
        }

        public async Task<PuzzleDefinitionDto> createAndStartGame(string lobbyId, int puzzleId, DifficultyLevels difficulty, List<string> players)
        {
            logger.Info("Attempting to create game for lobby {LobbyId}", lobbyId);

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

            var gameLogic = new GameLogic(lobbyId, puzzleDto, players);
            activeGames[lobbyId] = gameLogic;

            logger.Info("Game created and ready for lobby {LobbyId}", lobbyId);
            return puzzleDto; 
        }

        public void handlePlayerAction_PlacePiece(string username, string lobbyId, int pieceId)
        {
            if (activeGames.TryGetValue(lobbyId, out var gameLogic))
            {
                gameLogic.playerPlacedPiece(username, pieceId);
             
            }
            else
            {
                logger.Warn("Player {Username} sent PlacePiece for non-existent game {LobbyId}", username, lobbyId);
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

        public void handlePlayerAction_PlacePiece(string username, int pieceId)
        {
            GameLogic game = findGameForPlayer(username);

            if (game != null)
            {
                game.playerPlacedPiece(username, pieceId);

                // TODO: Aquí es donde, después de que 'playerPlacedPiece'
                // calcule el puntaje, usaremos los callbacks para notificar
                // a TODOS los jugadores de la nueva puntuación.
            }
            else
            {
                logger.Warn("Player {Username} sent PlacePiece but is not in any active game.", username);
            }
        }

        private GameLogic findGameForPlayer(string username)
        {
            foreach (var game in activeGames.Values)
            {
                if (game.HasPlayer(username))
                {
                    return game;
                }
            }
            return null; 
        }

        public void handlePlayerDisconnect(string username)
        {
            GameLogic game = findGameForPlayer(username);
            if (game != null)
            {
                logger.Info("Manejando desconexión para {Username} en la partida {LobbyId}", username, game.LobbyId);

               
                bool isGameEmpty = game.handlePlayerDisconnect(username);

                // (TODO: Notificar a los otros jugadores que 'username' se fue)

                if (isGameEmpty)
                {
                    activeGames.TryRemove(game.LobbyId, out _);
                    logger.Info("Se eliminó la partida vacía {LobbyId} del gestor de sesiones.", game.LobbyId);
                }
            }
        }

    }
}