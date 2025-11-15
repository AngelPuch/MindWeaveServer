using MindWeaveServer.Contracts.DataContracts.Puzzle;
using NLog;
using System.Collections.Generic;

namespace MindWeaveServer.BusinessLogic
{
    public class GameLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly PuzzleDefinitionDto puzzle;
        private readonly Dictionary<string, int> playerScores;
        private readonly bool[] placedPieces;

        public string LobbyId { get; }

        public GameLogic(string lobbyId, PuzzleDefinitionDto puzzle, List<string> players)
        {
            this.LobbyId = lobbyId;
            this.puzzle = puzzle;
            this.placedPieces = new bool[puzzle.pieces.Count];
            this.playerScores = new Dictionary<string, int>();

            foreach (var player in players)
            {
                playerScores.Add(player, 0);
            }

            logger.Info("New GameLogic created for Lobby {LobbyId} with {Count} pieces.", lobbyId, puzzle.pieces.Count);
        }

        // Aquí vivirá la lógica del juego
        public void playerPlacedPiece(string username, int pieceId)
        {
            // TODO:
            // 1. Validar si la pieza 'pieceId' es correcta (usando 'puzzle.pieces')
            // 2. Comprobar que no haya sido colocada ('placedPieces[pieceId]')
            // 3. Calcular puntuación (con bonos por racha, etc.)
            // 4. Actualizar 'playerScores[username]'
            // 5. Marcar 'placedPieces[pieceId] = true'
            // 6. Retornar el resultado al GameSessionManager (para notificar a todos)
            logger.Info("Player {Username} in lobby {LobbyId} placed piece {PieceId}", username, LobbyId, pieceId);
        }

        public bool HasPlayer(string username)
        {
            // playerScores es el Dictionary que creamos en el constructor
            return playerScores.ContainsKey(username);
        }


        public bool handlePlayerDisconnect(string username)
        {
            if (playerScores.Remove(username))
            {
                logger.Info("Player {Username} removido de la partida activa {LobbyId}.", username, this.LobbyId);
            }
            else
            {
                logger.Warn("Se intentó remover a {Username} de la partida {LobbyId}, pero ya no estaba en la lista de puntajes.", username, this.LobbyId);
            }
            return playerScores.Count == 0;
        }
    }
}