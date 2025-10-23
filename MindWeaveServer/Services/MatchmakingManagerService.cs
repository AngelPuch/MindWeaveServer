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
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class MatchmakingManagerService : IMatchmakingManager
    {
        // La lógica AHORA se crea por instancia (sesión)
        private readonly MatchmakingLogic matchmakingLogic;

        // ESTOS DEBEN SER ESTÁTICOS para ser compartidos entre TODAS las instancias/sesiones
        private static readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbies = new ConcurrentDictionary<string, LobbyStateDto>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, IMatchmakingCallback> userCallbacks = new ConcurrentDictionary<string, IMatchmakingCallback>(StringComparer.OrdinalIgnoreCase);

        private string currentUsername = null; // Username para ESTA sesión
        private IMatchmakingCallback currentUserCallback = null; // Callback para ESTA sesión

        // Constructor de la instancia de servicio (se llama una vez por sesión de cliente)
        public MatchmakingManagerService()
        {
            Console.WriteLine("==> MatchmakingManagerService INSTANCE CONSTRUCTOR called.");
            // Pasa las colecciones ESTÁTICAS compartidas a la lógica (que ahora es por instancia)
            this.matchmakingLogic = new MatchmakingLogic(activeLobbies, userCallbacks);

            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += Channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed += Channel_FaultedOrClosed;
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] WARNING: MatchmakingManagerService created without OperationContext!");
            }
        }

        public async Task<LobbyCreationResultDto> createLobby(string hostUsername, LobbySettingsDto settingsDto)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: createLobby ENTRY for user: {hostUsername}");
            // Asegura que el callback de matchmaking esté registrado para esta sesión
            currentUserCallback = getCurrentCallbackChannel(hostUsername);
            if (currentUserCallback == null) { /* Error crítico */ return new LobbyCreationResultDto { /*...*/ }; }

            if (string.IsNullOrWhiteSpace(hostUsername) || settingsDto == null) { /* Error Input */ return new LobbyCreationResultDto { /*...*/ }; }

            try { return await matchmakingLogic.createLobbyAsync(hostUsername, settingsDto); }
            catch (Exception ex) { /* Log Fatal */ return new LobbyCreationResultDto { /*...*/ }; }
        }

        // --- getCurrentCallbackChannel ---
        // (Sin cambios lógicos, pero ahora opera en el contexto de una sesión)
        private IMatchmakingCallback getCurrentCallbackChannel(string username)
        {
            // (Lógica igual a SocialManagerService, pero usando userCallbacks)
            if (OperationContext.Current == null) { /* Log */ return null; }
            var currentCallback = OperationContext.Current.GetCallbackChannel<IMatchmakingCallback>();
            if (currentCallback != null && !string.IsNullOrEmpty(username))
            {
                currentUsername = username; // Guardar para esta sesión
                userCallbacks.AddOrUpdate(username, currentCallback, (key, existingVal) =>
                {
                    var existingComm = existingVal as ICommunicationObject;
                    if (existingComm == null || existingComm.State != CommunicationState.Opened)
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:O}] Updating stale matchmaking callback for {username}.");
                        if (existingComm != null) CleanupCallbackEvents(existingComm);
                        return currentCallback;
                    }
                    return existingVal;
                });

                if (userCallbacks.TryGetValue(username, out currentCallback))
                {
                    SetupCallbackEvents(currentCallback as ICommunicationObject);
                }
                else { Console.WriteLine($"!!! CRITICAL: Failed to retrieve matchmaking callback for {username} after AddOrUpdate."); }
            }
            return currentCallback;
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
        public void joinLobby(string username, string lobbyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: joinLobby ENTRY for user: {username}, lobby: {lobbyId}");
            // Asegura que el callback de matchmaking esté registrado para esta sesión
            currentUserCallback = getCurrentCallbackChannel(username);
            if (currentUserCallback == null) { /* Error crítico */ return; }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId)) { /* Log */ return; }
            try { matchmakingLogic.joinLobby(username, lobbyId); }
            catch (Exception ex) { /* Log */ }
        }

        public void leaveLobby(string username, string lobbyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: leaveLobby ENTRY for user: {username}, lobby: {lobbyId}");
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId)) { /* Log */ return; }
            try { matchmakingLogic.leaveLobby(username, lobbyId); }
            catch (Exception ex) { /* Log */ }
            // La limpieza del callback se maneja en Faulted/Closed o al desconectar explícitamente
        }

        public void startGame(string hostUsername, string lobbyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: startGame ENTRY by: {hostUsername}, lobby: {lobbyId}");
            // No necesita registrar callback aquí
            if (string.IsNullOrWhiteSpace(hostUsername) || string.IsNullOrWhiteSpace(lobbyId)) { /* Log */ return; }
            try { matchmakingLogic.startGame(hostUsername, lobbyId); }
            catch (Exception ex) { /* Log */ }
        }

        public void kickPlayer(string hostUsername, string playerToKickUsername, string lobbyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: kickPlayer ENTRY by: {hostUsername}, kicking: {playerToKickUsername}, lobby: {lobbyId}");
            // No necesita registrar callback aquí
            if (string.IsNullOrWhiteSpace(hostUsername) || string.IsNullOrWhiteSpace(playerToKickUsername) || string.IsNullOrWhiteSpace(lobbyId)) { /* Log */ return; }
            try { matchmakingLogic.kickPlayer(hostUsername, playerToKickUsername, lobbyId); }
            catch (Exception ex) { /* Log */ }
        }
        public void inviteToLobby(string inviterUsername, string invitedUsername, string lobbyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: inviteToLobby ENTRY from: {inviterUsername}, to: {invitedUsername}, lobby: {lobbyId}");
            // No necesita registrar callback aquí, solo llama a la lógica
            if (string.IsNullOrWhiteSpace(inviterUsername) || string.IsNullOrWhiteSpace(invitedUsername) || string.IsNullOrWhiteSpace(lobbyId)) { /* Log */ return; }
            try { matchmakingLogic.inviteToLobby(inviterUsername, invitedUsername, lobbyId); }
            catch (Exception ex) { /* Log */ }
        }


        public void changeDifficulty(string hostUsername, string lobbyId, int newDifficultyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: changeDifficulty ENTRY by: {hostUsername}, lobby: {lobbyId}, newDiff: {newDifficultyId}");
            // No necesita registrar callback aquí
            if (string.IsNullOrWhiteSpace(hostUsername) || string.IsNullOrWhiteSpace(lobbyId) || newDifficultyId <= 0) { /* Log */ return; }
            try { matchmakingLogic.changeDifficulty(hostUsername, lobbyId, newDifficultyId); }
            catch (Exception ex) { /* Log */ }
        }





        private void Channel_FaultedOrClosed(object sender, EventArgs e)
        {
            Console.WriteLine($"[{DateTime.UtcNow:O}] Matchmaking Channel Faulted/Closed for: {currentUsername ?? "UNKNOWN"}");
            if (!string.IsNullOrEmpty(currentUsername))
            {
                cleanupAndNotifyDisconnect(currentUsername); // Llama a la limpieza lógica
            }
            CleanupCallbackEvents(sender as ICommunicationObject); // Limpia eventos locales
        }

        private void cleanupAndNotifyDisconnect(string username)
        {
            if (string.IsNullOrEmpty(username)) return;

            // Limpiar de la lista ESTÁTICA de callbacks
            if (userCallbacks.TryRemove(username, out IMatchmakingCallback removedChannel))
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] Matchmaking callback removed for {username}.");
                CleanupCallbackEvents(removedChannel as ICommunicationObject); // Limpia eventos del canal removido

                // Notificar a la lógica para que lo saque de los lobbies
                matchmakingLogic.handleUserDisconnect(username);
            }
            else { Console.WriteLine($"[{DateTime.UtcNow:O}] Attempted matchmaking cleanup for {username}, but not found."); }
        }

        // --- Helpers para suscribir/desuscribir eventos (igual que SocialManagerService) ---
        private void CleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= Channel_FaultedOrClosed;
                commObject.Closed -= Channel_FaultedOrClosed;
            }
        }
        private void SetupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= Channel_FaultedOrClosed; // Prevenir duplicados
                commObject.Closed -= Channel_FaultedOrClosed;
                commObject.Faulted += Channel_FaultedOrClosed;
                commObject.Closed += Channel_FaultedOrClosed;
            }
        }




    } // Fin clase MatchmakingManagerService
}