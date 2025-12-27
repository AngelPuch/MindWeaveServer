using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.Resources;
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
                    var existingCallback = gameStateManager.getUserCallback(currentUsername); 
                    gameStateManager.addConnectedUser(currentUsername, currentUserCallback); 
                    await handleConnectionResult(existingCallback, currentUserCallback, safeUsername);
                    
                });
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
                return  await socialLogic.searchPlayersAsync(requesterUsername, query);
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
                    sendNotificationToUser(targetUsername, cb => cb.notifyFriendRequest(requesterUsername));
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

        private async Task handleConnectionResult(ISocialCallback previousCallback, ISocialCallback newCallback, string username)
        {
            if (previousCallback != null && previousCallback != newCallback)
            {
                if (previousCallback is ICommunicationObject oldComm)
                {
                    cleanupCallbackEvents(oldComm);
                }
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

                foreach (var friend in friendsToNotify)
                {
                    if (gameStateManager.isUserConnected(friend.Username))
                    {
                        sendNotificationToUser(friend.Username, cb => cb.notifyFriendStatusChanged(changedUsername, isOnline));
                    }
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
                logger.Warn("Session security check failed. Expected: {Expected}, Got: {Actual}", currentUsername, username);
                throw new FaultException<ServiceFaultDto>(
                    new ServiceFaultDto(ServiceErrorType.SecurityError, Lang.ErrorSessionMismatch, "Session"),
                    new FaultReason("Session Mismatch"));
            }
        }
    }
}