using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.Utilities.Abstractions;
using NLog;
using System;
using System.Collections.Generic;
using System.Data.Entity.Core;
using System.Data.SqlClient;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(
        InstanceContextMode = InstanceContextMode.PerSession,
        ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class SocialManagerService : ISocialManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly SocialLogic socialLogic;
        private readonly IGameStateManager gameStateManager;
        private readonly IServiceExceptionHandler exceptionHandler;
        private readonly IDisconnectionHandler disconnectionHandler;

        private ISocialCallback currentUserCallback;
        private string currentUsername;

        private volatile bool isDisconnecting;
        private readonly object disconnectLock = new object();

        private const string DISCONNECT_REASON_SESSION_CLOSED = "SessionClosed";
        private const string DISCONNECT_REASON_SESSION_FAULTED = "SessionFaulted";

        public SocialManagerService() : this(
            Bootstrapper.Container.Resolve<SocialLogic>(),
            Bootstrapper.Container.Resolve<IGameStateManager>(),
            Bootstrapper.Container.Resolve<IServiceExceptionHandler>(),
            Bootstrapper.Container.Resolve<IDisconnectionHandler>())
        {
        }

        public SocialManagerService(
            SocialLogic socialLogic,
            IGameStateManager gameStateManager,
            IServiceExceptionHandler exceptionHandler,
            IDisconnectionHandler disconnectionHandler)
        {
            this.socialLogic = socialLogic;
            this.gameStateManager = gameStateManager;
            this.exceptionHandler = exceptionHandler;
            this.disconnectionHandler = disconnectionHandler;

            subscribeToChannelEvents();
        }

        public void connect(string username)
        {
            try
            {
                ISocialCallback callbackChannel = tryGetCallbackChannel(username);
                if (callbackChannel == null)
                {
                    return;
                }

                currentUserCallback = callbackChannel;
                currentUsername = username;

                Task.Run(async () =>
                {
                    var existingCallback = gameStateManager.getUserCallback(currentUsername);
                    gameStateManager.addConnectedUser(currentUsername, currentUserCallback);
                    await handleConnectionResult(existingCallback, currentUserCallback);
                });

                logger.Info("SocialManagerService: User {0} connected via Reliable Session.", username);
            }
            catch (InvalidOperationException opEx)
            {
                logger.Error(opEx, "Invalid WCF operation context during Connect for {User}", username);
            }
            catch (ArgumentException argEx)
            {
                logger.Error(argEx, "Invalid argument provided during Connect for {User}", username);
            }
        }

        public void disconnect(string username)
        {
            Task.Run(async () =>
            {
                await processDisconnect(username);
            });
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayers(string requesterUsername, string query)
        {
            try
            {
                logger.Info("Player search requested by {UserId}", requesterUsername);

                validateSession(requesterUsername);
                return await socialLogic.searchPlayersAsync(requesterUsername, query);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "SearchPlayersOperation");
            }
        }

        public async Task<OperationResultDto> sendFriendRequest(string requesterUsername, string targetUsername)
        {
            try
            {
                logger.Info("Friend request initiated from {RequesterId} to {TargetId}", requesterUsername, targetUsername);

                validateSession(requesterUsername);

                var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);

                if (result.Success)
                {
                    if (result.MessageCode == MessageCodes.SOCIAL_FRIEND_REQUEST_ACCEPTED)
                    {
                        sendNotificationToUser(targetUsername, cb => cb.notifyFriendResponse(requesterUsername, true));
                        sendNotificationToUser(targetUsername, cb => cb.notifyFriendStatusChanged(requesterUsername, true));
                    }
                    else
                    {
                        sendNotificationToUser(targetUsername, cb => cb.notifyFriendRequest(requesterUsername));
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "SendFriendRequestOperation");
            }
        }

        public async Task<OperationResultDto> respondToFriendRequest(string responderUsername, string requesterUsername, bool accepted)
        {
            try
            {
                validateSession(responderUsername);

                var result = await socialLogic.respondToFriendRequestAsync(responderUsername, requesterUsername, accepted);

                if (result.Success)
                {
                    await handleFriendResponseSuccess(responderUsername, requesterUsername, accepted);
                }

                return result;
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "RespondToFriendRequestOperation");
            }
        }

        public async Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername)
        {
            try
            {
                logger.Info("Remove friend requested by {UserId} for target {FriendId}", username, friendToRemoveUsername);

                validateSession(username);

                var result = await socialLogic.removeFriendAsync(username, friendToRemoveUsername);

                if (result.Success)
                {
                    sendNotificationToUser(friendToRemoveUsername, cb => cb.notifyFriendStatusChanged(username, false));
                }

                return result;
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "RemoveFriendOperation");
            }
        }

        public async Task<List<FriendDto>> getFriendsList(string username)
        {
            try
            {
                logger.Info("GetFriendsList requested for {UserId}", username);

                validateSession(username);
                var connectedUsersList = gameStateManager.ConnectedUsers.Keys.ToList();
                return await socialLogic.getFriendsListAsync(username, connectedUsersList);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "GetFriendsListOperation");
            }
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequests(string username)
        {
            try
            {
                validateSession(username);
                logger.Info("GetFriendRequests requested for {UserId}", username);

                return await socialLogic.getFriendRequestsAsync(username);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "GetFriendRequestsOperation");
            }
        }

        #region Channel Event Handlers (Reliable Session Detection)

        private void subscribeToChannelEvents()
        {
            if (OperationContext.Current?.Channel == null)
            {
                logger.Warn("SocialManagerService: Cannot subscribe to channel events - OperationContext or Channel is null.");
                return;
            }

            OperationContext.Current.Channel.Faulted += onChannelFaulted;
            OperationContext.Current.Channel.Closed += onChannelClosed;

            logger.Debug("SocialManagerService: Subscribed to channel Faulted/Closed events.");
        }

        private void onChannelFaulted(object sender, EventArgs e)
        {
            logger.Warn("SocialManagerService: Channel FAULTED for user {0}. Initiating disconnection.",
                currentUsername ?? "Unknown");

            initiateDisconnectionAsync(DISCONNECT_REASON_SESSION_FAULTED);
        }

        private void onChannelClosed(object sender, EventArgs e)
        {
            logger.Info("SocialManagerService: Channel CLOSED for user {0}. Initiating disconnection.",
                currentUsername ?? "Unknown");

            initiateDisconnectionAsync(DISCONNECT_REASON_SESSION_CLOSED);
        }

        private void initiateDisconnectionAsync(string reason)
        {
            string usernameToDisconnect;

            lock (disconnectLock)
            {
                if (isDisconnecting)
                {
                    logger.Debug("SocialManagerService: Disconnection already in progress for {0}.", currentUsername);
                    return;
                }

                isDisconnecting = true;
                usernameToDisconnect = currentUsername;
            }

            if (string.IsNullOrWhiteSpace(usernameToDisconnect))
            {
                logger.Warn("SocialManagerService: Cannot disconnect - username is null/empty.");
                return;
            }

            // Ejecutar la desconexión completa en un hilo separado
            Task.Run(async () =>
            {
                try
                {
                    logger.Info("SocialManagerService: Executing full disconnection for {0}. Reason: {1}",
                        usernameToDisconnect, reason);

                    // Llamar al DisconnectionHandler centralizado
                    await disconnectionHandler.handleFullDisconnectionAsync(usernameToDisconnect, reason);

                    logger.Info("SocialManagerService: Full disconnection completed for {0}.", usernameToDisconnect);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "SocialManagerService: Error during full disconnection for {0}.", usernameToDisconnect);
                }
                finally
                {
                    cleanupLocalState();
                }
            });
        }

        private void cleanupLocalState()
        {
            cleanupCallbackEvents(OperationContext.Current?.Channel);
            cleanupCallbackEvents(currentUserCallback as ICommunicationObject);

            currentUsername = null;
            currentUserCallback = null;
        }

        #endregion

        #region Private Helper Methods

        private static ISocialCallback tryGetCallbackChannel(string username)
        {
            if (string.IsNullOrWhiteSpace(username) || OperationContext.Current == null)
            {
                logger.Warn("Connect failed: Invalid context or empty username.");
                return null;
            }

            try
            {
                var channel = OperationContext.Current.GetCallbackChannel<ISocialCallback>();
                if (channel == null)
                {
                    logger.Error("Connect failed: Callback channel is null for user {UserId}", username);
                }
                return channel;
            }
            catch (InvalidCastException castEx)
            {
                logger.Error(castEx, "Channel casting failed for user {User}. Interface mismatch.", username);
                return null;
            }
            catch (InvalidOperationException opEx)
            {
                logger.Error(opEx, "Invalid operation retrieving callback channel for {User}.", username);
                return null;
            }
        }

        private async Task processDisconnect(string username)
        {
            if (!string.IsNullOrEmpty(currentUsername) &&
                currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
            {
                await cleanupAndNotifyDisconnect(currentUsername);
            }
        }

        private async Task handleConnectionResult(ISocialCallback previousCallback, ISocialCallback newCallback)
        {
            if (previousCallback != null && previousCallback != newCallback && previousCallback is ICommunicationObject oldComm)
            {
                cleanupCallbackEvents(oldComm);
            }

            setupCallbackEvents(newCallback as ICommunicationObject);
            await notifyFriendsStatusChange(currentUsername, true);
        }

        private async Task cleanupAndNotifyDisconnect(string username)
        {
            if (string.IsNullOrEmpty(username)) return;

            if (gameStateManager.isUserConnected(username))
            {
                var callbackToRemove = gameStateManager.getUserCallback(username);
                gameStateManager.removeConnectedUser(username);

                if (callbackToRemove is ICommunicationObject comm)
                {
                    cleanupCallbackEvents(comm);
                }

                await notifyFriendsStatusChange(username, false);
            }

            if (currentUsername == username)
            {
                currentUsername = null;
                currentUserCallback = null;
            }
        }

        private async Task handleFriendResponseSuccess(string responder, string requester, bool accepted)
        {
            sendNotificationToUser(requester, cb => cb.notifyFriendResponse(responder, accepted));

            if (accepted)
            {
                await notifyFriendsStatusChange(responder, true);
                bool isRequesterConnected = gameStateManager.isUserConnected(requester);

                if (isRequesterConnected)
                {
                    sendNotificationToUser(responder, cb => cb.notifyFriendStatusChanged(requester, true));
                }
            }
        }

        private async Task notifyFriendsStatusChange(string changedUsername, bool isOnline)
        {
            if (string.IsNullOrWhiteSpace(changedUsername)) return;

            try
            {
                List<FriendDto> friendsToNotify = await socialLogic.getFriendsListAsync(changedUsername, null);

                if (friendsToNotify == null || !friendsToNotify.Any()) return;

                foreach (var friend in friendsToNotify.Where(f => gameStateManager.isUserConnected(f.Username)))
                {
                    sendNotificationToUser(friend.Username, cb => cb.notifyFriendStatusChanged(changedUsername, isOnline));
                }
            }
            catch (EntityException dbEx)
            {
                logger.Error(dbEx, "Database error retrieving friend list for status notification of {User}", changedUsername);
            }
            catch (SqlException sqlEx)
            {
                logger.Error(sqlEx, "SQL error retrieving friend list for status notification of {User}", changedUsername);
            }
            catch (TimeoutException timeEx)
            {
                logger.Warn(timeEx, "Timeout retrieving friend list or notifying friends for {User}", changedUsername);
            }
        }

        private void sendNotificationToUser(string targetUsername, Action<ISocialCallback> action)
        {
            if (string.IsNullOrWhiteSpace(targetUsername)) return;

            var callback = gameStateManager.getUserCallback(targetUsername);
            if (callback == null) return;

            try
            {
                if (callback is ICommunicationObject commObject && commObject.State == CommunicationState.Opened)
                {
                    action(callback);
                }
                else
                {
                    logger.Warn("Callback channel closed for {UserId}. Removing from session.", targetUsername);
                    gameStateManager.removeConnectedUser(targetUsername);
                }
            }
            catch (CommunicationException)
            {
                gameStateManager.removeConnectedUser(targetUsername);
            }
            catch (TimeoutException)
            {
                gameStateManager.removeConnectedUser(targetUsername);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Unexpected error sending notification to {User}", targetUsername);
            }
        }

        private void setupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= onChannelFaulted;
                commObject.Closed -= onChannelClosed;
                commObject.Faulted += onChannelFaulted;
                commObject.Closed += onChannelClosed;
            }
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= onChannelFaulted;
                commObject.Closed -= onChannelClosed;
            }
        }

        private void validateSession(string username)
        {
            if (string.IsNullOrEmpty(currentUsername) ||
                !currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("Session security check failed. Expected: {Expected}, Got: {Actual}", currentUsername, username);
                throw new FaultException<ServiceFaultDto>(
                    new ServiceFaultDto(ServiceErrorType.SecurityError, MessageCodes.ERROR_SESSION_MISMATCH, "Session"),
                    new FaultReason(MessageCodes.ERROR_SESSION_MISMATCH));
            }
        }

        #endregion
    }
}