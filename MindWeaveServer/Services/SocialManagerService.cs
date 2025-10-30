// MindWeaveServer/Services/SocialManagerService.cs
using MindWeaveServer.BusinessLogic;
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
using MindWeaveServer.Contracts.DataContracts.Shared;
using NLog; // ¡Añadir using para NLog!

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class SocialManagerService : ISocialManager
    {
        // Obtener instancia del logger (NOMBRE CORREGIDO)
        private static readonly Logger logger = LogManager.GetCurrentClassLogger(); // <--- NOMBRE CORREGIDO

        // Diccionario estático para usuarios conectados (sin cambios)
        public static readonly ConcurrentDictionary<string, ISocialCallback> ConnectedUsers =
            new ConcurrentDictionary<string, ISocialCallback>(StringComparer.OrdinalIgnoreCase);

        private readonly SocialLogic socialLogic;
        private string currentUsername; // Usuario asociado a ESTA instancia de servicio (sesión)
        private ISocialCallback currentUserCallback; // Callback asociado a ESTA instancia

        public SocialManagerService()
        {
            // ... (inicialización de dependencias igual que antes) ...
            var dbContext = new MindWeaveDBEntities1();
            var playerRepository = new PlayerRepository(dbContext);
            var friendshipRepository = new FriendshipRepository(dbContext);
            this.socialLogic = new SocialLogic(playerRepository, friendshipRepository);

            logger.Info("SocialManagerService instance created (PerSession). Waiting for connect call."); // <--- Log de instancia

            // Adjuntar handlers a los eventos del canal de la sesión actual
            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += Channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed += Channel_FaultedOrClosed;
                logger.Debug("Attached Faulted/Closed event handlers to the current WCF channel.");
            }
            else
            {
                logger.Warn("Could not attach channel event handlers - OperationContext or Channel is null.");
            }
        }

        public async Task connect(string username)
        {
            string userForContext = username ?? "NULL";
            logger.Info("Connect attempt started for user: {Username}", userForContext);

            if (string.IsNullOrWhiteSpace(username) || OperationContext.Current == null)
            {
                logger.Warn("Connect failed: Username is empty or OperationContext is null. User: {Username}", userForContext);
                return; // Salir si no hay username o contexto
            }

            // Obtener el callback ANTES de añadir/actualizar
            ISocialCallback callbackChannel = null;
            try
            {
                callbackChannel = OperationContext.Current.GetCallbackChannel<ISocialCallback>();
                if (callbackChannel == null)
                {
                    logger.Error("Connect failed: GetCallbackChannel returned null for user: {Username}", userForContext);
                    // ¿Deberíamos cerrar la sesión aquí? Podría ser abrupto.
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Connect failed: Exception getting callback channel for user: {Username}", userForContext);
                return;
            }

            // Asignar a la instancia actual ANTES de interactuar con el diccionario estático
            currentUserCallback = callbackChannel;
            currentUsername = username; // Asociar username a esta instancia de servicio

            logger.Debug("Attempting to add or update user in ConnectedUsers dictionary: {Username}", userForContext);

            // Añadir o actualizar en el diccionario estático
            ISocialCallback addedOrUpdatedCallback = ConnectedUsers.AddOrUpdate(
                currentUsername, // Usar el username de esta instancia
                currentUserCallback, // Usar el callback de esta instancia
                (key, existingCallback) =>
                {
                    var existingComm = existingCallback as ICommunicationObject;
                    // Si el callback existente es diferente y está cerrado/fallido, reemplazarlo
                    if (existingCallback != currentUserCallback && (existingComm == null || existingComm.State != CommunicationState.Opened))
                    {
                        logger.Warn("Replacing existing non-opened callback channel for user: {Username}", key);
                        if (existingComm != null) cleanupCallbackEvents(existingComm); // Limpiar handlers del viejo
                        return currentUserCallback; // Devolver el nuevo callback
                    }
                    // Si es el mismo o el existente está abierto, mantener el existente
                    logger.Debug("Keeping existing callback channel for user: {Username} (State: {State})", key, existingComm?.State);
                    // ¡Importante! Si mantenemos el existente, la instancia actual NO debe usar el nuevo `currentUserCallback`
                    if (existingCallback != currentUserCallback)
                    {
                        // Descartar el callback recién obtenido si no se usó
                        logger.Debug("Discarding newly obtained callback channel for {Username} as a valid one already exists.", key);
                        // NO necesitamos hacer nada más aquí, `currentUserCallback` en esta instancia será el existente
                        // al salir del AddOrUpdate si no se reemplazó. Pero es más claro reasignar:
                        currentUserCallback = existingCallback; // Asegurarse que esta instancia use el correcto
                    }
                    return existingCallback; // Mantener el existente
                });

            // Si el callback que quedó en el diccionario es el de ESTA instancia, configurar sus eventos
            if (addedOrUpdatedCallback == currentUserCallback)
            {
                logger.Info("User connected and callback registered/updated: {Username}", userForContext);
                setupCallbackEvents(currentUserCallback as ICommunicationObject); // Configurar eventos para el callback ACTIVO
                await notifyFriendsStatusChange(currentUsername, true); // Notificar amigos que se conectó
            }
            else
            {
                // Esto puede pasar si el usuario ya estaba conectado desde otra sesión/cliente y su canal seguía abierto.
                logger.Warn("User {Username} attempted to connect, but an existing active session was found. The new connection might replace the old one implicitly by WCF session management, or might coexist depending on configuration.", userForContext);
                // Asegurarse de que ESTA instancia use el callback existente
                currentUserCallback = addedOrUpdatedCallback;
                currentUsername = username; // Confirmar username asociado
                setupCallbackEvents(currentUserCallback as ICommunicationObject); // Configurar eventos para el callback ACTIVO
            }
        }

        public async Task disconnect(string username)
        {
            string userForContext = username ?? "NULL";
            // Verificar si el username coincide con el de esta sesión
            if (!string.IsNullOrEmpty(currentUsername) && currentUsername.Equals(userForContext, StringComparison.OrdinalIgnoreCase))
            {
                logger.Info("Disconnect requested by user: {Username}", userForContext);
                // Llamar al método centralizado de limpieza que también notifica a amigos
                await cleanupAndNotifyDisconnect(currentUsername);
            }
            else
            {
                // Log de advertencia si el username no coincide o si la sesión ya no tiene username
                logger.Warn("Disconnect called with username '{Username}' which does not match the current session user '{CurrentUsername}' or session is already cleaned up.", userForContext, currentUsername ?? "N/A");
            }
            // No intentar cerrar el canal aquí, WCF lo maneja al terminar la llamada (o en Faulted/Closed)
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayers(string requesterUsername, string query)
        {
            // Verificar si la sesión actual corresponde al solicitante
            if (!IsCurrentUser(requesterUsername))
            {
                logger.Warn("searchPlayers called by {RequesterUsername}, but current session is for {CurrentUsername}. Aborting.", requesterUsername, currentUsername ?? "N/A");
                return new List<PlayerSearchResultDto>(); // O lanzar FaultException?
            }
            logger.Info("SearchPlayers request from {RequesterUsername} with query: '{Query}'", requesterUsername, query ?? "");
            try
            {
                var results = await socialLogic.searchPlayersAsync(requesterUsername, query);
                logger.Info("SearchPlayers found {Count} results for query '{Query}' by {RequesterUsername}", results?.Count ?? 0, query ?? "", requesterUsername);
                return results ?? new List<PlayerSearchResultDto>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during searchPlayers for query '{Query}' by {RequesterUsername}", query ?? "", requesterUsername);
                return new List<PlayerSearchResultDto>(); // Devolver lista vacía en caso de error
            }
        }

        public async Task<OperationResultDto> sendFriendRequest(string requesterUsername, string targetUsername)
        {
            if (!IsCurrentUser(requesterUsername))
            {
                logger.Warn("sendFriendRequest called by {RequesterUsername}, but current session is for {CurrentUsername}. Aborting.", requesterUsername, currentUsername ?? "N/A");
                return new OperationResultDto { success = false, message = "Session mismatch." }; // TODO: Lang
            }
            logger.Info("sendFriendRequest attempt from {RequesterUsername} to {TargetUsername}", requesterUsername, targetUsername ?? "NULL");
            try
            {
                var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);
                if (result.success)
                {
                    logger.Info("Friend request sent successfully from {RequesterUsername} to {TargetUsername}", requesterUsername, targetUsername);
                    // La notificación al target se maneja dentro de la lógica o aquí si se prefiere
                    sendNotificationToUser(targetUsername, cb => cb.notifyFriendRequest(requesterUsername));
                }
                else
                {
                    logger.Warn("sendFriendRequest failed from {RequesterUsername} to {TargetUsername}. Reason: {Reason}", requesterUsername, targetUsername, result.message);
                }
                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during sendFriendRequest from {RequesterUsername} to {TargetUsername}", requesterUsername, targetUsername ?? "NULL");
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> respondToFriendRequest(string responderUsername, string requesterUsername, bool accepted)
        {
            if (!IsCurrentUser(responderUsername))
            {
                logger.Warn("respondToFriendRequest called by {ResponderUsername}, but current session is for {CurrentUsername}. Aborting.", responderUsername, currentUsername ?? "N/A");
                return new OperationResultDto { success = false, message = "Session mismatch." }; // TODO: Lang
            }
            logger.Info("respondToFriendRequest attempt by {ResponderUsername} to request from {RequesterUsername}. Accepted: {Accepted}", responderUsername, requesterUsername ?? "NULL", accepted);
            try
            {
                var result = await socialLogic.respondToFriendRequestAsync(responderUsername, requesterUsername, accepted);
                if (result.success)
                {
                    logger.Info("Friend request response ({Accepted}) processed successfully by {ResponderUsername} for request from {RequesterUsername}", accepted ? "Accepted" : "Declined", responderUsername, requesterUsername);
                    // Notificación y actualización de estado online (como estaba antes)
                    sendNotificationToUser(requesterUsername, cb => cb.notifyFriendResponse(responderUsername, accepted));
                    if (accepted)
                    {
                        // No necesitas verificar si están online aquí, notifyFriendsStatusChange lo hará internamente
                        await notifyFriendsStatusChange(responderUsername, true); // Notificar a los amigos del respondedor que está online (si no lo estaban ya)
                        await notifyFriendsStatusChange(requesterUsername, ConnectedUsers.ContainsKey(requesterUsername)); // Notificar a los amigos del solicitante su estado actual
                    }
                }
                else
                {
                    logger.Warn("respondToFriendRequest failed by {ResponderUsername} for request from {RequesterUsername}. Reason: {Reason}", responderUsername, requesterUsername, result.message);
                }
                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during respondToFriendRequest by {ResponderUsername} for request from {RequesterUsername}", responderUsername, requesterUsername ?? "NULL");
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername)
        {
            if (!IsCurrentUser(username))
            {
                logger.Warn("removeFriend called by {Username}, but current session is for {CurrentUsername}. Aborting.", username, currentUsername ?? "N/A");
                return new OperationResultDto { success = false, message = "Session mismatch." }; // TODO: Lang
            }
            logger.Info("removeFriend attempt by {Username} to remove {FriendToRemoveUsername}", username, friendToRemoveUsername ?? "NULL");
            try
            {
                var result = await socialLogic.removeFriendAsync(username, friendToRemoveUsername);
                if (result.success)
                {
                    logger.Info("Friend removed successfully: {Username} removed {FriendToRemoveUsername}", username, friendToRemoveUsername);
                    // Notificar a ambos que ya no son amigos (o que el otro se desconectó para ellos)
                    sendNotificationToUser(friendToRemoveUsername, cb => cb.notifyFriendStatusChanged(username, false)); // Le dice al amigo removido que el usuario se 'desconectó' de su lista
                    // Podríamos necesitar un callback específico para "Removed" o simplemente actualizar la lista del cliente que inició la acción
                }
                else
                {
                    logger.Warn("removeFriend failed for {Username} trying to remove {FriendToRemoveUsername}. Reason: {Reason}", username, friendToRemoveUsername, result.message);
                }
                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during removeFriend by {Username} for {FriendToRemoveUsername}", username, friendToRemoveUsername ?? "NULL");
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<List<FriendDto>> getFriendsList(string username)
        {
            if (!IsCurrentUser(username))
            {
                logger.Warn("getFriendsList called by {Username}, but current session is for {CurrentUsername}. Aborting.", username, currentUsername ?? "N/A");
                return new List<FriendDto>();
            }
            logger.Info("getFriendsList request for user: {Username}", username);
            try
            {
                // Pasamos las Keys del diccionario estático, que son los usernames conectados
                var friends = await socialLogic.getFriendsListAsync(username, ConnectedUsers.Keys);
                logger.Info("Retrieved {Count} friends for user {Username}", friends?.Count ?? 0, username);
                return friends ?? new List<FriendDto>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during getFriendsList for user: {Username}", username);
                return new List<FriendDto>();
            }
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequests(string username)
        {
            if (!IsCurrentUser(username))
            {
                logger.Warn("getFriendRequests called by {Username}, but current session is for {CurrentUsername}. Aborting.", username, currentUsername ?? "N/A");
                return new List<FriendRequestInfoDto>();
            }
            logger.Info("getFriendRequests request for user: {Username}", username);
            try
            {
                var requests = await socialLogic.getFriendRequestsAsync(username);
                logger.Info("Retrieved {Count} friend requests for user {Username}", requests?.Count ?? 0, username);
                return requests ?? new List<FriendRequestInfoDto>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during getFriendRequests for user: {Username}", username);
                return new List<FriendRequestInfoDto>();
            }
        }

        // --- Manejo de Canal y Desconexión ---

        private async void Channel_FaultedOrClosed(object sender, EventArgs e)
        {
            logger.Warn("WCF channel Faulted or Closed for user: {Username}. Initiating cleanup.", currentUsername ?? "N/A");
            // Usar el username asociado a ESTA instancia de servicio
            if (!string.IsNullOrEmpty(currentUsername))
            {
                await cleanupAndNotifyDisconnect(currentUsername);
            }
            // Limpiar handlers del canal que disparó el evento
            cleanupCallbackEvents(sender as ICommunicationObject);
        }

        private async Task cleanupAndNotifyDisconnect(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                logger.Debug("cleanupAndNotifyDisconnect called with null or empty username. Skipping.");
                return;
            }

            logger.Info("Attempting to remove user {Username} from ConnectedUsers and notify friends.", username);
            // Intentar quitar del diccionario estático
            if (ConnectedUsers.TryRemove(username, out ISocialCallback removedChannel))
            {
                logger.Info("User {Username} removed from ConnectedUsers.", username);
                // Limpiar handlers del canal que se quitó (puede ser diferente al de la instancia actual si hubo reemplazo)
                cleanupCallbackEvents(removedChannel as ICommunicationObject);
                // Notificar a los amigos que el usuario se desconectó
                await notifyFriendsStatusChange(username, false);
            }
            else
            {
                logger.Warn("User {Username} was not found in ConnectedUsers during cleanup attempt.", username);
            }

            // Limpiar estado de la instancia actual si corresponde a este usuario
            if (currentUsername == username)
            {
                currentUsername = null;
                currentUserCallback = null;
                logger.Debug("Cleared username and callback reference for the current service instance.");
            }
        }

        private async Task notifyFriendsStatusChange(string changedUsername, bool isOnline)
        {
            if (string.IsNullOrWhiteSpace(changedUsername)) return;

            logger.Debug("Notifying friends of status change for {Username}. New status: {Status}", changedUsername, isOnline ? "Online" : "Offline");
            try
            {
                // Obtener amigos (SIN pasarle ConnectedUsers.Keys, para obtener TODOS los amigos)
                List<FriendDto> friendsToNotify = await socialLogic.getFriendsListAsync(changedUsername, null);
                logger.Debug("Found {Count} friends to potentially notify for user {Username}.", friendsToNotify?.Count ?? 0, changedUsername);

                if (friendsToNotify == null) return;

                foreach (var friend in friendsToNotify)
                {
                    // Notificar solo si el amigo está actualmente conectado
                    if (ConnectedUsers.ContainsKey(friend.username))
                    {
                        logger.Debug("Sending status change notification ({Username} is {Status}) to friend: {FriendUsername}", changedUsername, isOnline ? "Online" : "Offline", friend.username);
                        sendNotificationToUser(friend.username, cb => cb.notifyFriendStatusChanged(changedUsername, isOnline));
                    }
                    else
                    {
                        logger.Debug("Friend {FriendUsername} is offline, skipping status change notification for {Username}.", friend.username, changedUsername);
                    }
                }
            }
            catch (Exception ex)
            {
                // Usar logger en lugar de Console.WriteLine
                logger.Error(ex, "[Service Error] - Exception in NotifyFriendsStatusChange for {Username}", changedUsername);
            }
        }

        // Método estático para enviar notificaciones (sin cambios, pero añadimos logging)
        public static void sendNotificationToUser(string targetUsername, Action<ISocialCallback> action)
        {
            if (string.IsNullOrWhiteSpace(targetUsername))
            {
                logger.Warn("sendNotificationToUser called with null or empty targetUsername.");
                return;
            }

            if (ConnectedUsers.TryGetValue(targetUsername, out ISocialCallback callbackChannel))
            {
                try
                {
                    var commObject = callbackChannel as ICommunicationObject;
                    if (commObject != null && commObject.State == CommunicationState.Opened)
                    {
                        logger.Debug("Sending notification callback to user: {TargetUsername}", targetUsername);
                        action(callbackChannel);
                    }
                    else
                    {
                        logger.Warn("Callback channel for user {TargetUsername} is not open (State: {State}). Skipping notification.", targetUsername, commObject?.State);
                        // Considerar remover el usuario del diccionario si el canal no está abierto
                        // ConnectedUsers.TryRemove(targetUsername, out _); // Podría causar problemas si se está reconectando
                    }
                }
                catch (Exception ex)
                {
                    // Loguear la excepción
                    logger.Error(ex, "Exception sending notification callback to user: {TargetUsername}", targetUsername);
                    // Considerar remover al usuario si el canal falla repetidamente
                }
            }
            else
            {
                logger.Debug("User {TargetUsername} not found in ConnectedUsers. Skipping notification.", targetUsername);
            }
        }

        // --- Helpers para Manejo de Eventos y Sesión ---

        private void setupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                // Remover primero para evitar duplicados
                commObject.Faulted -= Channel_FaultedOrClosed;
                commObject.Closed -= Channel_FaultedOrClosed;
                // Añadir handlers
                commObject.Faulted += Channel_FaultedOrClosed;
                commObject.Closed += Channel_FaultedOrClosed;
                logger.Debug("Event handlers (Faulted/Closed) attached for user: {Username} callback. Channel State: {State}", currentUsername ?? "N/A", commObject.State);
            }
            else
            {
                logger.Warn("Attempted to setup callback events, but communication object was null for user: {Username}.", currentUsername ?? "N/A");
            }
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= Channel_FaultedOrClosed;
                commObject.Closed -= Channel_FaultedOrClosed;
                logger.Debug("Event handlers (Faulted/Closed) removed for a callback channel.");
            }
        }

        // Helper para verificar si la llamada corresponde al usuario de la sesión actual
        private bool IsCurrentUser(string username)
        {
            return !string.IsNullOrEmpty(currentUsername) && currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase);
        }
    }
}