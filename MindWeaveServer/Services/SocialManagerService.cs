using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Abstractions;
using Autofac;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class SocialManagerService : ISocialManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly SocialLogic socialLogic;
        private readonly IGameStateManager gameStateManager;
        private readonly IServiceExceptionHandler exceptionHandler;

        private ISocialCallback currentUserCallback;
        private string currentUsername;

        public SocialManagerService() : this(
            Bootstrapper.Container.Resolve<SocialLogic>(),
            Bootstrapper.Container.Resolve<IGameStateManager>(),
            Bootstrapper.Container.Resolve<IServiceExceptionHandler>())
        {
        }

        public SocialManagerService(
            SocialLogic socialLogic,
            IGameStateManager gameStateManager,
            IServiceExceptionHandler exceptionHandler)
        {
            this.socialLogic = socialLogic;
            this.gameStateManager = gameStateManager;
            this.exceptionHandler = exceptionHandler;

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
                string safeUsername = username ?? "Unknown";

                Task.Run(async () =>
                {
                    try
                    {
                        var existingCallback = gameStateManager.getUserCallback(currentUsername);
                        gameStateManager.addConnectedUser(currentUsername, currentUserCallback);
                        await handleConnectionResult(existingCallback, currentUserCallback, safeUsername);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error during async connection initialization for user {UserId}", safeUsername);
                    }
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Critical error in Connect method.");
            }
        }

        public void disconnect(string username)
        {
            Task.Run(async () =>
            {
                try
                {
                    await processDisconnect(username);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error processing disconnect for user {UserId}", username);
                }
            });
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayers(string requesterUsername, string query)
        {
            try
            {
                validateSession(requesterUsername);
                logger.Info("Player search requested by {UserId}", requesterUsername);

                var results = await socialLogic.searchPlayersAsync(requesterUsername, query);
                return results ?? new List<PlayerSearchResultDto>();
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, $"SearchPlayers - Requester: {requesterUsername}");
            }
        }

        public async Task<OperationResultDto> sendFriendRequest(string requesterUsername, string targetUsername)
        {
            try
            {
                validateSession(requesterUsername);
                logger.Info("Friend request initiated from {RequesterId} to {TargetId}", requesterUsername, targetUsername);

                var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);

                if (result.Success)
                {
                    logger.Info("Friend request successful from {RequesterId} to {TargetId}", requesterUsername, targetUsername);
                    sendNotificationToUser(targetUsername, cb => cb.notifyFriendRequest(requesterUsername));
                }
                else
                {
                    logger.Warn("Friend request failed. Reason: {Reason}", result.Message);
                }

                return result;
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, $"SendFriendRequest - From: {requesterUsername} To: {targetUsername}");
            }
        }

        public async Task<OperationResultDto> respondToFriendRequest(string responderUsername, string requesterUsername, bool accepted)
        {
            try
            {
                validateSession(responderUsername);
                string action = accepted ? "Accepted" : "Declined";
                logger.Info("Friend request response: {Action} by {ResponderId} for {RequesterId}", action, responderUsername, requesterUsername);

                var result = await socialLogic.respondToFriendRequestAsync(responderUsername, requesterUsername, accepted);

                if (result.Success)
                {
                    await handleFriendResponseSuccess(responderUsername, requesterUsername, accepted);
                }
                else
                {
                    logger.Warn("Friend response failed. Reason: {Reason}", result.Message);
                }

                return result;
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, $"RespondToFriendRequest - Responder: {responderUsername}");
            }
        }

        public async Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername)
        {
            try
            {
                validateSession(username);
                logger.Info("Remove friend requested by {UserId} for target {FriendId}", username, friendToRemoveUsername);

                var result = await socialLogic.removeFriendAsync(username, friendToRemoveUsername);

                if (result.Success)
                {
                    logger.Info("Friend removed successfully: {UserId} removed {FriendId}", username, friendToRemoveUsername);
                    sendNotificationToUser(friendToRemoveUsername, cb => cb.notifyFriendStatusChanged(username, false));
                }
                else
                {
                    logger.Warn("Remove friend failed. Reason: {Reason}", result.Message);
                }

                return result;
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, $"RemoveFriend - User: {username}");
            }
        }

        public async Task<List<FriendDto>> getFriendsList(string username)
        {
            try
            {
                validateSession(username);
                logger.Info("GetFriendsList requested for {UserId}", username);
                var connectedUsersList = gameStateManager.ConnectedUsers.Keys.ToList();
                var friends = await socialLogic.getFriendsListAsync(username, connectedUsersList);

                return friends ?? new List<FriendDto>();
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, $"GetFriendsList - User: {username}");
            }
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequests(string username)
        {
            try
            {
                validateSession(username);
                logger.Info("GetFriendRequests requested for {UserId}", username);

                var requests = await socialLogic.getFriendRequestsAsync(username);
                return requests ?? new List<FriendRequestInfoDto>();
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, $"GetFriendRequests - User: {username}");
            }
        }

        private void subscribeToChannelEvents()
        {
            if (OperationContext.Current?.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += channelFaultedOrClosed;
                OperationContext.Current.Channel.Closed += channelFaultedOrClosed;
            }
        }

        private async void channelFaultedOrClosed(object sender, EventArgs e)
        {
            logger.Warn("WCF channel Faulted or Closed for user session: {UserId}. Initiating cleanup.", currentUsername ?? "Unknown");

            if (!string.IsNullOrEmpty(currentUsername))
            {
                await cleanupAndNotifyDisconnect(currentUsername);
            }

            cleanupCallbackEvents(sender as ICommunicationObject);
        }

        private ISocialCallback tryGetCallbackChannel(string username)
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
            catch (Exception ex)
            {
                logger.Error(ex, "Connect failed: Exception retrieving callback channel for user {UserId}", username);
                return null;
            }
        }

        private async Task processDisconnect(string username)
        {
            string safeUser = username ?? "NULL";
            if (!string.IsNullOrEmpty(currentUsername) &&
                currentUsername.Equals(safeUser, StringComparison.OrdinalIgnoreCase))
            {
                logger.Info("Disconnect requested by user: {UserId}", safeUser);
                await cleanupAndNotifyDisconnect(currentUsername);
            }
            else
            {
                logger.Warn("Disconnect request mismatch. Request: {ReqId}, Session: {SessId}", safeUser, currentUsername ?? "NULL");
            }
        }

        private async Task handleConnectionResult(ISocialCallback previousCallback, ISocialCallback newCallback, string username)
        {
            if (previousCallback == null || previousCallback == newCallback)
            {
                logger.Info("User connected: {UserId}", username);
                setupCallbackEvents(newCallback as ICommunicationObject);
                await notifyFriendsStatusChange(currentUsername, true);
            }
            else
            {
                logger.Warn("User {UserId} replaced an existing active session.", username);
                if (previousCallback is ICommunicationObject oldComm)
                {
                    cleanupCallbackEvents(oldComm);
                }
                setupCallbackEvents(newCallback as ICommunicationObject);
            }
        }

        private async Task cleanupAndNotifyDisconnect(string username)
        {
            if (string.IsNullOrEmpty(username)) return;

            if (gameStateManager.isUserConnected(username))
            {
                var callbackToRemove = gameStateManager.getUserCallback(username);
                gameStateManager.removeConnectedUser(username);

                logger.Info("User {UserId} removed from active sessions.", username);

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
                await notifyFriendsStatusChange(requester, isRequesterConnected);
            }
        }

        private async Task notifyFriendsStatusChange(string changedUsername, bool isOnline)
        {
            if (string.IsNullOrWhiteSpace(changedUsername)) return;

            try
            {
                List<FriendDto> friendsToNotify = await socialLogic.getFriendsListAsync(changedUsername, null);

                if (friendsToNotify == null || !friendsToNotify.Any()) return;

                var onlineFriends = friendsToNotify
                    .Where(f => gameStateManager.isUserConnected(f.Username))
                    .ToList();

                logger.Debug("Notifying {Count} friends of status change for {UserId}", onlineFriends.Count, changedUsername);

                foreach (var friend in onlineFriends)
                {
                    sendNotificationToUser(friend.Username, cb => cb.notifyFriendStatusChanged(changedUsername, isOnline));
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to notify status change for user {UserId}", changedUsername);
            }
        }

        private void sendNotificationToUser(string targetUsername, Action<ISocialCallback> action)
        {
            if (string.IsNullOrWhiteSpace(targetUsername)) return;

            try
            {
                var callback = gameStateManager.getUserCallback(targetUsername);
                if (callback == null) return;

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
            catch (Exception ex)
            {
                logger.Error(ex, "Notification failed for target {UserId}", targetUsername);
            }
        }

        private void setupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= channelFaultedOrClosed;
                commObject.Closed -= channelFaultedOrClosed;
                commObject.Faulted += channelFaultedOrClosed;
                commObject.Closed += channelFaultedOrClosed;
            }
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= channelFaultedOrClosed;
                commObject.Closed -= channelFaultedOrClosed;
            }
        }

        private void validateSession(string username)
        {
            if (string.IsNullOrEmpty(currentUsername) ||
                !currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("Session mismatch. Expected: {SessionId}, Actual: {RequestId}", currentUsername ?? "NULL", username);
                throw new FaultException<ServiceFaultDto>(
                    new ServiceFaultDto(ServiceErrorType.SecurityError, Lang.ErrorSessionMismatch, "Session"),
                    new FaultReason("Session Mismatch"));
            }
        }
    }
}