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
        private const int SEARCH_RESULT_LIMIT = 10;

        public SocialLogic(IPlayerRepository playerRepo, IFriendshipRepository friendshipRepo)
        {
            this.playerRepository = playerRepo;
            this.friendshipRepository = friendshipRepo;
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayersAsync(string requesterUsername, string query)
        {
            logger.Info("searchPlayersAsync called by User: {RequesterUsername}, Query: '{Query}'", requesterUsername ?? "NULL", query ?? "NULL");
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(requesterUsername))
            {
                logger.Warn("Search players ignored: Query or requester username is null/whitespace.");
                return new List<PlayerSearchResultDto>();
            }

            logger.Debug("Fetching requester player data for search: {RequesterUsername}", requesterUsername);
            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);
            if (requester == null)
            {
                logger.Warn("Search players failed: Requester player {RequesterUsername} not found.", requesterUsername);
                return new List<PlayerSearchResultDto>();
            }

            logger.Debug("Performing player search in repository. RequesterID: {PlayerId}, Query: '{Query}', Limit: {Limit}", requester.idPlayer, query, SEARCH_RESULT_LIMIT);
            var results = await playerRepository.searchPlayersAsync(requester.idPlayer, query, SEARCH_RESULT_LIMIT);
            logger.Info("Player search completed for User: {RequesterUsername}, Query: '{Query}'. Found {Count} results.", requesterUsername, query, results?.Count ?? 0);
            return results ?? new List<PlayerSearchResultDto>();

        }

        public async Task<OperationResultDto> sendFriendRequestAsync(string requesterUsername, string targetUsername)
        {
            logger.Info("sendFriendRequestAsync called from User: {RequesterUsername} to Target: {TargetUsername}", requesterUsername ?? "NULL", targetUsername ?? "NULL");
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


            logger.Debug("Fetching requester ({RequesterUsername}) and target ({TargetUsername}) player data.", requesterUsername, targetUsername);
            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);
            var target = await playerRepository.getPlayerByUsernameAsync(targetUsername);

            if (requester == null || target == null)
            {
                logger.Warn("Send friend request failed: Requester (Found={RequesterFound}) or Target (Found={TargetFound}) player not found.", requester != null, target != null);
                return new OperationResultDto { Success = false, Message = Lang.ErrorPlayerNotFound };
            }
            logger.Debug("Both players found. RequesterID: {RequesterId}, TargetID: {TargetId}", requester.idPlayer, target.idPlayer);

            logger.Debug("Checking for existing friendship between PlayerID {RequesterId} and PlayerID {TargetId}", requester.idPlayer, target.idPlayer);
            var existingFriendship = await friendshipRepository.findFriendshipAsync(requester.idPlayer, target.idPlayer);

            if (existingFriendship != null)
            {
                logger.Info("Existing friendship record found (ID: {FriendshipId}, Status: {StatusId}). Handling existing friendship logic.", existingFriendship.friendships_id, existingFriendship.status_id);
                return await handleExistingFriendshipAsync(existingFriendship, requester, target);
            }
            else
            {
                logger.Info("No existing friendship record found. Creating new friend request.");
                return await createNewFriendshipAsync(requester, target);
            }

        }


        public async Task<OperationResultDto> respondToFriendRequestAsync(string responderUsername, string requesterUsername, bool accepted)
        {
            logger.Info("respondToFriendRequestAsync called by Responder: {ResponderUsername} for request from: {RequesterUsername}. Accepted: {Accepted}", responderUsername ?? "NULL", requesterUsername ?? "NULL", accepted);
            if (string.IsNullOrWhiteSpace(responderUsername) || string.IsNullOrWhiteSpace(requesterUsername))
            {
                logger.Warn("Respond friend request failed: Responder or requester username is null/whitespace.");
                return new OperationResultDto { Success = false, Message = Lang.ValidationUsernameRequired };
            }

            logger.Debug("Fetching responder ({ResponderUsername}) and requester ({RequesterUsername}) player data.", responderUsername, requesterUsername);
            var responder = await playerRepository.getPlayerByUsernameAsync(responderUsername);
            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);

            if (responder == null || requester == null)
            {
                logger.Warn("Respond friend request failed: Responder (Found={ResponderFound}) or Requester (Found={RequesterFound}) player not found.", responder != null, requester != null);
                return new OperationResultDto { Success = false, Message = Lang.ErrorPlayerNotFound };
            }
            logger.Debug("Both players found. ResponderID: {ResponderId}, RequesterID: {RequesterId}", responder.idPlayer, requester.idPlayer);


            var friendship = await friendshipRepository.findFriendshipAsync(requester.idPlayer, responder.idPlayer);

            if (friendship == null || friendship.status_id != FriendshipStatusConstants.PENDING || friendship.addressee_id != responder.idPlayer)
            {
                logger.Warn("Respond friend request failed: No matching PENDING request found directed to {ResponderUsername} from {RequesterUsername}. (Found={Found}, Status={StatusId}, Addressee={AddresseeId})",
                    responderUsername, requesterUsername, friendship != null, friendship?.status_id, friendship?.addressee_id);
                return new OperationResultDto { Success = false, Message = Lang.ErrorNoPendingRequestFound };
            }
            logger.Debug("Matching pending request found (ID: {FriendshipId}). Updating status.", friendship.friendships_id);

            friendship.status_id = accepted ? FriendshipStatusConstants.ACCEPTED : FriendshipStatusConstants.REJECTED;
            friendshipRepository.updateFriendship(friendship);

            await friendshipRepository.saveChangesAsync();
            logger.Info("Friend request from {RequesterUsername} to {ResponderUsername} processed. Status set to: {Status}", requesterUsername, responderUsername, friendship.status_id);
            return new OperationResultDto { Success = true, Message = accepted ? Lang.FriendRequestAccepted : Lang.FriendRequestRejected };

        }

        public async Task<List<FriendDto>> getFriendsListAsync(string username, ICollection<string> connectedUsernames)
        {
            logger.Info("getFriendsListAsync called for User: {Username}", username ?? "NULL");
            try
            {
                logger.Debug("Fetching player data for {Username}", username);
                var player = await playerRepository.getPlayerByUsernameAsync(username);
                if (player == null)
                {
                    logger.Warn("Get friends list failed: Player {Username} not found.", username ?? "NULL");
                    return new List<FriendDto>();
                }

                logger.Debug("Fetching accepted friendships for PlayerID: {PlayerId}", player.idPlayer);
                var friendships = await friendshipRepository.getAcceptedFriendshipsAsync(player.idPlayer);
                logger.Debug("Found {Count} accepted friendship records.", friendships?.Count ?? 0);

                var onlineUsersSet = connectedUsernames != null
                    ? new HashSet<string>(connectedUsernames, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>();
                var friendDtos = new List<FriendDto>();

                if (friendships != null)
                    foreach (var f in friendships)
                    {
                        var friendDto = mapFriendshipToDto(f, player.idPlayer, onlineUsersSet);
                        if (friendDto != null)
                        {
                            friendDtos.Add(friendDto);
                        }
                    }

                logger.Info("Successfully retrieved {Count} friends for User: {Username}", friendDtos.Count, username);
                return friendDtos;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during getFriendsListAsync for User: {Username}", username ?? "NULL");
                return new List<FriendDto>();
            }
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequestsAsync(string username)
        {
            logger.Info("getFriendRequestsAsync called for User: {Username}", username ?? "NULL");

            logger.Debug("Fetching player data for {Username}", username);
            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                logger.Warn("Get friend requests failed: Player {Username} not found.", username ?? "NULL");
                return new List<FriendRequestInfoDto>();
            }

            logger.Debug("Fetching pending friend requests for PlayerID: {PlayerId}", player.idPlayer);
            var pendingRequests = await friendshipRepository.getPendingFriendRequestsAsync(player.idPlayer);
            logger.Debug("Found {Count} pending friend request records.", pendingRequests?.Count ?? 0);

            var requestInfoDtos = pendingRequests
                .Where(req => req.Player1 != null)
                .Select(req => new FriendRequestInfoDto
                {
                    RequesterUsername = req.Player1.username,
                    RequestDate = req.request_date,
                    AvatarPath = req.Player1.avatar_path ?? "/Resources/Images/Avatar/default_avatar.png"
                })
                .OrderByDescending(r => r.RequestDate)
                .ToList();

            int skippedCount = pendingRequests.Count - requestInfoDtos.Count;
            if (skippedCount > 0)
            {
                logger.Warn("Skipped {Count} pending requests for User {Username} because requester data was null.", skippedCount, username);
            }

            logger.Info("Successfully retrieved {Count} friend requests for User: {Username}", requestInfoDtos.Count, username);
            return requestInfoDtos;

        }

        public async Task<OperationResultDto> removeFriendAsync(string username, string friendToRemoveUsername)
        {
            logger.Info("removeFriendAsync called by User: {Username} to remove Friend: {FriendToRemove}", username ?? "NULL", friendToRemoveUsername ?? "NULL");
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

            logger.Debug("Fetching player ({Username}) and friend ({FriendToRemove}) data.", username, friendToRemoveUsername);
            var player = await playerRepository.getPlayerByUsernameAsync(username);
            var friendToRemove = await playerRepository.getPlayerByUsernameAsync(friendToRemoveUsername);

            if (player == null || friendToRemove == null)
            {
                logger.Warn("Remove friend failed: Player (Found={PlayerFound}) or Friend (Found={FriendFound}) not found.", player != null, friendToRemove != null);
                return new OperationResultDto { Success = false, Message = Lang.ErrorPlayerNotFound };
            }
            logger.Debug("Both players found. PlayerID: {PlayerId}, FriendID: {FriendId}", player.idPlayer, friendToRemove.idPlayer);

            var friendship = await friendshipRepository.findFriendshipAsync(player.idPlayer, friendToRemove.idPlayer);

            if (friendship == null || friendship.status_id != FriendshipStatusConstants.ACCEPTED)
            {
                logger.Warn("Remove friend failed: No ACCEPTED friendship found between {Username} and {FriendToRemove}. (Found={Found}, Status={StatusId})", username, friendToRemoveUsername, friendship != null, friendship?.status_id);
                return new OperationResultDto { Success = false, Message = Lang.ErrorFriendshipNotFound };
            }
            logger.Debug("Accepted friendship found (ID: {FriendshipId}). Removing from DB.", friendship.friendships_id);

            friendshipRepository.removeFriendship(friendship);

            await friendshipRepository.saveChangesAsync();
            logger.Info("Friendship between {Username} and {FriendToRemove} removed successfully.", username, friendToRemoveUsername);
            return new OperationResultDto { Success = true, Message = Lang.FriendRemovedSuccessfully };

        }

        private async Task<OperationResultDto> handleExistingFriendshipAsync(Friendships existingFriendship, Player requester, Player target)
        {
            logger.Debug("Handling existing friendship (ID: {FriendshipId}, Status: {StatusId}) between Requester: {RequesterUsername} and Target: {TargetUsername}",
                existingFriendship.friendships_id, existingFriendship.status_id, requester.username, target.username);
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
                    logger.Info("Found REJECTED friendship. Re-sending request from {RequesterUsername} to {TargetUsername}.", requester.username, target.username);
                    existingFriendship.requester_id = requester.idPlayer;
                    existingFriendship.addressee_id = target.idPlayer;
                    existingFriendship.status_id = FriendshipStatusConstants.PENDING;
                    existingFriendship.request_date = DateTime.UtcNow;
                    friendshipRepository.updateFriendship(existingFriendship);
                    logger.Debug("Saving updated (rejected to pending) friendship request to DB.");
                    await friendshipRepository.saveChangesAsync();
                    logger.Info("Successfully re-sent friend request (updated rejected status to pending).");
                    return new OperationResultDto { Success = true, Message = Lang.FriendRequestSent };

                default:
                    logger.Error("Send request failed: Unknown or unhandled friendship status ({StatusId}) between {RequesterUsername} and {TargetUsername}.", existingFriendship.status_id, requester.username, target.username);
                    return new OperationResultDto { Success = false, Message = Lang.FriendshipStatusPreventsRequest };
            }

        }

        private async Task<OperationResultDto> createNewFriendshipAsync(Player requester, Player target)
        {
            logger.Debug("Creating new PENDING friendship request from RequesterID: {RequesterId} to TargetID: {TargetId}", requester.idPlayer, target.idPlayer);
            var newFriendship = new Friendships
            {
                requester_id = requester.idPlayer,
                addressee_id = target.idPlayer,
                request_date = DateTime.UtcNow,
                status_id = FriendshipStatusConstants.PENDING
            };
            friendshipRepository.addFriendship(newFriendship);


            logger.Debug("Saving new friendship request to DB.");
            await friendshipRepository.saveChangesAsync();
            if (newFriendship.friendships_id > 0)
            {
                logger.Info("New friendship request (ID: {FriendshipId}) created successfully.", newFriendship.friendships_id);
            }
            else
            {
                logger.Warn("New friendship request saved, but ID was not assigned or is zero.");
            }
            return new OperationResultDto { Success = true, Message = Lang.FriendRequestSent };

        }

        private FriendDto mapFriendshipToDto(Friendships f, int ownPlayerId, HashSet<string> onlineUsersSet)
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
                    AvatarPath = friendEntity.avatar_path ?? "/Resources/Images/Avatar/default_avatar.png"
                };
            }
            else
            {
                logger.Warn(
                    "Friend entity was null for FriendshipID: {FriendshipId}, FriendPlayerID: {FriendId}. Skipping.",
                    (object)f.friendships_id, (object)friendId);
                return null;
            }
        }
    }
}