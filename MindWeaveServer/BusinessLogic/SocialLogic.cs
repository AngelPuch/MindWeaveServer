using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Utilities;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class SocialLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IPlayerRepository playerRepository;
        private readonly IFriendshipRepository friendshipRepository;

        private const string DEFAULT_AVATAR_PATH = "/Resources/Images/Avatar/default_avatar.png";
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> userLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

        public SocialLogic(IPlayerRepository playerRepo, IFriendshipRepository friendshipRepo)
        {
            this.playerRepository = playerRepo;
            this.friendshipRepository = friendshipRepo;
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayersAsync(string requesterUsername, string query)
        {
            if (isInvalidSearchInput(requesterUsername, query))
            {
                return new List<PlayerSearchResultDto>();
            }

            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);
            if (requester == null)
            {
                logger.Warn("Search players failed: Requester player not found.");
                return new List<PlayerSearchResultDto>();
            }

            var results = await playerRepository.searchPlayersAsync(requester.idPlayer, query);
            return results ?? new List<PlayerSearchResultDto>();
        }

        public async Task<OperationResultDto> sendFriendRequestAsync(string requesterUsername, string targetUsername)
        {
            var validationResult = validateFriendRequestInput(requesterUsername, targetUsername);
            if (!validationResult.Success)
            {
                return validationResult;
            }

            var userLock = getLockForUsers(requesterUsername, targetUsername);
            await userLock.WaitAsync();

            try
            {
                return await processFriendRequestAsync(requesterUsername, targetUsername);
            }
            finally
            {
                userLock.Release();
            }
        }

        public async Task<OperationResultDto> respondToFriendRequestAsync(string responderUsername, string requesterUsername, bool accepted)
        {
            if (isInvalidUsernameInput(responderUsername, requesterUsername))
            {
                logger.Warn("Respond friend request failed: Responder or requester username is null/whitespace.");
                return createFailureResult(MessageCodes.VALIDATION_USERNAME_REQUIRED);
            }

            var responder = await playerRepository.getPlayerByUsernameAsync(responderUsername);
            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);

            if (responder == null || requester == null)
            {
                logger.Warn("Respond friend request failed: Responder (Found={ResponderFound}) or Requester (Found={RequesterFound}) player not found.",
                    responder != null, requester != null);
                return createFailureResult(MessageCodes.SOCIAL_USER_NOT_FOUND);
            }

            return await processRespondToFriendRequestAsync(responder, requester, accepted);
        }

        public async Task<List<FriendDto>> getFriendsListAsync(string username, ICollection<string> connectedUsernames)
        {
            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                logger.Warn("Get friends list failed: Player not found.");
                return new List<FriendDto>();
            }

            var friendships = await friendshipRepository.getAcceptedFriendshipsAsync(player.idPlayer);
            var onlineUsersSet = createOnlineUsersSet(connectedUsernames);

            return buildFriendsList(friendships, player.idPlayer, onlineUsersSet);
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequestsAsync(string username)
        {
            var player = await playerRepository.getPlayerByUsernameAsync(username);

            if (player == null)
            {
                logger.Warn("Get friend requests failed: Player not found.");
                return new List<FriendRequestInfoDto>();
            }

            var pendingRequests = await friendshipRepository.getPendingFriendRequestsAsync(player.idPlayer);

            return mapPendingRequestsToDto(pendingRequests);
        }

        public async Task<OperationResultDto> removeFriendAsync(string username, string friendToRemoveUsername)
        {
            var validationResult = validateRemoveFriendInput(username, friendToRemoveUsername);
            if (!validationResult.Success)
            {
                return validationResult;
            }

            var player = await playerRepository.getPlayerByUsernameAsync(username);
            var friendToRemove = await playerRepository.getPlayerByUsernameAsync(friendToRemoveUsername);

            if (player == null || friendToRemove == null)
            {
                logger.Warn("Remove friend failed: Player (Found={PlayerFound}) or Friend (Found={FriendFound}) not found.",
                    player != null, friendToRemove != null);
                return createFailureResult(MessageCodes.SOCIAL_USER_NOT_FOUND);
            }

            return await processRemoveFriendAsync(player, friendToRemove);
        }

        private static bool isInvalidSearchInput(string requesterUsername, string query)
        {
            if (!string.IsNullOrWhiteSpace(query) && !string.IsNullOrWhiteSpace(requesterUsername))
            {
                return false;
            }

            logger.Warn("Search players ignored: Query or requester username is null/whitespace.");
            return true;
        }

        private static bool isInvalidUsernameInput(string username1, string username2)
        {
            return string.IsNullOrWhiteSpace(username1) || string.IsNullOrWhiteSpace(username2);
        }

        private static SemaphoreSlim getLockForUsers(string user1, string user2)
        {
            string key = string.Compare(user1, user2, StringComparison.OrdinalIgnoreCase) < 0
                ? $"{user1.ToLower()}_{user2.ToLower()}"
                : $"{user2.ToLower()}_{user1.ToLower()}";

            return userLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        }

        private static OperationResultDto createFailureResult(string messageCode)
        {
            return new OperationResultDto { Success = false, MessageCode = messageCode };
        }

        private static OperationResultDto createSuccessResult(string messageCode)
        {
            return new OperationResultDto { Success = true, MessageCode = messageCode };
        }

        private OperationResultDto validateFriendRequestInput(string requesterUsername, string targetUsername)
        {
            if (isInvalidUsernameInput(requesterUsername, targetUsername))
            {
                logger.Warn("Send friend request failed: Requester or target username is null/whitespace.");
                return createFailureResult(MessageCodes.VALIDATION_USERNAME_REQUIRED);
            }

            if (requesterUsername.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("Send friend request failed: User attempted to send request to self.");
                return createFailureResult(MessageCodes.SOCIAL_CANNOT_ADD_SELF);
            }

            return new OperationResultDto { Success = true };
        }

        private async Task<OperationResultDto> processFriendRequestAsync(string requesterUsername, string targetUsername)
        {
            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);
            var target = await playerRepository.getPlayerByUsernameAsync(targetUsername);

            if (requester == null || target == null)
            {
                return createFailureResult(MessageCodes.SOCIAL_USER_NOT_FOUND);
            }

            var existingFriendship = await friendshipRepository.findFriendshipAsync(requester.idPlayer, target.idPlayer);

            if (existingFriendship == null)
            {
                return createNewFriendship(requester, target);
            }

            return handleExistingFriendshipRequest(existingFriendship, requester, target);
        }

        private OperationResultDto handleExistingFriendshipRequest(Friendships existingFriendship, Player requester, Player target)
        {
            if (isPendingRequestFromTarget(existingFriendship, target.idPlayer))
            {
                return acceptPendingRequest(existingFriendship);
            }

            return handleExistingFriendshipByStatus(existingFriendship, requester);
        }

        private static bool isPendingRequestFromTarget(Friendships friendship, int targetId)
        {
            return friendship.status_id == FriendshipStatusConstants.PENDING &&
                   friendship.requester_id == targetId;
        }

        private OperationResultDto acceptPendingRequest(Friendships friendship)
        {
            friendship.status_id = FriendshipStatusConstants.ACCEPTED;
            friendshipRepository.updateFriendship(friendship);

            return createSuccessResult(MessageCodes.SOCIAL_FRIEND_REQUEST_ACCEPTED);
        }

        private OperationResultDto handleExistingFriendshipByStatus(Friendships existingFriendship, Player requester)
        {
            switch (existingFriendship.status_id)
            {
                case FriendshipStatusConstants.ACCEPTED:
                    return handleAcceptedFriendship();

                case FriendshipStatusConstants.PENDING:
                    return handlePendingFriendship(existingFriendship, requester);

                case FriendshipStatusConstants.REJECTED:
                    return reactivateFriendship(existingFriendship, requester);

                default:
                    return handleUnknownFriendshipStatus(existingFriendship.status_id);
            }
        }

        private static OperationResultDto handleAcceptedFriendship()
        {
            logger.Warn("Send request failed: Friendship already exists and is ACCEPTED.");
            return createFailureResult(MessageCodes.SOCIAL_ALREADY_FRIENDS);
        }

        private static OperationResultDto handlePendingFriendship(Friendships existingFriendship, Player requester)
        {
            if (existingFriendship.requester_id == requester.idPlayer)
            {
                logger.Warn("Send request failed: A PENDING request already exists.");
                return createFailureResult(MessageCodes.SOCIAL_REQUEST_ALREADY_SENT);
            }

            logger.Warn("Send request failed: A PENDING request from target already exists.");
            return createFailureResult(MessageCodes.SOCIAL_REQUEST_ALREADY_RECEIVED);
        }

        private OperationResultDto reactivateFriendship(Friendships existingFriendship, Player requester)
        {
            existingFriendship.requester_id = requester.idPlayer;
            existingFriendship.status_id = FriendshipStatusConstants.PENDING;
            existingFriendship.request_date = DateTime.UtcNow;

            friendshipRepository.updateFriendship(existingFriendship);

            return createSuccessResult(MessageCodes.SOCIAL_FRIEND_REQUEST_SENT);
        }

        private static OperationResultDto handleUnknownFriendshipStatus(int statusId)
        {
            logger.Error("Send request failed: Unknown friendship status ({StatusId}).", statusId);
            return createFailureResult(MessageCodes.ERROR_SERVER_GENERIC);
        }

        private OperationResultDto createNewFriendship(Player requester, Player target)
        {
            var newFriendship = new Friendships
            {
                requester_id = requester.idPlayer,
                addressee_id = target.idPlayer,
                request_date = DateTime.UtcNow,
                status_id = FriendshipStatusConstants.PENDING
            };

            friendshipRepository.addFriendship(newFriendship);

            return createSuccessResult(MessageCodes.SOCIAL_FRIEND_REQUEST_SENT);
        }

        private async Task<OperationResultDto> processRespondToFriendRequestAsync(Player responder, Player requester, bool accepted)
        {
            var friendship = await friendshipRepository.findFriendshipAsync(requester.idPlayer, responder.idPlayer);

            if (!isValidPendingRequest(friendship, responder.idPlayer))
            {
                logger.Warn("Respond friend request failed: No matching PENDING request found.");
                return createFailureResult(MessageCodes.SOCIAL_REQUEST_NOT_FOUND);
            }

            return updateFriendshipResponse(friendship, accepted);
        }

        private static bool isValidPendingRequest(Friendships friendship, int responderId)
        {
            return friendship != null &&
                   friendship.status_id == FriendshipStatusConstants.PENDING &&
                   friendship.addressee_id == responderId;
        }

        private OperationResultDto updateFriendshipResponse(Friendships friendship, bool accepted)
        {
            friendship.status_id = accepted ? FriendshipStatusConstants.ACCEPTED : FriendshipStatusConstants.REJECTED;
            friendshipRepository.updateFriendship(friendship);

            string messageCode = accepted
                ? MessageCodes.SOCIAL_FRIEND_REQUEST_ACCEPTED
                : MessageCodes.SOCIAL_FRIEND_REQUEST_DECLINED;

            return createSuccessResult(messageCode);
        }

        private OperationResultDto validateRemoveFriendInput(string username, string friendToRemoveUsername)
        {
            if (isInvalidUsernameInput(username, friendToRemoveUsername))
            {
                logger.Warn("Remove friend failed: Username or friend username is null/whitespace.");
                return createFailureResult(MessageCodes.VALIDATION_USERNAME_REQUIRED);
            }

            if (username.Equals(friendToRemoveUsername, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("Remove friend failed: User attempted to remove self.");
                return createFailureResult(MessageCodes.SOCIAL_CANNOT_ADD_SELF);
            }

            return new OperationResultDto { Success = true };
        }

        private async Task<OperationResultDto> processRemoveFriendAsync(Player player, Player friendToRemove)
        {
            var friendship = await friendshipRepository.findFriendshipAsync(player.idPlayer, friendToRemove.idPlayer);

            if (!isAcceptedFriendship(friendship))
            {
                logger.Warn("Remove friend failed: No ACCEPTED friendship found.");
                return createFailureResult(MessageCodes.SOCIAL_NOT_FRIENDS);
            }

            friendshipRepository.removeFriendship(friendship);

            return createSuccessResult(MessageCodes.SOCIAL_FRIEND_REMOVED);
        }

        private static bool isAcceptedFriendship(Friendships friendship)
        {
            return friendship != null && friendship.status_id == FriendshipStatusConstants.ACCEPTED;
        }

        private static HashSet<string> createOnlineUsersSet(ICollection<string> connectedUsernames)
        {
            return connectedUsernames != null
                ? new HashSet<string>(connectedUsernames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();
        }

        private static List<FriendDto> buildFriendsList(IEnumerable<Friendships> friendships, int ownPlayerId, HashSet<string> onlineUsersSet)
        {
            if (friendships == null)
            {
                return new List<FriendDto>();
            }

            return friendships
                .Select(f => mapFriendshipToDto(f, ownPlayerId, onlineUsersSet))
                .Where(dto => dto != null)
                .ToList();
        }

        private static FriendDto mapFriendshipToDto(Friendships f, int ownPlayerId, HashSet<string> onlineUsersSet)
        {
            Player friendEntity = getFriendEntity(f, ownPlayerId);

            if (friendEntity == null)
            {
                return null;
            }

            return new FriendDto
            {
                Username = friendEntity.username,
                IsOnline = onlineUsersSet.Contains(friendEntity.username),
                AvatarPath = friendEntity.avatar_path ?? DEFAULT_AVATAR_PATH
            };
        }

        private static Player getFriendEntity(Friendships f, int ownPlayerId)
        {
            int friendId = (f.requester_id == ownPlayerId) ? f.addressee_id : f.requester_id;
            return (f.Player1?.idPlayer == friendId) ? f.Player1 : f.Player;
        }

        private static List<FriendRequestInfoDto> mapPendingRequestsToDto(IEnumerable<Friendships> pendingRequests)
        {
            return pendingRequests
                .Where(req => req.Player1 != null)
                .Select(mapToFriendRequestInfoDto)
                .OrderByDescending(r => r.RequestDate)
                .ToList();
        }

        private static FriendRequestInfoDto mapToFriendRequestInfoDto(Friendships req)
        {
            return new FriendRequestInfoDto
            {
                RequesterUsername = req.Player1.username,
                RequestDate = req.request_date,
                AvatarPath = req.Player1.avatar_path ?? DEFAULT_AVATAR_PATH
            };
        }
    }
}