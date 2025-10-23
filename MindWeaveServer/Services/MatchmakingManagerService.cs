// MindWeaveServer/Services/MatchmakingManagerService.cs
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    // *** CAMBIO PRINCIPAL: PerSession para aislar instancias por cliente/sesión ***
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class MatchmakingManagerService : IMatchmakingManager
    {
        // La lógica AHORA se crea por instancia (sesión)
        private readonly MatchmakingLogic matchmakingLogic;

        // ESTOS DEBEN SER ESTÁTICOS para ser compartidos entre TODAS las instancias/sesiones
        private static readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbies = new ConcurrentDictionary<string, LobbyStateDto>();
        private static readonly ConcurrentDictionary<string, IMatchmakingCallback> userCallbacks = new ConcurrentDictionary<string, IMatchmakingCallback>();

        // Constructor de la instancia de servicio (se llama una vez por sesión de cliente)
        public MatchmakingManagerService()
        {
            Console.WriteLine("==> MatchmakingManagerService INSTANCE CONSTRUCTOR called.");
            // Pasa las colecciones ESTÁTICAS compartidas a la lógica (que ahora es por instancia)
            this.matchmakingLogic = new MatchmakingLogic(activeLobbies, userCallbacks);
        }

        public async Task<LobbyCreationResultDto> createLobby(string hostUsername, LobbySettingsDto settingsDto)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service Method: createLobby ENTRY for user: {hostUsername}");

            // Obtiene y registra/actualiza el callback para ESTA sesión/usuario
            IMatchmakingCallback callback = getCurrentCallbackChannel(hostUsername);
            // Si el callback es null aquí, hay un problema fundamental con la conexión del cliente
            if (callback == null)
            {
                Console.WriteLine($"!!! CRITICAL: Failed to get callback channel for {hostUsername} in createLobby.");
                return new LobbyCreationResultDto { success = false, message = "Failed to establish callback channel.", lobbyCode = null, initialLobbyState = null };
            }

            if (string.IsNullOrWhiteSpace(hostUsername) || settingsDto == null)
            {
                Console.WriteLine($"!!! Service Method: Invalid input for createLobby. Host: {hostUsername}, Settings null? {settingsDto == null}");
                // TODO: Lang Key
                return new LobbyCreationResultDto { success = false, message = "Host username and settings required.", lobbyCode = null, initialLobbyState = null };
            }

            try
            {
                Console.WriteLine($"{DateTime.UtcNow:O} --> Service Method: Calling matchmakingLogic.createLobbyAsync for {hostUsername}");
                // Llama a la lógica de negocio (que usará su propio DbContext)
                LobbyCreationResultDto result = await matchmakingLogic.createLobbyAsync(hostUsername, settingsDto);
                Console.WriteLine($"{DateTime.UtcNow:O} <-- Service Method: matchmakingLogic.createLobbyAsync returned. Success: {result.success}. Message: {result.message}");

                // Si falló en la lógica, simplemente devuelve el resultado de error
                if (!result.success)
                {
                    return result;
                }

                // Si tuvo éxito, la lógica ya preparó el DTO. Solo lo devolvemos.
                Console.WriteLine($"{DateTime.UtcNow:O} ==> Service Method: Returning SUCCESS result for lobby {result.lobbyCode} to {hostUsername}");
                return result;
            }
            catch (Exception ex) // Captura errores INESPERADOS *en esta capa de servicio*
            {
                Console.WriteLine($"!!! FATAL EXCEPTION in MatchmakingManagerService.createLobby for {hostUsername}: {ex.ToString()}");
                // Devuelve un DTO de error genérico (simplificado)
                return new LobbyCreationResultDto
                {
                    success = false,
                    message = "Unexpected server error during lobby creation.", // Mensaje simple
                    lobbyCode = null,
                    initialLobbyState = null
                };
            }
        }

        // --- getCurrentCallbackChannel ---
        // (Sin cambios lógicos, pero ahora opera en el contexto de una sesión)
        private IMatchmakingCallback getCurrentCallbackChannel(string username)
        {
            if (OperationContext.Current == null)
            {
                Console.WriteLine($"!!! Warning: OperationContext.Current is null when trying to get callback for {username} in PerSession service.");
                // En PerSession, esto no debería ocurrir una vez establecida la sesión.
                return null;
            }

            IMatchmakingCallback currentCallback = OperationContext.Current.GetCallbackChannel<IMatchmakingCallback>();

            if (currentCallback != null && !string.IsNullOrEmpty(username))
            {
                // Almacena o actualiza el canal ESTATICO para este usuario
                userCallbacks.AddOrUpdate(username, currentCallback, (key, existingVal) =>
                {
                    ICommunicationObject existingComm = existingVal as ICommunicationObject;
                    // Ya no necesitamos comparar currentCallback con existingVal porque en PerSession,
                    // el canal debería ser estable mientras la sesión esté activa.
                    // Solo actualizamos si el canal existente está mal (no abierto).
                    if (existingComm == null || existingComm.State != CommunicationState.Opened)
                    {
                        Console.WriteLine($"Callback channel state issue for user {username}. Current state: {existingComm?.State}. Updating channel.");
                        if (existingComm != null)
                        {
                            // Intenta limpiar eventos del canal viejo ANTES de reemplazarlo
                            existingComm.Faulted -= CommObject_FaultedOrClosed;
                            existingComm.Closed -= CommObject_FaultedOrClosed;
                            try { if (existingComm.State != CommunicationState.Closed) existingComm.Abort(); } catch { } // Abortar si no está cerrado
                        }
                        return currentCallback; // Devolver el nuevo canal
                    }
                    // Console.WriteLine($"Callback channel for user {username} is already open and valid.");
                    return existingVal; // Mantener el canal existente si está abierto
                });

                // Re-obtener y asociar manejadores (importante hacerlo siempre por si AddOrUpdate lo cambió)
                if (userCallbacks.TryGetValue(username, out currentCallback))
                {
                    ICommunicationObject commObject = currentCallback as ICommunicationObject;
                    if (commObject != null)
                    {
                        // Limpiar manejadores anteriores para evitar duplicados
                        commObject.Faulted -= CommObject_FaultedOrClosed;
                        commObject.Closed -= CommObject_FaultedOrClosed;
                        // Añadir manejadores
                        commObject.Faulted += CommObject_FaultedOrClosed;
                        commObject.Closed += CommObject_FaultedOrClosed;
                    }
                    // Console.WriteLine($"Callback channel event handlers updated for user: {username}");
                }
                else
                {
                    Console.WriteLine($"!!! CRITICAL: Failed to retrieve callback channel for {username} immediately after AddOrUpdate.");
                }
            }
            else if (string.IsNullOrEmpty(username)) { /* Log */ } else { /* Log */ }

            return currentCallback; // Devuelve el canal obtenido/actualizado
        }

        // --- CommObject_FaultedOrClosed ---
        // (Sin cambios) - Se dispara cuando un canal de callback falla o se cierra
        private void CommObject_FaultedOrClosed(object sender, EventArgs e)
        {
            IMatchmakingCallback callbackChannel = sender as IMatchmakingCallback;
            if (callbackChannel != null)
            {
                // Buscar el usuario asociado a este canal específico
                var userEntry = userCallbacks.FirstOrDefault(pair => pair.Value == callbackChannel);
                if (!string.IsNullOrEmpty(userEntry.Key))
                {
                    Console.WriteLine($"Callback channel for {userEntry.Key} has Faulted or Closed.");
                    removeCallbackChannel(userEntry.Key); // Llama a la limpieza
                }
                else { /* Log Warning */ }

                // Desuscribirse para evitar fugas
                ICommunicationObject commObject = sender as ICommunicationObject;
                if (commObject != null)
                {
                    commObject.Faulted -= CommObject_FaultedOrClosed;
                    commObject.Closed -= CommObject_FaultedOrClosed;
                }
            }
        }

        // --- removeCallbackChannel ---
        // (Sin cambios lógicos) - Limpia el canal de un usuario específico
        private void removeCallbackChannel(string username)
        {
            if (!string.IsNullOrEmpty(username))
            {
                if (userCallbacks.TryRemove(username, out IMatchmakingCallback removedChannel))
                {
                    Console.WriteLine($"Callback channel explicitly removed for user: {username}");
                    // Limpiar también al usuario de los lobbies activos (llamando a la lógica)
                    matchmakingLogic.handleUserDisconnect(username); // Asegúrate que matchmakingLogic esté inicializado si se llama desde aquí

                    // Intentar cerrar/abortar el canal removido
                    ICommunicationObject commObject = removedChannel as ICommunicationObject;
                    if (commObject != null)
                    {
                        // Ya nos desuscribimos en Faulted/Closed, pero por si acaso se llama directamente
                        commObject.Faulted -= CommObject_FaultedOrClosed;
                        commObject.Closed -= CommObject_FaultedOrClosed;
                        try { /* Intenta cerrar/abortar limpiamente */ } catch { /* Abortar si falla */ }
                    }
                }
            }
        }

        // --- Implementaciones del resto de métodos de la interfaz (joinLobby, leaveLobby, etc.) ---
        // (Sin cambios, ya que delegan en la lógica de negocio)
        public void joinLobby(string username, string lobbyCode)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service Method: joinLobby ENTRY for user: {username}, lobby: {lobbyCode}");
            IMatchmakingCallback callback = getCurrentCallbackChannel(username); // Asegura que el callback esté registrado/actualizado
            if (callback == null)
            {
                Console.WriteLine($"!!! CRITICAL: Failed to get callback channel for {username} in joinLobby.");
                // No podemos notificar al cliente directamente aquí si no tenemos canal
                return;
            }
            // ... validaciones de input ...
            try { matchmakingLogic.joinLobby(username, lobbyCode); } catch (Exception ex) { /* Log y posible notificación de error */ }
        }

        public void leaveLobby(string username, string lobbyCode)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service Method: leaveLobby ENTRY for user: {username}, lobby: {lobbyCode}");
            // No necesitamos callback aquí usualmente, pero sí limpiar el existente
            // ... validaciones ...
            try { matchmakingLogic.leaveLobby(username, lobbyCode); } catch (Exception ex) { /* Log */ } finally { removeCallbackChannel(username); } // Limpiar al salir
        }
        public void startGame(string hostUsername, string lobbyCode)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service Method: startGame ENTRY for host: {hostUsername}, lobby: {lobbyCode}");
            // ... validaciones ...
            try { matchmakingLogic.startGame(hostUsername, lobbyCode); } catch (Exception ex) { /* Log y notificación al host */ }
        }

        public void kickPlayer(string hostUsername, string playerToKickUsername, string lobbyCode)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service Method: kickPlayer ENTRY by host: {hostUsername}, kicking: {playerToKickUsername}, lobby: {lobbyCode}");
            // ... validaciones ...
            try { matchmakingLogic.kickPlayer(hostUsername, playerToKickUsername, lobbyCode); } catch (Exception ex) { /* Log y notificación al host */ }
        }

        public void inviteToLobby(string inviterUsername, string invitedUsername, string lobbyCode)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service Method: inviteToLobby ENTRY from: {inviterUsername}, to: {invitedUsername}, lobby: {lobbyCode}");
            // ... validaciones ...
            try { matchmakingLogic.inviteToLobby(inviterUsername, invitedUsername, lobbyCode); } catch (Exception ex) { /* Log y notificación al invitador */ }
        }


        public void changeDifficulty(string hostUsername, string lobbyId, int newDifficultyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: changeDifficulty ENTRY by {hostUsername}, lobby: {lobbyId}, newDiff: {newDifficultyId}");
            if (string.IsNullOrWhiteSpace(hostUsername) || string.IsNullOrWhiteSpace(lobbyId) || newDifficultyId <= 0) return; // Validación básica
            try
            {
                // Llama a la lógica de negocio (que crearemos a continuación)
                matchmakingLogic.changeDifficulty(hostUsername, lobbyId, newDifficultyId);
            }
            catch (Exception ex) { Console.WriteLine($"!!! EXCEPTION in Service.changeDifficulty: {ex.Message}"); /* Consider notifying host */ }
        }

    } // Fin clase MatchmakingManagerService
}