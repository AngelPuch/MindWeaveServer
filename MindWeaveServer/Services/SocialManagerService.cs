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
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
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

        private const string OPERATION_SEARCH_PLAYERS = "SearchPlayersOperation";
        private const string OPERATION_SEND_FRIEND_REQUEST = "SendFriendRequestOperation";
        private const string OPERATION_RESPOND_FRIEND_REQUEST = "RespondToFriendRequestOperation";
        private const string OPERATION_REMOVE_FRIEND = "RemoveFriendOperation";
        private const string OPERATION_GET_FRIENDS_LIST = "GetFriendsListOperation";
        private const string OPERATION_GET_FRIEND_REQUESTS = "GetFriendRequestsOperation";

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
                processConnect(username);
            }
            catch (InvalidOperationException opEx)
            {
                logger.Error(opEx, "Invalid WCF operation context during Connect.");
            }
            catch (ArgumentException argEx)
            {
                logger.Error(argEx, "Invalid argument provided during Connect.");
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
                logger.Info("Player search requested.");
                validateSession(requesterUsername);
                return await socialLogic.searchPlayersAsync(requesterUsername, query);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_SEARCH_PLAYERS);
            }
        }

        public async Task<OperationResultDto> sendFriendRequest(string requesterUsername, string targetUsername)
        {
            try
            {
                logger.Info("Friend request initiated.");
                validateSession(requesterUsername);

                var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);

                if (result.Success)
                {
                    notifyFriendRequestResult(requesterUsername, targetUsername, result.MessageCode);
                }

                return result;
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_SEND_FRIEND_REQUEST);
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
                    await notifyFriendResponseResult(responderUsername, requesterUsername, accepted);
                }

                return result;
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_RESPOND_FRIEND_REQUEST);
            }
        }

        public async Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername)
        {
            try
            {
                logger.Info("Remove friend requested.");
                validateSession(username);

                var result = await socialLogic.removeFriendAsync(username, friendToRemoveUsername);

                if (result.Success)
                {
                    notifyFriendRemoved(username, friendToRemoveUsername);
                }

                return result;
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_REMOVE_FRIEND);
            }
        }

        public async Task<List<FriendDto>> getFriendsList(string username)
        {
            try
            {
                logger.Info("GetFriendsList requested.");
                validateSession(username);

                var connectedUsersList = gameStateManager.ConnectedUsers.Keys.ToList();
                return await socialLogic.getFriendsListAsync(username, connectedUsersList);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_GET_FRIENDS_LIST);
            }
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequests(string username)
        {
            try
            {
                validateSession(username);
                logger.Info("GetFriendRequests requested.");

                return await socialLogic.getFriendRequestsAsync(username);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_GET_FRIEND_REQUESTS);
            }
        }

        private void processConnect(string username)
        {
            ISocialCallback callbackChannel = tryGetCallbackChannel();
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

            logger.Info("SocialManagerService: User connected via Reliable Session.");
        }

        private static ISocialCallback tryGetCallbackChannel()
        {
            if (OperationContext.Current == null)
            {
                logger.Warn("Connect failed: Invalid context.");
                return null;
            }

            try
            {
                var channel = OperationContext.Current.GetCallbackChannel<ISocialCallback>();
                if (channel == null)
                {
                    logger.Error("Connect failed: Callback channel is null.");
                }
                return channel;
            }
            catch (InvalidCastException castEx)
            {
                logger.Error(castEx, "Channel casting failed. Interface mismatch.");
                return null;
            }
            catch (InvalidOperationException opEx)
            {
                logger.Error(opEx, "Invalid operation retrieving callback channel.");
                return null;
            }
        }

        private void notifyFriendRequestResult(string requesterUsername, string targetUsername, string messageCode)
        {
            if (messageCode == MessageCodes.SOCIAL_FRIEND_REQUEST_ACCEPTED)
            {
                notifyFriendRequestAccepted(requesterUsername, targetUsername);
            }
            else
            {
                sendNotificationToUser(targetUsername, cb => cb.notifyFriendRequest(requesterUsername));
            }
        }

        private void notifyFriendRequestAccepted(string requesterUsername, string targetUsername)
        {
            sendNotificationToUser(targetUsername, cb => cb.notifyFriendResponse(requesterUsername, true));
            sendNotificationToUser(targetUsername, cb => cb.notifyFriendStatusChanged(requesterUsername, true));
        }

        private async Task notifyFriendResponseResult(string responder, string requester, bool accepted)
        {
            sendNotificationToUser(requester, cb => cb.notifyFriendResponse(responder, accepted));

            if (accepted)
            {
                await notifyAcceptedFriendResponse(responder, requester);
            }
        }

        private async Task notifyAcceptedFriendResponse(string responder, string requester)
        {
            await notifyFriendsStatusChange(responder, true);

            if (gameStateManager.isUserConnected(requester))
            {
                sendNotificationToUser(responder, cb => cb.notifyFriendStatusChanged(requester, true));
            }
        }

        private void notifyFriendRemoved(string username, string friendToRemoveUsername)
        {
            sendNotificationToUser(friendToRemoveUsername, cb => cb.notifyFriendStatusChanged(username, false));
        }

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
            logger.Warn("SocialManagerService: Channel FAULTED. Initiating disconnection.");
            initiateDisconnectionAsync(DISCONNECT_REASON_SESSION_FAULTED);
        }

        private void onChannelClosed(object sender, EventArgs e)
        {
            logger.Info("SocialManagerService: Channel CLOSED. Initiating disconnection.");
            initiateDisconnectionAsync(DISCONNECT_REASON_SESSION_CLOSED);
        }

        private void initiateDisconnectionAsync(string reason)
        {
            string usernameToDisconnect = tryBeginDisconnection();
            if (usernameToDisconnect == null)
            {
                return;
            }

            executeDisconnectionAsync(usernameToDisconnect, reason);
        }

        private string tryBeginDisconnection()
        {
            lock (disconnectLock)
            {
                if (isDisconnecting)
                {
                    logger.Debug("SocialManagerService: Disconnection already in progress.");
                    return null;
                }

                isDisconnecting = true;
            }

            if (string.IsNullOrWhiteSpace(currentUsername))
            {
                logger.Warn("SocialManagerService: Cannot disconnect - username is null/empty.");
                return null;
            }

            return currentUsername;
        }

        private void executeDisconnectionAsync(string usernameToDisconnect, string reason)
        {
            Task.Run(async () =>
            {
                try
                {
                    logger.Info("SocialManagerService: Executing full disconnection. Reason: {Reason}", reason);
                    await disconnectionHandler.handleFullDisconnectionAsync(usernameToDisconnect, reason);
                    logger.Info("SocialManagerService: Full disconnection completed.");
                }
                catch (InvalidOperationException opEx)
                {
                    logger.Error(opEx, "SocialManagerService: Invalid operation during disconnection.");
                }
                catch (CommunicationException commEx)
                {
                    logger.Error(commEx, "SocialManagerService: Communication error during disconnection.");
                }
                catch (TimeoutException timeoutEx)
                {
                    logger.Error(timeoutEx, "SocialManagerService: Timeout during disconnection.");
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

        private async Task processDisconnect(string username)
        {
            if (isCurrentUser(username))
            {
                await cleanupAndNotifyDisconnect(currentUsername);
            }
        }

        private bool isCurrentUser(string username)
        {
            return !string.IsNullOrEmpty(currentUsername) &&
                   currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase);
        }

        private async Task handleConnectionResult(ISocialCallback previousCallback, ISocialCallback newCallback)
        {
            if (shouldCleanupPreviousCallback(previousCallback, newCallback))
            {
                cleanupCallbackEvents(previousCallback as ICommunicationObject);
            }

            setupCallbackEvents(newCallback as ICommunicationObject);
            await notifyFriendsStatusChange(currentUsername, true);
        }

        private static bool shouldCleanupPreviousCallback(ISocialCallback previousCallback, ISocialCallback newCallback)
        {
            return previousCallback != null && previousCallback != newCallback;
        }

        private async Task cleanupAndNotifyDisconnect(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                return;
            }

            if (gameStateManager.isUserConnected(username))
            {
                await removeUserAndNotify(username);
            }

            clearCurrentUserIfMatch(username);
        }

        private async Task removeUserAndNotify(string username)
        {
            var callbackToRemove = gameStateManager.getUserCallback(username);
            gameStateManager.removeConnectedUser(username);

            if (callbackToRemove is ICommunicationObject comm)
            {
                cleanupCallbackEvents(comm);
            }

            await notifyFriendsStatusChange(username, false);
        }

        private void clearCurrentUserIfMatch(string username)
        {
            if (currentUsername == username)
            {
                currentUsername = null;
                currentUserCallback = null;
            }
        }

        private async Task notifyFriendsStatusChange(string changedUsername, bool isOnline)
        {
            if (string.IsNullOrWhiteSpace(changedUsername))
            {
                return;
            }

            try
            {
                await notifyConnectedFriends(changedUsername, isOnline);
            }
            catch (EntityException dbEx)
            {
                logger.Error(dbEx, "Database error retrieving friend list for status notification.");
            }
            catch (SqlException sqlEx)
            {
                logger.Error(sqlEx, "SQL error retrieving friend list for status notification.");
            }
            catch (TimeoutException timeEx)
            {
                logger.Warn(timeEx, "Timeout retrieving friend list or notifying friends.");
            }
        }

        private async Task notifyConnectedFriends(string changedUsername, bool isOnline)
        {
            List<FriendDto> friendsToNotify = await socialLogic.getFriendsListAsync(changedUsername, null);

            if (friendsToNotify == null || !friendsToNotify.Any())
            {
                return;
            }

            foreach (var friend in friendsToNotify.Where(f => gameStateManager.isUserConnected(f.Username)))
            {
                sendNotificationToUser(friend.Username, cb => cb.notifyFriendStatusChanged(changedUsername, isOnline));
            }
        }

        private void sendNotificationToUser(string targetUsername, Action<ISocialCallback> action)
        {
            if (string.IsNullOrWhiteSpace(targetUsername))
            {
                return;
            }

            var callback = gameStateManager.getUserCallback(targetUsername);
            if (callback == null)
            {
                return;
            }

            executeNotification(targetUsername, callback, action);
        }

        private void executeNotification(string targetUsername, ISocialCallback callback, Action<ISocialCallback> action)
        {
            try
            {
                if (isCallbackOpen(callback))
                {
                    action(callback);
                }
                else
                {
                    handleClosedCallback(targetUsername);
                }
            }
            catch (CommunicationException commEx)
            {
                logger.Debug(commEx, "Communication error sending notification. Removing user from session.");
                gameStateManager.removeConnectedUser(targetUsername);
            }
            catch (TimeoutException timeoutEx)
            {
                logger.Debug(timeoutEx, "Timeout sending notification. Removing user from session.");
                gameStateManager.removeConnectedUser(targetUsername);
            }
            catch (ObjectDisposedException disposedEx)
            {
                logger.Debug(disposedEx, "Channel disposed while sending notification. Removing user from session.");
                gameStateManager.removeConnectedUser(targetUsername);
            }
        }

        private static bool isCallbackOpen(ISocialCallback callback)
        {
            return callback is ICommunicationObject commObject && commObject.State == CommunicationState.Opened;
        }

        private void handleClosedCallback(string targetUsername)
        {
            logger.Warn("Callback channel closed. Removing from session.");
            gameStateManager.removeConnectedUser(targetUsername);
        }

        private void setupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject == null)
            {
                return;
            }

            commObject.Faulted -= onChannelFaulted;
            commObject.Closed -= onChannelClosed;
            commObject.Faulted += onChannelFaulted;
            commObject.Closed += onChannelClosed;
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject == null)
            {
                return;
            }

            commObject.Faulted -= onChannelFaulted;
            commObject.Closed -= onChannelClosed;
        }

        private void validateSession(string username)
        {
            if (isValidSession(username))
            {
                return;
            }

            logger.Warn("Session security check failed.");
            throw new FaultException<ServiceFaultDto>(
                new ServiceFaultDto(ServiceErrorType.SecurityError, MessageCodes.ERROR_SESSION_MISMATCH, "Session"),
                new FaultReason(MessageCodes.ERROR_SESSION_MISMATCH));
        }

        private bool isValidSession(string username)
        {
            return !string.IsNullOrEmpty(currentUsername) &&
                   currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase);
        }
    }
}