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
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class SocialManagerService : ISocialManager
    {
        public static readonly ConcurrentDictionary<string, ISocialCallback> ConnectedUsers =
            new ConcurrentDictionary<string, ISocialCallback>(StringComparer.OrdinalIgnoreCase); // Comparador case-insensitive

        private readonly SocialLogic socialLogic;
        private string currentUsername = null;
        private ISocialCallback currentUserCallback = null;

        public SocialManagerService()
        {
            var dbContext = new MindWeaveDBEntities1();
            var playerRepository = new PlayerRepository(dbContext);
            var friendshipRepository = new FriendshipRepository(dbContext);
            this.socialLogic = new SocialLogic(playerRepository, friendshipRepository);

            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += Channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed += Channel_FaultedOrClosed;
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] WARNING: SocialManagerService created without OperationContext!");
            }
        }

        public async Task connect(string username)
        {
            if (string.IsNullOrWhiteSpace(username) || OperationContext.Current == null)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] Connect attempt failed: Invalid username or OperationContext.");
                return;
            }

            currentUserCallback = OperationContext.Current.GetCallbackChannel<ISocialCallback>();
            currentUsername = username;

            if (currentUserCallback == null)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] Connect failed for {username}: Could not get callback channel.");
                return;
            }

            bool added = ConnectedUsers.TryAdd(currentUsername, currentUserCallback);
            if (!added)
            {
                if (ConnectedUsers.TryGetValue(currentUsername, out var existingCallback))
                {
                    var existingComm = existingCallback as ICommunicationObject;
                    if (existingCallback != currentUserCallback || existingComm == null || existingComm.State != CommunicationState.Opened)
                    {
                        ConnectedUsers[currentUsername] = currentUserCallback;
                        Console.WriteLine($"[{DateTime.UtcNow:O}] Updated callback channel for already connected user: {currentUsername}");
                        if (existingComm != null) CleanupCallbackEvents(existingComm);
                    }
                }
                else
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] Failed to add or update user {currentUsername} in connectedUsers dictionary.");
                    return;
                }
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] User '{currentUsername}' connected and callback registered.");
            }

            SetupCallbackEvents(currentUserCallback as ICommunicationObject);
            await notifyFriendsStatusChange(currentUsername, true);
        }

        // *** MÉTODO disconnect (modificado para usar ConnectedUsers) ***
        public async Task disconnect(string username)
        {
            // Usar cleanupAndNotifyDisconnect que ya maneja la lógica central
            Console.WriteLine($"[{DateTime.UtcNow:O}] User '{username}' requested disconnect.");
            await cleanupAndNotifyDisconnect(username);
        }

        // --- Otros métodos de ISocialManager (sin cambios lógicos aquí, llaman a socialLogic) ---
        public async Task<List<PlayerSearchResultDto>> searchPlayers(string requesterUsername, string query)
        {
            try { return await socialLogic.searchPlayersAsync(requesterUsername, query); }
            catch (Exception ex) { /* Log */ return new List<PlayerSearchResultDto>(); }
        }

        public async Task<OperationResultDto> sendFriendRequest(string requesterUsername, string targetUsername)
        {
            try
            {
                var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);
                if (result.success)
                {
                    // Notificar al destinatario SI está conectado
                    SendNotificationToUser(targetUsername, cb => cb.notifyFriendRequest(requesterUsername));
                }
                return result;
            }
            catch (Exception ex) { /* Log */ return new OperationResultDto { success = false, message = Lang.GenericServerError }; }
        }

        public async Task<OperationResultDto> respondToFriendRequest(string responderUsername, string requesterUsername, bool accepted)
        {
            try
            {
                var result = await socialLogic.respondToFriendRequestAsync(responderUsername, requesterUsername, accepted);
                if (result.success)
                {
                    // Notificar al solicitante SI está conectado
                    SendNotificationToUser(requesterUsername, cb => cb.notifyFriendResponse(responderUsername, accepted));
                    if (accepted)
                    {
                        // Notificar cambio de estado a amigos de AMBOS
                        await notifyFriendsStatusChange(responderUsername, true); // Asume que el que responde está online
                        await notifyFriendsStatusChange(requesterUsername, ConnectedUsers.ContainsKey(requesterUsername));
                    }
                }
                return result;
            }
            catch (Exception ex) { /* Log */ return new OperationResultDto { success = false, message = Lang.GenericServerError }; }
        }

        public async Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername)
        {
            try
            {
                var result = await socialLogic.removeFriendAsync(username, friendToRemoveUsername);
                if (result.success)
                {
                    // Notificar a ambos (si están online) que ya no son amigos (reusando status changed)
                    SendNotificationToUser(friendToRemoveUsername, cb => cb.notifyFriendStatusChanged(username, false));
                    SendNotificationToUser(username, cb => cb.notifyFriendStatusChanged(friendToRemoveUsername, false)); // También a sí mismo? Opcional
                }
                return result;
            }
            catch (Exception ex) { /* Log */ return new OperationResultDto { success = false, message = Lang.GenericServerError }; }
        }

        public async Task<List<FriendDto>> getFriendsList(string username)
        {
            try { return await socialLogic.getFriendsListAsync(username, ConnectedUsers.Keys); } // Pasa la lista de conectados
            catch (Exception ex) { /* Log */ return new List<FriendDto>(); }
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequests(string username)
        {
            try { return await socialLogic.getFriendRequestsAsync(username); }
            catch (Exception ex) { /* Log */ return new List<FriendRequestInfoDto>(); }
        }

        // --- Manejo de Desconexión y Notificaciones (modificado para usar ConnectedUsers) ---
        private async void Channel_FaultedOrClosed(object sender, EventArgs e)
        {
            Console.WriteLine($"[{DateTime.UtcNow:O}] Channel Faulted or Closed detected for user: {currentUsername ?? "UNKNOWN"}. Cleaning up.");
            // Usar el username guardado en la instancia de sesión
            if (!string.IsNullOrEmpty(currentUsername))
            {
                await cleanupAndNotifyDisconnect(currentUsername);
            }
            CleanupCallbackEvents(sender as ICommunicationObject);
        }

        private async Task cleanupAndNotifyDisconnect(string username)
        {
            if (string.IsNullOrEmpty(username)) return;

            // *** Usa el diccionario estático ***
            if (ConnectedUsers.TryRemove(username, out ISocialCallback removedChannel))
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] User '{username}' removed from ConnectedUsers dictionary.");
                CleanupCallbackEvents(removedChannel as ICommunicationObject);
                await notifyFriendsStatusChange(username, false); // Notificar DESPUÉS de remover
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] Attempted cleanup for {username}, but not found in ConnectedUsers.");
            }
        }

        private async Task notifyFriendsStatusChange(string changedUsername, bool isOnline)
        {
            // (Sin cambios lógicos internos, pero usa ConnectedUsers para verificar a quién notificar)
            Console.WriteLine($"[{DateTime.UtcNow:O}] Notifying friends of {changedUsername}'s status: {(isOnline ? "Online" : "Offline")}");
            List<FriendDto> friendsToNotify = await socialLogic.getFriendsListAsync(changedUsername, null); // Obtener todos los amigos
            Console.WriteLine($"[{DateTime.UtcNow:O}] Found {friendsToNotify.Count} friends for {changedUsername}.");

            foreach (var friend in friendsToNotify)
            {
                // *** Usa el diccionario estático para verificar si el amigo está online ***
                if (ConnectedUsers.TryGetValue(friend.username, out ISocialCallback friendCallback))
                {
                    SendNotificationToUser(friend.username, cb => cb.notifyFriendStatusChanged(changedUsername, isOnline));
                }
                else { Console.WriteLine($"[{DateTime.UtcNow:O}] Friend {friend.username} offline. Skipping status notification."); }
            }
            Console.WriteLine($"[{DateTime.UtcNow:O}] Finished notifying friends of {changedUsername}'s status.");
        }


        // *** NUEVO: Método estático para enviar notificaciones ***
        public static void SendNotificationToUser(string targetUsername, Action<ISocialCallback> action)
        {
            if (string.IsNullOrWhiteSpace(targetUsername)) return;

            // *** Usa el diccionario estático ***
            if (ConnectedUsers.TryGetValue(targetUsername, out ISocialCallback callbackChannel))
            {
                try
                {
                    var commObject = callbackChannel as ICommunicationObject;
                    if (commObject != null && commObject.State == CommunicationState.Opened)
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:O}] --> Sending callback notification to {targetUsername}.");
                        action(callbackChannel);
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:O}] Could not send notification to {targetUsername}, channel state is {commObject?.State}.");
                        // Considerar remover el canal si no está abierto (posible desconexión abrupta no detectada)
                        // Task.Run(async () => await Instance?.cleanupAndNotifyDisconnect(targetUsername)); // Podría causar problemas si Instance es null
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] !!! Exception sending callback to {targetUsername}: {ex.Message}");
                    // Considerar remover el canal aquí también
                }
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] Cannot send notification, user {targetUsername} not found in ConnectedUsers.");
            }
        }


        // --- Helpers para suscribir/desuscribir eventos (sin cambios) ---
        private void CleanupCallbackEvents(ICommunicationObject commObject) { /* ... */ }
        private void SetupCallbackEvents(ICommunicationObject commObject) { /* ... */ }

    }
}