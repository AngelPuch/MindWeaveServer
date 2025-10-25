using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities; 
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.BusinessLogic
{
    public class SocialLogic
    {
        private readonly IPlayerRepository playerRepository;
        private readonly IFriendshipRepository friendshipRepository;
        private const int SEARCH_RESULT_LIMIT = 10;

        public SocialLogic(IPlayerRepository playerRepo, IFriendshipRepository friendshipRepo)
        {
            this.playerRepository = playerRepo ?? throw new ArgumentNullException(nameof(playerRepo));
            this.friendshipRepository = friendshipRepo ?? throw new ArgumentNullException(nameof(friendshipRepo));
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayersAsync(string requesterUsername, string query)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(requesterUsername))
            {
                return new List<PlayerSearchResultDto>();
            }

            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);
            if (requester == null)
            {
                return new List<PlayerSearchResultDto>();
            }

            try
            {
                return await playerRepository.SearchPlayersAsync(requester.idPlayer, query, SEARCH_RESULT_LIMIT);
            }
            catch (Exception ex)
            {
                return new List<PlayerSearchResultDto>();
            }

        }

        public async Task<OperationResultDto> sendFriendRequestAsync(string requesterUsername, string targetUsername)
        {
            if (string.IsNullOrWhiteSpace(requesterUsername) || string.IsNullOrWhiteSpace(targetUsername))
            {
                return new OperationResultDto { success = false, message = Lang.ValidationUsernameRequired };
            }

            if (requesterUsername.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
            {
                return new OperationResultDto
                { success = false, message = Lang.ErrorCannotSelfFriend };
            }

            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);
            var target = await playerRepository.getPlayerByUsernameAsync(targetUsername);

            if (requester == null || target == null)
            {
                return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
            }

            var existingFriendship =
                await friendshipRepository.findFriendshipAsync(requester.idPlayer, target.idPlayer);

            if (existingFriendship != null)
            {
                return await handleExistingFriendshipAsync(existingFriendship, requester, target);
            }

            return await createNewFriendshipAsync(requester, target);
        }


        public async Task<OperationResultDto> respondToFriendRequestAsync(string responderUsername, string requesterUsername, bool accepted)
        {
            if (string.IsNullOrWhiteSpace(responderUsername) || string.IsNullOrWhiteSpace(requesterUsername))
            {
                return new OperationResultDto { success = false, message = Lang.ValidationUsernameRequired };
            }

            var responder = await playerRepository.getPlayerByUsernameAsync(responderUsername);
            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);

            if (responder == null || requester == null)
            {
                return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
            }

            var friendship = await friendshipRepository.findFriendshipAsync(requester.idPlayer, responder.idPlayer);

            if (friendship == null || friendship.status_id != FriendshipStatusConstants.PENDING || friendship.addressee_id != responder.idPlayer)
            {
                return new OperationResultDto { success = false, message = Lang.ErrorNoPendingRequestFound }; 
            }

            friendship.status_id = accepted ? FriendshipStatusConstants.ACCEPTED : FriendshipStatusConstants.REJECTED;
            friendshipRepository.updateFriendship(friendship);

            try
            {
                await friendshipRepository.saveChangesAsync();
                return new OperationResultDto { success = true, message = accepted ? Lang.FriendRequestAccepted : Lang.FriendRequestRejected };
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<List<FriendDto>> getFriendsListAsync(string username, ICollection<string> connectedUsernames)
        {
            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                return new List<FriendDto>();
            }

            var friendships = await friendshipRepository.getAcceptedFriendshipsAsync(player.idPlayer);
            var onlineUsersSet = connectedUsernames != null ? new HashSet<string>(connectedUsernames, StringComparer.OrdinalIgnoreCase) 
                : new HashSet<string>();
            var friendDtos = new List<FriendDto>();

            foreach (var f in friendships)
            {
                int friendId = (f.requester_id == player.idPlayer) ? f.addressee_id : f.requester_id;
                Player friendEntity = (f.Player1?.idPlayer == friendId) ? f.Player1 : f.Player;

                if (friendEntity != null)
                {
                    bool isOnline = onlineUsersSet.Contains(friendEntity.username);
                    friendDtos.Add(new FriendDto
                    {
                        username = friendEntity.username,
                        isOnline = isOnline,
                        avatarPath = friendEntity.avatar_path ?? "/Resources/Images/Avatar/default_avatar.png"
                    });
                }
            }

            return friendDtos;
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequestsAsync(string username)
        {
            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                return new List<FriendRequestInfoDto>();
            }

            var pendingRequests = await friendshipRepository.getPendingFriendRequestsAsync(player.idPlayer);

            var requestInfoDtos = pendingRequests
                .Where(req => req.Player1 != null)
                .Select(req => new FriendRequestInfoDto
                {
                    requesterUsername = req.Player1.username,
                    requestDate = req.request_date,
                    avatarPath = req.Player1.avatar_path ?? "/Resources/Images/Avatar/default_avatar.png"


                })
                .OrderByDescending(r => r.requestDate)
                .ToList();

            return requestInfoDtos;
        }

        public async Task<OperationResultDto> removeFriendAsync(string username, string friendToRemoveUsername)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(friendToRemoveUsername))
            {
                return new OperationResultDto { success = false, message = Lang.ValidationUsernameRequired };
            }
            if (username.Equals(friendToRemoveUsername, StringComparison.OrdinalIgnoreCase))
            {
                return new OperationResultDto { success = false, message = Lang.ErrorCannotRemoveSelf };
            }

            var player = await playerRepository.getPlayerByUsernameAsync(username);
            var friendToRemove = await playerRepository.getPlayerByUsernameAsync(friendToRemoveUsername);

            if (player == null || friendToRemove == null)
            {
                return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
            }

            var friendship = await friendshipRepository.findFriendshipAsync(player.idPlayer, friendToRemove.idPlayer);

            if (friendship == null || friendship.status_id != FriendshipStatusConstants.ACCEPTED)
            {
                return new OperationResultDto { success = false, message = Lang.ErrorFriendshipNotFound };
            }

            friendshipRepository.removeFriendship(friendship);

            try
            {
                await friendshipRepository.saveChangesAsync();
                return new OperationResultDto { success = true, message = Lang.FriendRemovedSuccessfully };
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        private async Task<OperationResultDto> handleExistingFriendshipAsync(Friendships existingFriendship, Player requester, Player target)
        {
            switch (existingFriendship.status_id)
            {
                case FriendshipStatusConstants.ACCEPTED:
                    return new OperationResultDto { success = false, message = Lang.FriendshipAlreadyExists };

                case FriendshipStatusConstants.PENDING:
                    return existingFriendship.requester_id == requester.idPlayer ?
                        new OperationResultDto { success = false, message = Lang.FriendRequestAlreadySent } :
                        new OperationResultDto { success = false, message = Lang.FriendRequestReceivedFromUser };

                case FriendshipStatusConstants.REJECTED:
                    existingFriendship.requester_id = requester.idPlayer;
                    existingFriendship.addressee_id = target.idPlayer;
                    existingFriendship.status_id = FriendshipStatusConstants.PENDING;
                    existingFriendship.request_date = DateTime.UtcNow;
                    friendshipRepository.updateFriendship(existingFriendship);
                    await friendshipRepository.saveChangesAsync();
                    return new OperationResultDto { success = true, message = Lang.FriendRequestSent };

                default:
                    return new OperationResultDto { success = false, message = Lang.FriendshipStatusPreventsRequest };
            }
        }

        private async Task<OperationResultDto> createNewFriendshipAsync(Player requester, Player target)
        {

            var newFriendship = new Friendships
            {
                requester_id = requester.idPlayer,
                addressee_id = target.idPlayer,
                request_date = DateTime.UtcNow,
                status_id = FriendshipStatusConstants.PENDING
            };
            friendshipRepository.addFriendship(newFriendship);

            try
            {
                await friendshipRepository.saveChangesAsync();
                return new OperationResultDto { success = true, message = Lang.FriendRequestSent };
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

    }
}