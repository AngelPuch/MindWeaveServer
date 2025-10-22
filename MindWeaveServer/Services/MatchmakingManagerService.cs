// MindWeaveServer/Services/MatchmakingManagerService.cs
using MindWeaveServer.BusinessLogic; // Asumiremos una clase de lógica después
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Matchmaking; // Para LobbyCreationResultDto
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.Collections.Generic; // Para Dictionary
using System.ServiceModel; // Para ServiceBehavior, InstanceContextMode, etc.
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    // TODO: Considerar concurrencia e instancia única si manejas estado en memoria
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class MatchmakingManagerService : IMatchmakingManager
    {
        // Usaremos una clase de lógica de negocio para mantener el servicio limpio
        private readonly MatchmakingLogic matchmakingLogic;

        // Diccionario estático para mantener el estado de los lobbies activos en memoria
        // Key: lobbyCode, Value: LobbyStateDto
        // ¡¡IMPORTANTE!! Esto es una implementación simple. En producción, considera:
        // 1. Thread safety (ConcurrentDictionary)
        // 2. Persistencia (si el servidor se reinicia, los lobbies se pierden)
        // 3. Limpieza de lobbies inactivos
        private static readonly Dictionary<string, LobbyStateDto> activeLobbies = new Dictionary<string, LobbyStateDto>();

        public MatchmakingManagerService()
        {
            // Inyectar dependencias si usas un contenedor IoC,
            // o instanciarlas directamente por ahora.
            // Pasamos el diccionario de lobbies a la lógica.
            this.matchmakingLogic = new MatchmakingLogic(activeLobbies);
        }

        public async Task<LobbyCreationResultDto> createLobby(string hostUsername, LobbySettingsDto settingsDto)
        {
            // Validación básica de entrada
            if (string.IsNullOrWhiteSpace(hostUsername) || settingsDto == null)
            {
                // Devolvemos un resultado de error específico
                return new LobbyCreationResultDto
                {
                    success = false,
                    message = "Host username and settings are required.", // TODO: Usar Lang resources
                    lobbyCode = null,
                    initialLobbyState = null
                };
            }

            try
            {
                // Llama a la lógica de negocio para crear el lobby
                return await matchmakingLogic.createLobbyAsync(hostUsername, settingsDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating lobby: {ex}"); // TODO: Log error properly
                return new LobbyCreationResultDto
                {
                    success = false,
                    message = "Failed to create lobby due to a server error.", // TODO: Usar Lang resources
                    lobbyCode = null,
                    initialLobbyState = null
                };
            }
        }

        // --- Implementaciones de otros métodos de IMatchmakingManager (vacías por ahora) ---

        public void inviteToLobby(string inviterUsername, string invitedUsername, string lobbyId)
        {
            Console.WriteLine($"inviteToLobby called: Inviter={inviterUsername}, Invited={invitedUsername}, Lobby={lobbyId}");
            // TODO: Implementar lógica de invitación (usar matchmakingLogic)
            // TODO: Enviar callback al 'invitedUsername'
        }

        public void joinLobby(string username, string lobbyId)
        {
            Console.WriteLine($"joinLobby called: User={username}, Lobby={lobbyId}");
            // TODO: Implementar lógica para unirse (usar matchmakingLogic)
            // TODO: Notificar a todos en el lobby (actualizar LobbyState)
        }

        public void leaveLobby(string username, string lobbyId)
        {
            Console.WriteLine($"leaveLobby called: User={username}, Lobby={lobbyId}");
            // TODO: Implementar lógica para salir (usar matchmakingLogic)
            // TODO: Notificar a todos en el lobby (actualizar LobbyState)
            // TODO: Si el host se va, ¿qué pasa? (¿cerrar lobby o transferir host?)
        }

        public void startGame(string hostUsername, string lobbyId)
        {
            Console.WriteLine($"startGame called: Host={hostUsername}, Lobby={lobbyId}");
            // TODO: Implementar lógica para iniciar partida (usar matchmakingLogic)
            // TODO: Cambiar estado en DB, notificar a todos para ir a pantalla de juego
        }

        public void kickPlayer(string hostUsername, string playerToKickUsername, string lobbyId)
        {
            Console.WriteLine($"kickPlayer called: Host={hostUsername}, Kicked={playerToKickUsername}, Lobby={lobbyId}");
            // TODO: Implementar lógica para expulsar (usar matchmakingLogic)
            // TODO: Notificar al expulsado y actualizar estado del lobby para los demás
        }
    }
}