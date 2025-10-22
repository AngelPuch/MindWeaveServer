// MindWeaveServer/Services/MatchmakingManagerService.cs
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.Collections.Concurrent; // Usar ConcurrentDictionary
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    // Cambiado a Single / Multiple para mantener estado y permitir concurrencia
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class MatchmakingManagerService : IMatchmakingManager
    {
        private readonly MatchmakingLogic matchmakingLogic;

        // Diccionarios seguros para hilos para estado en memoria
        private static readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbies = new ConcurrentDictionary<string, LobbyStateDto>();
        private static readonly ConcurrentDictionary<string, IMatchmakingCallback> userCallbacks = new ConcurrentDictionary<string, IMatchmakingCallback>();

        public MatchmakingManagerService()
        {
            // Pasar diccionarios a la lógica de negocio
            this.matchmakingLogic = new MatchmakingLogic(activeLobbies, userCallbacks);
        }

        // --- Método auxiliar para obtener/registrar el callback del cliente actual ---
        private IMatchmakingCallback getCurrentCallbackChannel(string username)
        {
            // Verificar si hay un contexto de operación (puede ser null si no es una llamada de cliente WCF)
            if (OperationContext.Current == null)
            {
                Console.WriteLine($"Warning: OperationContext.Current is null when trying to get callback for {username}.");
                return null;
            }

            IMatchmakingCallback currentCallback = OperationContext.Current.GetCallbackChannel<IMatchmakingCallback>();

            if (currentCallback != null && !string.IsNullOrEmpty(username))
            {
                // Almacena o actualiza el canal para este usuario
                userCallbacks.AddOrUpdate(username, currentCallback, (key, existingVal) =>
                {
                    // Si el canal existente es diferente o no está abierto, actualizamos
                    ICommunicationObject existingComm = existingVal as ICommunicationObject;
                    ICommunicationObject currentComm = currentCallback as ICommunicationObject;
                    if (existingVal != currentCallback || existingComm == null || existingComm.State != CommunicationState.Opened)
                    {
                        Console.WriteLine($"Updating callback channel for user: {username}");
                        // Intentar cerrar el viejo canal si existe y no es el mismo
                        if (existingVal != currentCallback && existingComm != null && existingComm.State == CommunicationState.Opened)
                        {
                            try { existingComm.Close(); } catch { existingComm.Abort(); }
                        }
                        return currentCallback; // Devolver el nuevo canal
                    }
                    return existingVal; // Mantener el canal existente si es el mismo y está abierto
                });

                // Re-obtener el canal almacenado por si acaso AddOrUpdate mantuvo el existente
                if (userCallbacks.TryGetValue(username, out currentCallback))
                {
                    ICommunicationObject commObject = currentCallback as ICommunicationObject;
                    if (commObject != null)
                    {
                        // Limpiar manejadores anteriores para evitar duplicados
                        commObject.Faulted -= CommObject_FaultedOrClosed;
                        commObject.Closed -= CommObject_FaultedOrClosed;
                        // Añadir manejadores (usando una función nombrada para claridad)
                        commObject.Faulted += CommObject_FaultedOrClosed;
                        commObject.Closed += CommObject_FaultedOrClosed;
                    }
                    Console.WriteLine($"Callback channel registered/updated for user: {username}"); // Log
                }
            }
            else if (string.IsNullOrEmpty(username))
            {
                Console.WriteLine("Warning: Attempted to register callback for null or empty username.");
            }
            else
            {
                Console.WriteLine($"Warning: Could not get callback channel for user {username}.");
            }
            return currentCallback;
        }

        // --- Manejador de eventos nombrado para Faulted y Closed ---
        private void CommObject_FaultedOrClosed(object sender, EventArgs e)
        {
            IMatchmakingCallback callbackChannel = sender as IMatchmakingCallback;
            if (callbackChannel != null)
            {
                // Buscar el usuario asociado a este canal específico
                var userEntry = userCallbacks.FirstOrDefault(pair => pair.Value == callbackChannel);
                if (!string.IsNullOrEmpty(userEntry.Key))
                {
                    removeCallbackChannel(userEntry.Key); // Llama a la limpieza
                }
                else
                {
                    Console.WriteLine("Warning: Faulted/Closed callback channel could not be mapped back to a user.");
                }

                // Importante: Desuscribirse para evitar fugas de memoria
                ICommunicationObject commObject = sender as ICommunicationObject;
                if (commObject != null)
                {
                    commObject.Faulted -= CommObject_FaultedOrClosed;
                    commObject.Closed -= CommObject_FaultedOrClosed;
                }
            }
        }


        // --- Método auxiliar para limpiar callbacks ---
        private void removeCallbackChannel(string username)
        {
            if (!string.IsNullOrEmpty(username))
            {
                if (userCallbacks.TryRemove(username, out IMatchmakingCallback removedChannel))
                {
                    Console.WriteLine($"Callback channel removed for user: {username}");
                    // Limpiar también al usuario de los lobbies activos
                    matchmakingLogic.handleUserDisconnect(username);

                    // Intentar cerrar/abortar el canal removido
                    ICommunicationObject commObject = removedChannel as ICommunicationObject;
                    if (commObject != null)
                    {
                        commObject.Faulted -= CommObject_FaultedOrClosed; // Desuscribirse
                        commObject.Closed -= CommObject_FaultedOrClosed;  // Desuscribirse
                        try
                        {
                            if (commObject.State != CommunicationState.Closed && commObject.State != CommunicationState.Closing)
                            {
                                if (commObject.State != CommunicationState.Faulted)
                                {
                                    commObject.Close();
                                }
                                else
                                {
                                    commObject.Abort();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Exception while closing/aborting removed channel for {username}: {ex.Message}");
                            commObject.Abort(); // Forzar aborto si falla el cierre limpio
                        }
                    }
                }
                // else { Console.WriteLine($"Attempted to remove callback for {username}, but was not found."); } // Opcional: Log si no se encontró
            }
        }

        // --- Implementaciones de la interfaz ---

        public async Task<LobbyCreationResultDto> createLobby(string hostUsername, LobbySettingsDto settingsDto)
        {
            // Registrar callback ANTES de llamar a la lógica
            getCurrentCallbackChannel(hostUsername);

            if (string.IsNullOrWhiteSpace(hostUsername) || settingsDto == null)
            {
                return new LobbyCreationResultDto { success = false, message = "Host username and settings required.", lobbyCode = null, initialLobbyState = null }; // TODO: Lang
            }
            try
            {
                // Llama a la lógica de negocio
                return await matchmakingLogic.createLobbyAsync(hostUsername, settingsDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating lobby: {ex.ToString()}"); // Log completo
                return new LobbyCreationResultDto { success = false, message = "Server error during lobby creation.", lobbyCode = null, initialLobbyState = null }; // TODO: Lang
            }
        }

        public void joinLobby(string username, string lobbyCode)
        {
            Console.WriteLine($"joinLobby called: User={username}, LobbyCode={lobbyCode}");
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyCode))
            {
                Console.WriteLine("JoinLobby Error: Username or LobbyCode is empty.");
                return;
            }

            // Registrar/Actualizar callback del usuario que se une
            IMatchmakingCallback joiningUserCallback = getCurrentCallbackChannel(username);
            if (joiningUserCallback == null)
            {
                Console.WriteLine($"JoinLobby Error: Could not get callback channel for {username}.");
                return;
            }

            try
            {
                // Llamar a la lógica de negocio
                matchmakingLogic.joinLobby(username, lobbyCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during joinLobby logic for {username} in {lobbyCode}: {ex.ToString()}"); // Log completo
                // Considerar enviar callback de error si defines uno
                matchmakingLogic.sendCallbackToUser(username, cb => cb.lobbyCreationFailed($"Failed to join lobby: Server error.")); // Reutiliza lobbyCreationFailed
            }
        }

        public void leaveLobby(string username, string lobbyCode)
        {
            Console.WriteLine($"leaveLobby called: User={username}, LobbyCode={lobbyCode}");
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyCode)) return;

            try
            {
                matchmakingLogic.leaveLobby(username, lobbyCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during leaveLobby logic for {username} in {lobbyCode}: {ex.ToString()}");
            }
            finally // Siempre intentar limpiar el canal al salir explícitamente
            {
                // Nota: removeCallbackChannel también llama a handleUserDisconnect
                this.removeCallbackChannel(username);
            }
        }

        public void startGame(string hostUsername, string lobbyCode)
        {
            Console.WriteLine($"startGame called: Host={hostUsername}, LobbyCode={lobbyCode}");
            if (string.IsNullOrWhiteSpace(hostUsername) || string.IsNullOrWhiteSpace(lobbyCode)) return;

            try
            {
                matchmakingLogic.startGame(hostUsername, lobbyCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during startGame logic for lobby {lobbyCode}: {ex.ToString()}");
                // Considerar notificar al host que falló el inicio
                matchmakingLogic.sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed($"Failed to start game: Server error.")); // Reutiliza callback
            }
        }

        public void kickPlayer(string hostUsername, string playerToKickUsername, string lobbyCode)
        {
            Console.WriteLine($"kickPlayer called: Host={hostUsername}, Kicked={playerToKickUsername}, LobbyCode={lobbyCode}");
            if (string.IsNullOrWhiteSpace(hostUsername) || string.IsNullOrWhiteSpace(playerToKickUsername) || string.IsNullOrWhiteSpace(lobbyCode)) return;

            try
            {
                matchmakingLogic.kickPlayer(hostUsername, playerToKickUsername, lobbyCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during kickPlayer logic in lobby {lobbyCode}: {ex.ToString()}");
                // Considerar notificar al host que falló la expulsión
                matchmakingLogic.sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed($"Failed to kick player: Server error.")); // Reutiliza callback
            }
        }

        public void inviteToLobby(string inviterUsername, string invitedUsername, string lobbyCode)
        {
            Console.WriteLine($"inviteToLobby called: Inviter={inviterUsername}, Invited={invitedUsername}, Lobby={lobbyCode}");
            if (string.IsNullOrWhiteSpace(inviterUsername) || string.IsNullOrWhiteSpace(invitedUsername) || string.IsNullOrWhiteSpace(lobbyCode)) return;

            try
            {
                matchmakingLogic.inviteToLobby(inviterUsername, invitedUsername, lobbyCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during inviteToLobby logic from {inviterUsername} to {invitedUsername}: {ex.ToString()}");
                // Considerar notificar al que invita que falló la invitación
                // matchmakingLogic.sendCallbackToUser(inviterUsername, cb => cb. ??? ); // Necesitaría un callback específico
            }
        }
    }
}