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
using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic.Abstractions;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class SocialManagerService : ISocialManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly SocialLogic socialLogic;
        private readonly IGameStateManager gameStateManager;
        private ISocialCallback currentUserCallback;

        private string currentUsername;

        public SocialManagerService() : this(resolveDep()) { }

        private static (SocialLogic, IGameStateManager) resolveDep()
        {
            Bootstrapper.init();
            return (
                Bootstrapper.Container.Resolve<SocialLogic>(),
                Bootstrapper.Container.Resolve<IGameStateManager>()
            );
        }

        public SocialManagerService((SocialLogic logic, IGameStateManager state) dependencies)
        {
            this.socialLogic = dependencies.logic;
            this.gameStateManager = dependencies.state;

            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += Channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed += Channel_FaultedOrClosed;
            }
        }

        public static bool isUserConnected(string username)
        {
            try
            {
                var manager = Bootstrapper.Container.Resolve<IGameStateManager>();
                return manager.isUserConnected(username);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error resolving GameStateManager in static method isUserConnected.");
                return false;
            }
        }

        public void connect(string username)
        {
            ISocialCallback callbackChannel = tryGetCallbackChannel(username);
            if (callbackChannel == null) return;

            this.currentUserCallback = callbackChannel;
            this.currentUsername = username;
            string userForContext = username ?? "NULL";

            Task.Run(async () =>
            {
                var existingCallback = gameStateManager.getUserCallback(currentUsername);
                gameStateManager.addConnectedUser(currentUsername, currentUserCallback);
                await handleConnectionResult(existingCallback, currentUserCallback, userForContext);
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
                    logger.Warn("Disconnect mismatch/invalid: Request: {Req}, Session: {Sess}", userForContext, currentUsername ?? "N/A");
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
                var connectedUsersList = gameStateManager.ConnectedUsers.Keys.ToList();
                var friends = await socialLogic.getFriendsListAsync(username, connectedUsersList);
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

        private async Task handleConnectionResult(ISocialCallback previousCallback, ISocialCallback newCallback, string userForContext)
        {
            if (previousCallback == null || previousCallback == newCallback)
            {
                logger.Info("User connected and callback registered/updated: {Username}", userForContext);
                setupCallbackEvents(newCallback as ICommunicationObject);
                await notifyFriendsStatusChange(currentUsername, true);
            }
            else
            {
                logger.Warn(
                    "User {Username} attempted to connect, but an existing active session was found. The new connection replaced the old one.",
                    userForContext);

                if (previousCallback is ICommunicationObject oldComm) cleanupCallbackEvents(oldComm);

                setupCallbackEvents(newCallback as ICommunicationObject);
            }
        }

        private async Task cleanupAndNotifyDisconnect(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                logger.Debug("cleanupAndNotifyDisconnect called with null or empty username. Skipping.");
                return;
            }

            if (gameStateManager.isUserConnected(username))
            {
                var callbackToRemove = gameStateManager.getUserCallback(username);
                gameStateManager.removeConnectedUser(username);

                logger.Info("User {Username} removed from ConnectedUsers.", username);
                if (callbackToRemove is ICommunicationObject comm) cleanupCallbackEvents(comm);

                await notifyFriendsStatusChange(username, false);
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
                    .Where(SocialManagerService.isUserConnected);
                
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

            try
            {
                var manager = Bootstrapper.Container.Resolve<IGameStateManager>();
                var callback = manager.getUserCallback(targetUsername);

                if (callback != null)
                {
                    try
                    {
                        var commObject = callback as ICommunicationObject;
                        if (commObject != null && commObject.State == CommunicationState.Opened)
                        {
                            logger.Debug("Sending notification callback to user: {TargetUsername}", targetUsername);
                            action(callback);
                        }
                        else
                        { 
                            logger.Warn("Callback channel for user {TargetUsername} is not open (State: {State}). Skipping notification and removing.", targetUsername, commObject?.State);
                            manager.removeConnectedUser(targetUsername);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Exception sending notification callback to user: {TargetUsername}", targetUsername);
                    }
                }
               
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error resolving GameStateManager in sendNotificationToUser.");
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
            }
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= Channel_FaultedOrClosed;
                commObject.Closed -= Channel_FaultedOrClosed;
            }
        }

        private bool isCurrentUser(string username)
        {
            return !string.IsNullOrEmpty(currentUsername) && currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase);
        }
    }
}