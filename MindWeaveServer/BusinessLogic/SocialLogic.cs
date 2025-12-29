using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Shared;
using NLog;

namespace MindWeaveServer.BusinessLogic
{
    public class SocialLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IPlayerRepository playerRepository;
        private readonly IFriendshipRepository friendshipRepository;

        private const string DEFAULT_AVATAR_PATH = "/Resources/Images/Avatar/default_avatar.png";

        public SocialLogic(IPlayerRepository playerRepo, IFriendshipRepository friendshipRepo)
        {
            this.playerRepository = playerRepo;
            this.friendshipRepository = friendshipRepo;
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayersAsync(string requesterUsername, string query)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(requesterUsername))
            {
                logger.Warn("Search players ignored: Query or requester username is null/whitespace.");
                return new List<PlayerSearchResultDto>();
            }

            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);
            if (requester == null)
            {
                logger.Warn("Search players failed: Requester player {RequesterUsername} not found.", requesterUsername);
                return new List<PlayerSearchResultDto>();
            }

            var results = await playerRepository.searchPlayersAsync(requester.idPlayer, query);
            return results ?? new List<PlayerSearchResultDto>();
        }

        public async Task<OperationResultDto> sendFriendRequestAsync(string requesterUsername, string targetUsername)
        {
            if (string.IsNullOrWhiteSpace(requesterUsername) || string.IsNullOrWhiteSpace(targetUsername))
            {
                logger.Warn("Send friend request failed: Requester or target username is null/whitespace.");
                return new OperationResultDto { Success = false, Message = Lang.ValidationUsernameRequired };
            }

            if (requesterUsername.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("Send friend request failed: User {Username} attempted to send request to self.", requesterUsername);
                return new OperationResultDto { Success = false, Message = Lang.ErrorCannotSelfFriend };
            }

            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);
            var target = await playerRepository.getPlayerByUsernameAsync(targetUsername);

            if (requester == null || target == null)
            {
                logger.Warn("Send friend request failed: Requester (Found={RequesterFound}) or Target (Found={TargetFound}) player not found.", requester != null, target != null);
                return new OperationResultDto { Success = false, Message = Lang.ErrorPlayerNotFound };
            }

            var existingFriendship = await friendshipRepository.findFriendshipAsync(requester.idPlayer, target.idPlayer);

            if (existingFriendship != null)
            {
                return await handleExistingFriendshipAsync(existingFriendship, requester, target);
            }
            else
            {
                return await createNewFriendshipAsync(requester, target);
            }
        }

        public async Task<OperationResultDto> respondToFriendRequestAsync(string responderUsername, string requesterUsername, bool accepted)
        {
            if (string.IsNullOrWhiteSpace(responderUsername) || string.IsNullOrWhiteSpace(requesterUsername))
            {
                logger.Warn("Respond friend request failed: Responder or requester username is null/whitespace.");
                return new OperationResultDto { Success = false, Message = Lang.ValidationUsernameRequired };
            }

            var responder = await playerRepository.getPlayerByUsernameAsync(responderUsername);
            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);

            if (responder == null || requester == null)
            {
                logger.Warn("Respond friend request failed: Responder (Found={ResponderFound}) or Requester (Found={RequesterFound}) player not found.", responder != null, requester != null);
                return new OperationResultDto { Success = false, Message = Lang.ErrorPlayerNotFound };
            }

            var friendship = await friendshipRepository.findFriendshipAsync(requester.idPlayer, responder.idPlayer);

            if (friendship == null || friendship.status_id != FriendshipStatusConstants.PENDING || friendship.addressee_id != responder.idPlayer)
            {
                logger.Warn("Respond friend request failed: No matching PENDING request found directed to {ResponderUsername} from {RequesterUsername}. (Found={Found}, Status={StatusId}, Addressee={AddresseeId})",
                    responderUsername, requesterUsername, friendship != null, friendship?.status_id, friendship?.addressee_id);
                return new OperationResultDto { Success = false, Message = Lang.ErrorNoPendingRequestFound };
            }

            friendship.status_id = accepted ? FriendshipStatusConstants.ACCEPTED : FriendshipStatusConstants.REJECTED;

            friendshipRepository.updateFriendship(friendship);

            return new OperationResultDto { Success = true, Message = accepted ? Lang.FriendRequestAccepted : Lang.FriendRequestRejected };
        }

        public async Task<List<FriendDto>> getFriendsListAsync(string username, ICollection<string> connectedUsernames)
        {
            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                logger.Warn("Get friends list failed: Player {Username} not found.", username ?? "NULL");
                return new List<FriendDto>();
            }

            var friendships = await friendshipRepository.getAcceptedFriendshipsAsync(player.idPlayer);

            var onlineUsersSet = connectedUsernames != null
                ? new HashSet<string>(connectedUsernames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();
            var friendsList = new List<FriendDto>();

            if (friendships != null)
                foreach (var f in friendships)
                {
                    var friendDto = mapFriendshipToDto(f, player.idPlayer, onlineUsersSet);
                    if (friendDto != null)
                    {
                        friendsList.Add(friendDto);
                    }
                }

            return friendsList;
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequestsAsync(string username)
        {
            var player = await playerRepository.getPlayerByUsernameAsync(username);

            if (player == null)
            {
                logger.Warn("Get friend requests failed: Player {Username} not found.", username ?? "NULL");
                return new List<FriendRequestInfoDto>();
            }

            var pendingRequests = await friendshipRepository.getPendingFriendRequestsAsync(player.idPlayer);
            var requestPlayerList = pendingRequests
                .Where(req => req.Player1 != null)
                .Select(req => new FriendRequestInfoDto
                {
                    RequesterUsername = req.Player1.username,
                    RequestDate = req.request_date,
                    AvatarPath = req.Player1.avatar_path ?? DEFAULT_AVATAR_PATH
                })
                .OrderByDescending(r => r.RequestDate)
                .ToList();

            return requestPlayerList;
        }

        public async Task<OperationResultDto> removeFriendAsync(string username, string friendToRemoveUsername)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(friendToRemoveUsername))
            {
                logger.Warn("Remove friend failed: Username or friend username is null/whitespace.");
                return new OperationResultDto { Success = false, Message = Lang.ValidationUsernameRequired };
            }
            if (username.Equals(friendToRemoveUsername, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("Remove friend failed: User {Username} attempted to remove self.", username);
                return new OperationResultDto { Success = false, Message = Lang.ErrorCannotRemoveSelf };
            }

            var player = await playerRepository.getPlayerByUsernameAsync(username);
            var friendToRemove = await playerRepository.getPlayerByUsernameAsync(friendToRemoveUsername);

            if (player == null || friendToRemove == null)
            {
                logger.Warn("Remove friend failed: Player (Found={PlayerFound}) or Friend (Found={FriendFound}) not found.", player != null, friendToRemove != null);
                return new OperationResultDto { Success = false, Message = Lang.ErrorPlayerNotFound };
            }

            var friendship = await friendshipRepository.findFriendshipAsync(player.idPlayer, friendToRemove.idPlayer);

            if (friendship == null || friendship.status_id != FriendshipStatusConstants.ACCEPTED)
            {
                logger.Warn("Remove friend failed: No ACCEPTED friendship found between {Username} and {FriendToRemove}. (Found={Found}, Status={StatusId})", username, friendToRemoveUsername, friendship != null, friendship?.status_id);
                return new OperationResultDto { Success = false, Message = Lang.ErrorFriendshipNotFound };
            }

            friendshipRepository.removeFriendship(friendship);

            return new OperationResultDto { Success = true, Message = Lang.FriendRemovedSuccessfully };
        }

        private async Task<OperationResultDto> handleExistingFriendshipAsync(Friendships existingFriendship, Player requester, Player target)
        {
            switch (existingFriendship.status_id)
            {
                case FriendshipStatusConstants.ACCEPTED:
                    logger.Warn("Send request failed: Friendship already exists and is ACCEPTED.");
                    return new OperationResultDto { Success = false, Message = Lang.FriendshipAlreadyExists };

                case FriendshipStatusConstants.PENDING:
                    if (existingFriendship.requester_id == requester.idPlayer)
                    {
                        logger.Warn("Send request failed: A PENDING request from {RequesterUsername} to {TargetUsername} already exists.", requester.username, target.username);
                        return new OperationResultDto { Success = false, Message = Lang.FriendRequestAlreadySent };
                    }
                    else
                    {
                        logger.Warn("Send request failed: A PENDING request from {TargetUsername} to {RequesterUsername} already exists. User should respond instead.", target.username, requester.username);
                        return new OperationResultDto { Success = false, Message = Lang.FriendRequestReceivedFromUser };
                    }

                case FriendshipStatusConstants.REJECTED:
                    existingFriendship.requester_id = requester.idPlayer;
                    existingFriendship.addressee_id = target.idPlayer;
                    existingFriendship.status_id = FriendshipStatusConstants.PENDING;
                    existingFriendship.request_date = DateTime.UtcNow;

                    friendshipRepository.updateFriendship(existingFriendship);

                    return new OperationResultDto { Success = true, Message = Lang.FriendRequestSent };

                default:
                    logger.Error("Send request failed: Unknown or unhandled friendship status ({StatusId}) between {RequesterUsername} and {TargetUsername}.", existingFriendship.status_id, requester.username, target.username);
                    return new OperationResultDto { Success = false, Message = Lang.FriendshipStatusPreventsRequest };
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

            return new OperationResultDto { Success = true, Message = Lang.FriendRequestSent };
        }

        private static FriendDto mapFriendshipToDto(Friendships f, int ownPlayerId, HashSet<string> onlineUsersSet)
        {
            int friendId = (f.requester_id == ownPlayerId) ? f.addressee_id : f.requester_id;
            Player friendEntity = (f.Player1?.idPlayer == friendId) ? f.Player1 : f.Player;

            if (friendEntity != null)
            {
                bool isOnline = onlineUsersSet.Contains(friendEntity.username);
                return new FriendDto
                {
                    Username = friendEntity.username,
                    IsOnline = isOnline,
                    AvatarPath = friendEntity.avatar_path ?? DEFAULT_AVATAR_PATH
                };
            }

            return null;
        }
    }
}