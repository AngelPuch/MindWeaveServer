using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Resources;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Entity.Core;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class SocialManagerService : ISocialManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private static readonly ConcurrentDictionary<string, ISocialCallback> connectedUsers =
            new ConcurrentDictionary<string, ISocialCallback>(StringComparer.OrdinalIgnoreCase);

        private readonly SocialLogic socialLogic;
        private string currentUsername;
        private ISocialCallback currentUserCallback;

        public SocialManagerService()
        {
            var dbContext = new MindWeaveDBEntities1();
            var playerRepository = new PlayerRepository(dbContext);
            var friendshipRepository = new FriendshipRepository(dbContext);
            this.socialLogic = new SocialLogic(playerRepository, friendshipRepository);

            logger.Info("SocialManagerService instance created (PerSession). Waiting for connect call.");

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

        public static bool isUserConnected(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                return false;
            }
            return connectedUsers.ContainsKey(username);
        }

        public static ICollection<string> getConnectedUsernames()
        {
            return connectedUsers.Keys.ToList();
        }


        public void connect(string username)
        {
            ISocialCallback callbackChannel = tryGetCallbackChannel(username);
            if (callbackChannel == null)
            {
                return;
            }

            this.currentUserCallback = callbackChannel;
            this.currentUsername = username;
            string userForContext = username ?? "NULL";

            Task.Run(async () =>
            {
                logger.Info("Connect task started for user: {Username}", userForContext);
                ISocialCallback addedOrUpdatedCallback = connectedUsers.AddOrUpdate(
                    currentUsername,
                    currentUserCallback,
                    updateCallbackFactory
                );

                await handleConnectionResult(addedOrUpdatedCallback, userForContext);
            });
        }
        
        public void disconnect(string username)
        {
            Task.Run(async () =>
            {
                string userForContext = username ?? "NULL";
                if (!string.IsNullOrEmpty(currentUsername) &&
                    currentUsername.Equals(userForContext, StringComparison.OrdinalIgnoreCase))
                {
                    logger.Info("Disconnect requested by user: {Username}", userForContext);
                    await cleanupAndNotifyDisconnect(currentUsername);
                }
                else
                {
                    logger.Warn(
                        "Disconnect called with username '{Username}' which does not match the current session user '{CurrentUsername}' or session is already cleaned up.",
                        userForContext, currentUsername ?? "N/A");
                }
            });
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayers(string requesterUsername, string query)
        {
            if (!isCurrentUser(requesterUsername))
            {
                logger.Warn("searchPlayers called by {RequesterUsername}, but current session is for {CurrentUsername}. Aborting.", requesterUsername, currentUsername ?? "N/A");
                return new List<PlayerSearchResultDto>();
            }
            logger.Info("SearchPlayers request from {RequesterUsername} with query: '{Query}'", requesterUsername, query ?? "");
            try
            {
                var results = await socialLogic.searchPlayersAsync(requesterUsername, query);
                logger.Info("SearchPlayers found {Count} results for query '{Query}' by {RequesterUsername}", results?.Count ?? 0, query ?? "", requesterUsername);
                return results ?? new List<PlayerSearchResultDto>();
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.DatabaseError, Lang.ErrorMsgServerOffline, "Database");
                logger.Fatal(ex, "Social Service DB Error in searchPlayers");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.GenericServerError, "Server");
                logger.Fatal(ex, "Social Service Critical Error in searchPlayers");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public async Task<OperationResultDto> sendFriendRequest(string requesterUsername, string targetUsername)
        {
            if (!isCurrentUser(requesterUsername))
            {
                logger.Warn("sendFriendRequest called by {RequesterUsername}, but current session is for {CurrentUsername}. Aborting.", requesterUsername, currentUsername ?? "N/A");
                return new OperationResultDto { Success = false, Message = Lang.ErrorSessionMismatch };
            }
            logger.Info("sendFriendRequest attempt from {RequesterUsername} to {TargetUsername}", requesterUsername, targetUsername ?? "NULL");
            try
            {
                var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);
                if (result.Success)
                {
                    logger.Info("Friend request sent successfully from {RequesterUsername} to {TargetUsername}", requesterUsername, targetUsername);
                    sendNotificationToUser(targetUsername, cb => cb.notifyFriendRequest(requesterUsername));
                }
                else
                {
                    logger.Warn("sendFriendRequest failed from {RequesterUsername} to {TargetUsername}. Reason: {Reason}", requesterUsername, targetUsername, result.Message);
                }
                return result;
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.DatabaseError, Lang.ErrorMsgServerOffline, "Database");
                logger.Fatal(ex, "Social Service DB Error in sendFriendRequest");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.GenericServerError, "Server");
                logger.Fatal(ex, "Social Service Critical Error in sendFriendRequest");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public async Task<OperationResultDto> respondToFriendRequest(string responderUsername, string requesterUsername, bool accepted)
        {
            if (!isCurrentUser(responderUsername))
            {
                logger.Warn("respondToFriendRequest called by {ResponderUsername}, but current session is for {CurrentUsername}. Aborting.", responderUsername, currentUsername ?? "N/A");
                return new OperationResultDto { Success = false, Message = Lang.ErrorSessionMismatch };
            }
            logger.Info("respondToFriendRequest attempt by {ResponderUsername} to request from {RequesterUsername}. Accepted: {Accepted}", responderUsername, requesterUsername ?? "NULL", accepted);
            try
            {
                var result = await socialLogic.respondToFriendRequestAsync(responderUsername, requesterUsername, accepted);
                if (result.Success)
                {
                    logger.Info("Friend request response ({Accepted}) processed successfully by {ResponderUsername} for request from {RequesterUsername}", accepted ? "Accepted" : "Declined", responderUsername, requesterUsername);
                    sendNotificationToUser(requesterUsername, cb => cb.notifyFriendResponse(responderUsername, accepted));
                    if (accepted)
                    {
                        await notifyFriendsStatusChange(responderUsername, true);
                        await notifyFriendsStatusChange(requesterUsername, isUserConnected(requesterUsername));
                    }
                }
                else
                {
                    logger.Warn("respondToFriendRequest failed by {ResponderUsername} for request from {RequesterUsername}. Reason: {Reason}", responderUsername, requesterUsername, result.Message);
                }
                return result;
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.DatabaseError, Lang.ErrorMsgServerOffline, "Database");
                logger.Fatal(ex, "Social Service DB Error in respondToFriendRequest");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.GenericServerError, "Server");
                logger.Fatal(ex, "Social Service Critical Error in respondToFriendRequest");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public async Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername)
        {
            if (!isCurrentUser(username))
            {
                logger.Warn("removeFriend called by {Username}, but current session is for {CurrentUsername}. Aborting.", username, currentUsername ?? "N/A");
                return new OperationResultDto { Success = false, Message = Lang.ErrorSessionMismatch };
            }
            logger.Info("removeFriend attempt by {Username} to remove {FriendToRemoveUsername}", username, friendToRemoveUsername ?? "NULL");
            try
            {
                var result = await socialLogic.removeFriendAsync(username, friendToRemoveUsername);
                if (result.Success)
                {
                    logger.Info("Friend removed successfully: {Username} removed {FriendToRemoveUsername}", username, friendToRemoveUsername);
                    sendNotificationToUser(friendToRemoveUsername, cb => cb.notifyFriendStatusChanged(username, false));
                }
                else
                {
                    logger.Warn("removeFriend failed for {Username} trying to remove {FriendToRemoveUsername}. Reason: {Reason}", username, friendToRemoveUsername, result.Message);
                }
                return result;
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.DatabaseError, Lang.ErrorMsgServerOffline, "Database");
                logger.Fatal(ex, "Social Service DB Error in removeFriend");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.GenericServerError, "Server");
                logger.Fatal(ex, "Social Service Critical Error in removeFriend");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public async Task<List<FriendDto>> getFriendsList(string username)
        {
            if (!isCurrentUser(username))
            {
                logger.Warn("getFriendsList called by {Username}, but current session is for {CurrentUsername}. Aborting.", username, currentUsername ?? "N/A");
                return new List<FriendDto>();
            }
            logger.Info("getFriendsList request for user: {Username}", username);
            try
            {
                var friends = await socialLogic.getFriendsListAsync(username, getConnectedUsernames());
                logger.Info("Retrieved {Count} friends for user {Username}", friends?.Count ?? 0, username);
                return friends ?? new List<FriendDto>();
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.DatabaseError, Lang.ErrorMsgServerOffline, "Database");
                logger.Fatal(ex, "Social Service DB Error in getFriendsList");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.GenericServerError, "Server");
                logger.Fatal(ex, "Social Service Critical Error in getFriendsList");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequests(string username)
        {
            if (!isCurrentUser(username))
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
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.DatabaseError, Lang.ErrorMsgServerOffline, "Database");
                logger.Fatal(ex, "Social Service DB Error in getFriendRequests");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.GenericServerError, "Server");
                logger.Fatal(ex, "Social Service Critical Error in getFriendRequests");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        private async void Channel_FaultedOrClosed(object sender, EventArgs e)
        {
            logger.Warn("WCF channel Faulted or Closed for user: {Username}. Initiating cleanup.", currentUsername ?? "N/A");
            if (!string.IsNullOrEmpty(currentUsername))
            {
                await cleanupAndNotifyDisconnect(currentUsername);
            }
            cleanupCallbackEvents(sender as ICommunicationObject);
        }

        private static ISocialCallback tryGetCallbackChannel(string username)
        {
            string userForContext = username ?? "NULL";
            if (string.IsNullOrWhiteSpace(username) || OperationContext.Current == null)
            {
                logger.Warn("Connect failed: Username is empty or OperationContext is null. User: {Username}", userForContext);
                return null;
            }

            try
            {
                ISocialCallback callbackChannel = OperationContext.Current.GetCallbackChannel<ISocialCallback>();
                if (callbackChannel == null)
                {
                    logger.Error("Connect failed: GetCallbackChannel returned null for user: {Username}", userForContext);
                    return null;
                }
                return callbackChannel;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Connect failed: Exception getting callback channel for user: {Username}", userForContext);
                return null;
            }
        }

        private ISocialCallback updateCallbackFactory(string key, ISocialCallback existingCallback)
        {
            var existingComm = existingCallback as ICommunicationObject;

            if (existingCallback != currentUserCallback &&
                (existingComm == null || existingComm.State != CommunicationState.Opened))
            {
                logger.Warn("Replacing existing non-opened callback channel for user: {Username}", key);
                if (existingComm != null) cleanupCallbackEvents(existingComm);
                return currentUserCallback;
            }

            logger.Debug("Keeping existing callback channel for user: {Username} (State: {State})", key, existingComm?.State);

            if (existingCallback != currentUserCallback)
            {
                logger.Debug("Discarding newly obtained callback channel for {Username} as a valid one already exists.", key);
                currentUserCallback = existingCallback;
            }

            return existingCallback;
        }

        private async Task handleConnectionResult(ISocialCallback addedOrUpdatedCallback, string userForContext)
        {
            if (addedOrUpdatedCallback == currentUserCallback)
            {
                logger.Info("User connected and callback registered/updated: {Username}", userForContext);
                setupCallbackEvents(currentUserCallback as ICommunicationObject);
                await notifyFriendsStatusChange(currentUsername, true);
            }
            else
            {
                logger.Warn(
                    "User {Username} attempted to connect, but an existing active session was found. The new connection might replace the old one implicitly by WCF session management, or might coexist depending on configuration.",
                    userForContext);

                currentUserCallback = addedOrUpdatedCallback;
                setupCallbackEvents(currentUserCallback as ICommunicationObject);
            }
        }

        private async Task cleanupAndNotifyDisconnect(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                logger.Debug("cleanupAndNotifyDisconnect called with null or empty username. Skipping.");
                return;
            }

            logger.Info("Attempting to remove user {Username} from ConnectedUsers and notify friends.", username);
            
            if (connectedUsers.TryRemove(username, out ISocialCallback removedChannel))
            {
                logger.Info("User {Username} removed from ConnectedUsers.", username);
                cleanupCallbackEvents(removedChannel as ICommunicationObject);
                await notifyFriendsStatusChange(username, false);
            }
            else
            {
                logger.Warn("User {Username} was not found in ConnectedUsers during cleanup attempt.", username);
            }

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
                List<FriendDto> friendsToNotify = await socialLogic.getFriendsListAsync(changedUsername, null);
                logger.Debug("Found {Count} friends to potentially notify for user {Username}.", friendsToNotify?.Count ?? 0, changedUsername);

                if (friendsToNotify == null) return;

                var onlineFriendUsernames = friendsToNotify
                    .Select(friend => friend.Username)
                    .Where(username => SocialManagerService.isUserConnected(username));
                
                foreach (var username in onlineFriendUsernames)
                {
                    logger.Debug("Sending status change notification ({Username} is {Status}) to friend: {FriendUsername}", changedUsername, isOnline ? "Online" : "Offline", username);
                    sendNotificationToUser(username, cb => cb.notifyFriendStatusChanged(changedUsername, isOnline));
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[Service Error] - Exception in NotifyFriendsStatusChange for {Username}", changedUsername);
            }
        }

        public static void sendNotificationToUser(string targetUsername, Action<ISocialCallback> action)
        {
            if (string.IsNullOrWhiteSpace(targetUsername))
            {
                logger.Warn("sendNotificationToUser called with null or empty targetUsername.");
                return;
            }

            if (connectedUsers.TryGetValue(targetUsername, out ISocialCallback callbackChannel))
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
                        connectedUsers.TryRemove(targetUsername, out _);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Exception sending notification callback to user: {TargetUsername}", targetUsername);
                }
            }
            else
            {
                logger.Debug("User {TargetUsername} not found in ConnectedUsers. Skipping notification.", targetUsername);
            }
        }

        private void setupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= Channel_FaultedOrClosed;
                commObject.Closed -= Channel_FaultedOrClosed;
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

        private bool isCurrentUser(string username)
        {
            return !string.IsNullOrEmpty(currentUsername) && currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase);
        }
    }
}