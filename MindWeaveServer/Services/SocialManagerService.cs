using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Resources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    // NO LLEVA [ServiceContract] aquí
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class SocialManagerService : ISocialManager // Implementa la interfaz actualizada
    {
        private static readonly ConcurrentDictionary<string, ISocialCallback> connectedUsers =
            new ConcurrentDictionary<string, ISocialCallback>();
        private readonly SocialLogic socialLogic;

        private string currentUsername = null; // Almacena el nombre de usuario para esta sesión
        private ISocialCallback currentUserCallback = null; // Almacena el callback para esta sesión

        // Constructor... (igual que antes)
        public SocialManagerService()
        {
            var dbContext = new MindWeaveDBEntities1();
            var playerRepository = new PlayerRepository(dbContext);
            var friendshipRepository = new FriendshipRepository(dbContext);
            this.socialLogic = new SocialLogic(playerRepository, friendshipRepository);

            OperationContext.Current.Channel.Faulted += Channel_FaultedOrClosed;
            OperationContext.Current.Channel.Closed += Channel_FaultedOrClosed;
        }

        public async Task connect(string username)
        {
            if (string.IsNullOrWhiteSpace(username) || OperationContext.Current == null)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] Connect attempt failed: Invalid username or OperationContext.");
                // Podríamos lanzar una FaultException si el cliente necesita saberlo
                return;
            }

            // Obtener el canal de callback para ESTA sesión
            currentUserCallback = OperationContext.Current.GetCallbackChannel<ISocialCallback>();
            currentUsername = username; // Guardar el nombre de usuario para esta sesión

            if (currentUserCallback == null)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] Connect failed for {username}: Could not get callback channel.");
                return;
            }

            // Añadir o actualizar el usuario en el diccionario ESTÁTICO
            bool added = connectedUsers.TryAdd(currentUsername, currentUserCallback);
            if (!added)
            {
                // Si ya existía, intentamos actualizar (podría ser una reconexión)
                if (connectedUsers.TryGetValue(currentUsername, out var existingCallback))
                {
                    // Reemplazar solo si el canal es diferente o el existente está fallido
                    var existingComm = existingCallback as ICommunicationObject;
                    if (existingCallback != currentUserCallback || existingComm == null || existingComm.State != CommunicationState.Opened)
                    {
                        connectedUsers[currentUsername] = currentUserCallback;
                        Console.WriteLine($"[{DateTime.UtcNow:O}] Updated callback channel for already connected user: {currentUsername}");
                        if (existingComm != null) CleanupCallbackEvents(existingComm); // Limpiar eventos del viejo canal
                    }
                }
                else // Falló el TryAdd y el TryGetValue, situación extraña
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] Failed to add or update user {currentUsername} in connectedUsers dictionary.");
                    return; // No continuar si no podemos registrarlo
                }
            }
            else // Se añadió correctamente
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] User '{currentUsername}' connected and callback registered.");
            }

            // Asociar eventos al NUEVO canal (o al actualizado)
            SetupCallbackEvents(currentUserCallback as ICommunicationObject);


            // Notificar a los amigos que este usuario se conectó
            await notifyFriendsStatusChange(currentUsername, true);
        }

        // --- Métodos que implementan la interfaz ISocialManager ---
        // SIN [OperationContract] aquí
        // *** NUEVO: Método Disconnect para limpiar y notificar ***
        public async Task disconnect(string username)
        {
            Console.WriteLine($"[{DateTime.UtcNow:O}] User '{username}' requested disconnect.");
            await cleanupAndNotifyDisconnect(username);
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayers(string requesterUsername, string query)
        {
            // ... (implementación igual que antes)
            try
            {
                return await socialLogic.searchPlayersAsync(requesterUsername, query);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in searchPlayers: {ex}");
                return new List<PlayerSearchResultDto>();
            }
        }

        public async Task<OperationResultDto> sendFriendRequest(string requesterUsername, string targetUsername)
        {
            try
            {
                var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);
                if (result.success)
                {
                    // Si el destinatario está conectado, notificarle por callback
                    if (connectedUsers.TryGetValue(targetUsername, out ISocialCallback targetCallback))
                    {
                        try
                        {
                            Console.WriteLine($"[{DateTime.UtcNow:O}] Notifying {targetUsername} of friend request from {requesterUsername}.");
                            targetCallback.notifyFriendRequest(requesterUsername);
                        }
                        catch (Exception cbEx) { Console.WriteLine($"[{DateTime.UtcNow:O}] Error sending notifyFriendRequest callback to {targetUsername}: {cbEx.Message}"); }
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:O}] Target user {targetUsername} not online for friend request notification.");
                    }
                }
                return result;
            }
            catch (Exception ex) { /* Log error */ return new OperationResultDto { success = false, message = Lang.GenericServerError }; }
        }

        public async Task<OperationResultDto> respondToFriendRequest(string responderUsername, string requesterUsername, bool accepted)
        {
            try
            {
                var result = await socialLogic.respondToFriendRequestAsync(responderUsername, requesterUsername, accepted);
                if (result.success)
                {
                    // Notificar al solicitante (requester) si está conectado
                    if (connectedUsers.TryGetValue(requesterUsername, out ISocialCallback requesterCallback))
                    {
                        try
                        {
                            Console.WriteLine($"[{DateTime.UtcNow:O}] Notifying {requesterUsername} of response from {responderUsername} (Accepted: {accepted}).");
                            requesterCallback.notifyFriendResponse(responderUsername, accepted);
                        }
                        catch (Exception cbEx) { Console.WriteLine($"[{DateTime.UtcNow:O}] Error sending notifyFriendResponse callback to {requesterUsername}: {cbEx.Message}"); }

                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:O}] Requester user {requesterUsername} not online for friend response notification.");
                    }

                    // Si aceptó, notificar cambio de estado a ambos (si están online)
                    if (accepted)
                    {
                        await notifyFriendsStatusChange(responderUsername, true); // Notificar a los amigos del que respondió
                        await notifyFriendsStatusChange(requesterUsername, connectedUsers.ContainsKey(requesterUsername)); // Notificar a los amigos del que solicitó
                    }
                }
                return result;
            }
            catch (Exception ex) { /* Log error */ return new OperationResultDto { success = false, message = Lang.GenericServerError }; }
        }

        public async Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername)
        {
            try
            {
                var result = await socialLogic.removeFriendAsync(username, friendToRemoveUsername);
                if (result.success)
                {
                    // Notificar al amigo eliminado (si está conectado) que ya no son amigos
                    if (connectedUsers.TryGetValue(friendToRemoveUsername, out ISocialCallback removedFriendCallback))
                    {
                        // Podríamos añadir un callback específico "notifyFriendRemoved" o reusar status changed a offline para él
                        try
                        {
                            Console.WriteLine($"[{DateTime.UtcNow:O}] Notifying {friendToRemoveUsername} they were removed by {username}.");
                            // Ejemplo reusando status changed (puede ser confuso, mejor un callback dedicado si es posible)
                            removedFriendCallback.notifyFriendStatusChanged(username, false); // Le dice que 'username' está offline para él
                        }
                        catch (Exception cbEx) { Console.WriteLine($"[{DateTime.UtcNow:O}] Error sending remove notification callback to {friendToRemoveUsername}: {cbEx.Message}"); }

                    }
                    // Notificar a los amigos de 'username' que 'friendToRemoveUsername' ya no es amigo (si están online)
                    // (Esto requeriría obtener la lista de amigos de 'username' y luego notificarles)
                    // Por simplicidad, omitimos esta notificación masiva por ahora. El cliente refrescará su lista eventualmente.
                }
                return result;
            }
            catch (Exception ex) { /* Log error */ return new OperationResultDto { success = false, message = Lang.GenericServerError }; }
        }


        public async Task<List<FriendDto>> getFriendsList(string username)
        {
            try
            {
                // *** CAMBIO: La lógica ahora recibe el diccionario de usuarios conectados ***
                return await socialLogic.getFriendsListAsync(username, connectedUsers.Keys);
            }
            catch (Exception ex) { /* Log error */ return new List<FriendDto>(); }
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequests(string username)
        {
            // No necesita saber quién está online, no cambia
            try { return await socialLogic.getFriendRequestsAsync(username); }
            catch (Exception ex) { /* Log error */ return new List<FriendRequestInfoDto>(); }
        }

        // **** ¡¡ELIMINA LOS MÉTODOS WRAPPER Y EL BLOQUE COMENTADO IsOneWay=true DE AQUÍ!! ****

        private async Task notifyFriendsStatusChange(string changedUsername, bool isOnline)
        {
            Console.WriteLine($"[{DateTime.UtcNow:O}] Preparing to notify friends of {changedUsername}'s status change (Online: {isOnline}).");
            List<FriendDto> friendsToNotify = await socialLogic.getFriendsListAsync(changedUsername, null); // Obtener amigos sin filtrar por online

            Console.WriteLine($"[{DateTime.UtcNow:O}] Found {friendsToNotify.Count} friends for {changedUsername}.");


            foreach (var friend in friendsToNotify)
            {
                // Verificar si el amigo está actualmente conectado
                if (connectedUsers.TryGetValue(friend.username, out ISocialCallback friendCallback))
                {
                    try
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:O}] --> Sending status change notification ({changedUsername} is {(isOnline ? "Online" : "Offline")}) to {friend.username}.");
                        friendCallback.notifyFriendStatusChanged(changedUsername, isOnline);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:O}] !!! Error sending status update callback to {friend.username}: {ex.Message}");
                        // Considerar remover el canal del amigo si falla repetidamente
                    }
                }
                else
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] Friend {friend.username} is not online. Skipping notification.");
                }
            }
            Console.WriteLine($"[{DateTime.UtcNow:O}] Finished notifying friends of {changedUsername}'s status change.");
        }

        // Manejador para eventos Faulted y Closed del canal de comunicación
        private async void Channel_FaultedOrClosed(object sender, EventArgs e)
        {
            Console.WriteLine($"[{DateTime.UtcNow:O}] Channel Faulted or Closed detected for user: {currentUsername ?? "UNKNOWN"}. Cleaning up.");
            // Usar el username guardado en la instancia de sesión
            if (!string.IsNullOrEmpty(currentUsername))
            {
                await cleanupAndNotifyDisconnect(currentUsername);
            }
            // Limpiar manejadores de eventos locales para este canal
            CleanupCallbackEvents(sender as ICommunicationObject);

        }

        // Método centralizado para limpiar diccionario y notificar desconexión
        private async Task cleanupAndNotifyDisconnect(string username)
        {
            if (string.IsNullOrEmpty(username)) return;

            ISocialCallback removedChannel;
            bool removed = connectedUsers.TryRemove(username, out removedChannel);

            if (removed)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] User '{username}' removed from connectedUsers dictionary.");
                // Limpiar eventos del canal que acabamos de remover
                CleanupCallbackEvents(removedChannel as ICommunicationObject);

                // Notificar a los amigos que este usuario se desconectó
                await notifyFriendsStatusChange(username, false);
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] Attempted to remove {username}, but they were not found in connectedUsers (possibly already removed).");
            }
        }

        // Helper para desuscribir eventos
        private void CleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= Channel_FaultedOrClosed;
                commObject.Closed -= Channel_FaultedOrClosed;
            }
        }
        // Helper para suscribir eventos
        private void SetupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                // Desuscribir primero por seguridad
                commObject.Faulted -= Channel_FaultedOrClosed;
                commObject.Closed -= Channel_FaultedOrClosed;
                // Suscribir
                commObject.Faulted += Channel_FaultedOrClosed;
                commObject.Closed += Channel_FaultedOrClosed;
            }
        }

    }
}