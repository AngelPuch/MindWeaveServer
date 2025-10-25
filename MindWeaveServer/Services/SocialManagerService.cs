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
using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class SocialManagerService : ISocialManager
    {
        public static readonly ConcurrentDictionary<string, ISocialCallback> ConnectedUsers =
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

            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += Channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed += Channel_FaultedOrClosed;
            }
        }

        public async Task connect(string username)
        {
            if (string.IsNullOrWhiteSpace(username) || OperationContext.Current == null)
            {
                return;
            }

            currentUserCallback = OperationContext.Current.GetCallbackChannel<ISocialCallback>();
            currentUsername = username;

            if (currentUserCallback == null)
            {
                return;
            }

            ISocialCallback addedOrUpdatedCallback = ConnectedUsers.AddOrUpdate(
                currentUsername, currentUserCallback,
                (key, existingCallback) =>
                {
                    var existingComm = existingCallback as ICommunicationObject;
                    if (existingCallback != currentUserCallback && (existingComm == null || existingComm.State != CommunicationState.Opened))
                    {
                        if (existingComm != null) cleanupCallbackEvents(existingComm);
                        return currentUserCallback;
                    }
                    return existingCallback;
                });

            if (addedOrUpdatedCallback == currentUserCallback)
            {
                setupCallbackEvents(currentUserCallback as ICommunicationObject);
                await notifyFriendsStatusChange(currentUsername, true);
            }
        
        }

        public async Task disconnect(string username)
        {
            if (!string.IsNullOrEmpty(username))
            {
                await cleanupAndNotifyDisconnect(username);

            }
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayers(string requesterUsername, string query)
        {
            try
            {
                return await socialLogic.searchPlayersAsync(requesterUsername, query);

            }
            catch (Exception ex)
            {
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
                    sendNotificationToUser(targetUsername, cb => cb.notifyFriendRequest(requesterUsername));
                }

                return result;
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.GenericServerError };

            }
        }

        public async Task<OperationResultDto> respondToFriendRequest(string responderUsername, string requesterUsername, bool accepted)
        {
            try
            {
                var result =
                    await socialLogic.respondToFriendRequestAsync(responderUsername, requesterUsername, accepted);
                if (result.success)
                {
                    sendNotificationToUser(requesterUsername,
                        cb => cb.notifyFriendResponse(responderUsername, accepted));
                    if (accepted)
                    {
                        bool responderIsOnline = ConnectedUsers.ContainsKey(responderUsername);
                        bool requesterIsOnline = ConnectedUsers.ContainsKey(requesterUsername);
                        if (responderIsOnline)
                        {
                            await notifyFriendsStatusChange(responderUsername, true);
                        }

                        if (requesterIsOnline)
                        {
                            await notifyFriendsStatusChange(requesterUsername,
                                ConnectedUsers.ContainsKey(requesterUsername));
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.GenericServerError }; 

            }
        }

        public async Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername)
        {
            try
            {
                var result = await socialLogic.removeFriendAsync(username, friendToRemoveUsername);
                if (result.success)
                {
                    sendNotificationToUser(friendToRemoveUsername, cb => cb.notifyFriendStatusChanged(username, false));
                    sendNotificationToUser(username, cb => cb.notifyFriendStatusChanged(friendToRemoveUsername, false));
                }

                return result;
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.GenericServerError }; 

            }
        }

        public async Task<List<FriendDto>> getFriendsList(string username)
        {
            try
            {
                return await socialLogic.getFriendsListAsync(username, ConnectedUsers.Keys);
            }
            catch (Exception ex)
            {
                return new List<FriendDto>();
            }
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequests(string username)
        {
            try
            {
                return await socialLogic.getFriendRequestsAsync(username);
            }
            catch (Exception ex) 
            { 
                return new List<FriendRequestInfoDto>();
            }
        }

        private async void Channel_FaultedOrClosed(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(currentUsername))
            {
                await cleanupAndNotifyDisconnect(currentUsername);
            }
            cleanupCallbackEvents(sender as ICommunicationObject);
        }

        private async Task cleanupAndNotifyDisconnect(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                return;
            }

            if (ConnectedUsers.TryRemove(username, out ISocialCallback removedChannel))
            {
                cleanupCallbackEvents(removedChannel as ICommunicationObject);
                await notifyFriendsStatusChange(username, false);
            }
        }

        private async Task notifyFriendsStatusChange(string changedUsername, bool isOnline)
        {
            try
            {
                List<FriendDto> friendsToNotify = await socialLogic.getFriendsListAsync(changedUsername, null);

                foreach (var friend in friendsToNotify)
                {
                    if (ConnectedUsers.TryGetValue(friend.username, out ISocialCallback friendCallback))
                    {
                        sendNotificationToUser(friend.username, cb => cb.notifyFriendStatusChanged(changedUsername, isOnline));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service Error - NotifyFriendsStatusChangeAsync for {changedUsername}]: {ex.ToString()}");
            }
            
        }

        public static void sendNotificationToUser(string targetUsername, Action<ISocialCallback> action)
        {
            if (string.IsNullOrWhiteSpace(targetUsername))
            {
                return;
            }

            if (ConnectedUsers.TryGetValue(targetUsername, out ISocialCallback callbackChannel))
            {
                try
                {
                    var commObject = callbackChannel as ICommunicationObject;
                    if (commObject != null && commObject.State == CommunicationState.Opened)
                    {
                        action(callbackChannel);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] !!! Exception sending callback to {targetUsername}: {ex.Message}");
                }
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

    }
}