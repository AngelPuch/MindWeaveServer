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
using NLog; // Using para NLog

namespace MindWeaveServer.BusinessLogic
{
    public class SocialLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger(); // Logger NLog

        private readonly IPlayerRepository playerRepository;
        private readonly IFriendshipRepository friendshipRepository;
        private const int SEARCH_RESULT_LIMIT = 10;

        public SocialLogic(IPlayerRepository playerRepo, IFriendshipRepository friendshipRepo)
        {
            this.playerRepository = playerRepo ?? throw new ArgumentNullException(nameof(playerRepo));
            this.friendshipRepository = friendshipRepo ?? throw new ArgumentNullException(nameof(friendshipRepo));
            logger.Info("SocialLogic instance created."); // Log añadido
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayersAsync(string requesterUsername, string query)
        {
            logger.Info("searchPlayersAsync called by User: {RequesterUsername}, Query: '{Query}'", requesterUsername ?? "NULL", query ?? "NULL"); // Log añadido
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(requesterUsername))
            {
                logger.Warn("Search players ignored: Query or requester username is null/whitespace."); // Log añadido
                return new List<PlayerSearchResultDto>();
            }

            try
            {
                logger.Debug("Fetching requester player data for search: {RequesterUsername}", requesterUsername); // Log añadido
                var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);
                if (requester == null)
                {
                    logger.Warn("Search players failed: Requester player {RequesterUsername} not found.", requesterUsername); // Log añadido
                    return new List<PlayerSearchResultDto>();
                }

                logger.Debug("Performing player search in repository. RequesterID: {PlayerId}, Query: '{Query}', Limit: {Limit}", requester.idPlayer, query, SEARCH_RESULT_LIMIT); // Log añadido
                var results = await playerRepository.searchPlayersAsync(requester.idPlayer, query, SEARCH_RESULT_LIMIT);
                logger.Info("Player search completed for User: {RequesterUsername}, Query: '{Query}'. Found {Count} results.", requesterUsername, query, results?.Count ?? 0); // Log añadido
                return results ?? new List<PlayerSearchResultDto>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during searchPlayersAsync for User: {RequesterUsername}, Query: '{Query}'", requesterUsername ?? "NULL", query ?? "NULL"); // Log añadido
                return new List<PlayerSearchResultDto>();
            }
        }

        public async Task<OperationResultDto> sendFriendRequestAsync(string requesterUsername, string targetUsername)
        {
            logger.Info("sendFriendRequestAsync called from User: {RequesterUsername} to Target: {TargetUsername}", requesterUsername ?? "NULL", targetUsername ?? "NULL"); // Log añadido
            if (string.IsNullOrWhiteSpace(requesterUsername) || string.IsNullOrWhiteSpace(targetUsername))
            {
                logger.Warn("Send friend request failed: Requester or target username is null/whitespace."); // Log añadido
                return new OperationResultDto { success = false, message = Lang.ValidationUsernameRequired };
            }

            if (requesterUsername.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("Send friend request failed: User {Username} attempted to send request to self.", requesterUsername); // Log añadido
                return new OperationResultDto { success = false, message = Lang.ErrorCannotSelfFriend };
            }

            try // Añadido try-catch general aquí por si fallan las búsquedas iniciales
            {
                logger.Debug("Fetching requester ({RequesterUsername}) and target ({TargetUsername}) player data.", requesterUsername, targetUsername); // Log añadido
                var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);
                var target = await playerRepository.getPlayerByUsernameAsync(targetUsername);

                if (requester == null || target == null)
                {
                    logger.Warn("Send friend request failed: Requester (Found={RequesterFound}) or Target (Found={TargetFound}) player not found.", requester != null, target != null); // Log añadido
                    return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
                }
                logger.Debug("Both players found. RequesterID: {RequesterId}, TargetID: {TargetId}", requester.idPlayer, target.idPlayer); // Log añadido

                logger.Debug("Checking for existing friendship between PlayerID {RequesterId} and PlayerID {TargetId}", requester.idPlayer, target.idPlayer); // Log añadido
                var existingFriendship = await friendshipRepository.findFriendshipAsync(requester.idPlayer, target.idPlayer);

                if (existingFriendship != null)
                {
                    logger.Info("Existing friendship record found (ID: {FriendshipId}, Status: {StatusId}). Handling existing friendship logic.", existingFriendship.friendships_id, existingFriendship.status_id); // Log añadido
                    return await handleExistingFriendshipAsync(existingFriendship, requester, target); // Logs internos en helper
                }
                else
                {
                    logger.Info("No existing friendship record found. Creating new friend request."); // Log añadido
                    return await createNewFriendshipAsync(requester, target); // Logs internos en helper
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Unhandled exception during sendFriendRequestAsync from {RequesterUsername} to {TargetUsername} (likely during player/friendship lookup)", requesterUsername ?? "NULL", targetUsername ?? "NULL"); // Log añadido
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }


        public async Task<OperationResultDto> respondToFriendRequestAsync(string responderUsername, string requesterUsername, bool accepted)
        {
            logger.Info("respondToFriendRequestAsync called by Responder: {ResponderUsername} for request from: {RequesterUsername}. Accepted: {Accepted}", responderUsername ?? "NULL", requesterUsername ?? "NULL", accepted); // Log añadido
            if (string.IsNullOrWhiteSpace(responderUsername) || string.IsNullOrWhiteSpace(requesterUsername))
            {
                logger.Warn("Respond friend request failed: Responder or requester username is null/whitespace."); // Log añadido
                return new OperationResultDto { success = false, message = Lang.ValidationUsernameRequired };
            }

            try
            {
                logger.Debug("Fetching responder ({ResponderUsername}) and requester ({RequesterUsername}) player data.", responderUsername, requesterUsername); // Log añadido
                var responder = await playerRepository.getPlayerByUsernameAsync(responderUsername);
                var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);

                if (responder == null || requester == null)
                {
                    logger.Warn("Respond friend request failed: Responder (Found={ResponderFound}) or Requester (Found={RequesterFound}) player not found.", responder != null, requester != null); // Log añadido
                    return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
                }
                logger.Debug("Both players found. ResponderID: {ResponderId}, RequesterID: {RequesterId}", responder.idPlayer, requester.idPlayer); // Log añadido


                logger.Debug("Searching for PENDING friendship request from RequesterID {RequesterId} to ResponderID {ResponderId}", requester.idPlayer, responder.idPlayer); // Log añadido
                var friendship = await friendshipRepository.findFriendshipAsync(requester.idPlayer, responder.idPlayer);

                if (friendship == null || friendship.status_id != FriendshipStatusConstants.PENDING || friendship.addressee_id != responder.idPlayer)
                {
                    logger.Warn("Respond friend request failed: No matching PENDING request found directed to {ResponderUsername} from {RequesterUsername}. (Found={Found}, Status={StatusId}, Addressee={AddresseeId})",
                        responderUsername, requesterUsername, friendship != null, friendship?.status_id, friendship?.addressee_id); // Log añadido
                    return new OperationResultDto { success = false, message = Lang.ErrorNoPendingRequestFound };
                }
                logger.Debug("Matching pending request found (ID: {FriendshipId}). Updating status.", friendship.friendships_id); // Log añadido

                friendship.status_id = accepted ? FriendshipStatusConstants.ACCEPTED : FriendshipStatusConstants.REJECTED;
                friendshipRepository.updateFriendship(friendship);

                logger.Debug("Saving friendship status change to DB."); // Log añadido
                await friendshipRepository.saveChangesAsync();
                logger.Info("Friend request from {RequesterUsername} to {ResponderUsername} processed. Status set to: {Status}", requesterUsername, responderUsername, friendship.status_id); // Log añadido
                return new OperationResultDto { success = true, message = accepted ? Lang.FriendRequestAccepted : Lang.FriendRequestRejected };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during respondToFriendRequestAsync for Responder: {ResponderUsername}, Requester: {RequesterUsername}", responderUsername ?? "NULL", requesterUsername ?? "NULL"); // Log añadido
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<List<FriendDto>> getFriendsListAsync(string username, ICollection<string> connectedUsernames)
        {
            logger.Info("getFriendsListAsync called for User: {Username}", username ?? "NULL"); // Log añadido
            try
            {
                logger.Debug("Fetching player data for {Username}", username); // Log añadido
                var player = await playerRepository.getPlayerByUsernameAsync(username);
                if (player == null)
                {
                    logger.Warn("Get friends list failed: Player {Username} not found.", username ?? "NULL"); // Log añadido
                    return new List<FriendDto>();
                }

                logger.Debug("Fetching accepted friendships for PlayerID: {PlayerId}", player.idPlayer); // Log añadido
                var friendships = await friendshipRepository.getAcceptedFriendshipsAsync(player.idPlayer);
                logger.Debug("Found {Count} accepted friendship records.", friendships?.Count ?? 0); // Log añadido

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
                    else
                    {
                        logger.Warn("Friend entity was null for FriendshipID: {FriendshipId}, FriendPlayerID: {FriendId}. Skipping.", f.friendships_id, friendId); // Log añadido
                    }
                }
                logger.Info("Successfully retrieved {Count} friends for User: {Username}", friendDtos.Count, username); // Log añadido
                return friendDtos;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during getFriendsListAsync for User: {Username}", username ?? "NULL"); // Log añadido
                return new List<FriendDto>();
            }
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequestsAsync(string username)
        {
            logger.Info("getFriendRequestsAsync called for User: {Username}", username ?? "NULL"); // Log añadido
            try
            {
                logger.Debug("Fetching player data for {Username}", username); // Log añadido
                var player = await playerRepository.getPlayerByUsernameAsync(username);
                if (player == null)
                {
                    logger.Warn("Get friend requests failed: Player {Username} not found.", username ?? "NULL"); // Log añadido
                    return new List<FriendRequestInfoDto>();
                }

                logger.Debug("Fetching pending friend requests for PlayerID: {PlayerId}", player.idPlayer); // Log añadido
                var pendingRequests = await friendshipRepository.getPendingFriendRequestsAsync(player.idPlayer);
                logger.Debug("Found {Count} pending friend request records.", pendingRequests?.Count ?? 0); // Log añadido

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

                int skippedCount = pendingRequests.Count - requestInfoDtos.Count;
                if (skippedCount > 0)
                {
                    logger.Warn("Skipped {Count} pending requests for User {Username} because requester data was null.", skippedCount, username); // Log añadido
                }

                logger.Info("Successfully retrieved {Count} friend requests for User: {Username}", requestInfoDtos.Count, username); // Log añadido
                return requestInfoDtos;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during getFriendRequestsAsync for User: {Username}", username ?? "NULL"); // Log añadido
                return new List<FriendRequestInfoDto>();
            }
        }

        public async Task<OperationResultDto> removeFriendAsync(string username, string friendToRemoveUsername)
        {
            logger.Info("removeFriendAsync called by User: {Username} to remove Friend: {FriendToRemove}", username ?? "NULL", friendToRemoveUsername ?? "NULL"); // Log añadido
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(friendToRemoveUsername))
            {
                logger.Warn("Remove friend failed: Username or friend username is null/whitespace."); // Log añadido
                return new OperationResultDto { success = false, message = Lang.ValidationUsernameRequired };
            }
            if (username.Equals(friendToRemoveUsername, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("Remove friend failed: User {Username} attempted to remove self.", username); // Log añadido
                return new OperationResultDto { success = false, message = Lang.ErrorCannotRemoveSelf };
            }

            try
            {
                logger.Debug("Fetching player ({Username}) and friend ({FriendToRemove}) data.", username, friendToRemoveUsername); // Log añadido
                var player = await playerRepository.getPlayerByUsernameAsync(username);
                var friendToRemove = await playerRepository.getPlayerByUsernameAsync(friendToRemoveUsername);

                if (player == null || friendToRemove == null)
                {
                    logger.Warn("Remove friend failed: Player (Found={PlayerFound}) or Friend (Found={FriendFound}) not found.", player != null, friendToRemove != null); // Log añadido
                    return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
                }
                logger.Debug("Both players found. PlayerID: {PlayerId}, FriendID: {FriendId}", player.idPlayer, friendToRemove.idPlayer); // Log añadido

                logger.Debug("Searching for ACCEPTED friendship between PlayerID {PlayerId} and FriendID {FriendId}", player.idPlayer, friendToRemove.idPlayer); // Log añadido
                var friendship = await friendshipRepository.findFriendshipAsync(player.idPlayer, friendToRemove.idPlayer);

                if (friendship == null || friendship.status_id != FriendshipStatusConstants.ACCEPTED)
                {
                    logger.Warn("Remove friend failed: No ACCEPTED friendship found between {Username} and {FriendToRemove}. (Found={Found}, Status={StatusId})", username, friendToRemoveUsername, friendship != null, friendship?.status_id); // Log añadido
                    return new OperationResultDto { success = false, message = Lang.ErrorFriendshipNotFound };
                }
                logger.Debug("Accepted friendship found (ID: {FriendshipId}). Removing from DB.", friendship.friendships_id); // Log añadido

                friendshipRepository.removeFriendship(friendship);

                logger.Debug("Saving friendship removal to DB."); // Log añadido
                await friendshipRepository.saveChangesAsync();
                logger.Info("Friendship between {Username} and {FriendToRemove} removed successfully.", username, friendToRemoveUsername); // Log añadido
                return new OperationResultDto { success = true, message = Lang.FriendRemovedSuccessfully };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during removeFriendAsync for User: {Username}, Friend: {FriendToRemove}", username ?? "NULL", friendToRemoveUsername ?? "NULL"); // Log añadido
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        private async Task<OperationResultDto> handleExistingFriendshipAsync(Friendships existingFriendship, Player requester, Player target)
        {
            logger.Debug("Handling existing friendship (ID: {FriendshipId}, Status: {StatusId}) between Requester: {RequesterUsername} and Target: {TargetUsername}",
                existingFriendship.friendships_id, existingFriendship.status_id, requester.username, target.username); // Log añadido
            try
            {
                switch (existingFriendship.status_id)
                {
                    case FriendshipStatusConstants.ACCEPTED:
                        logger.Warn("Send request failed: Friendship already exists and is ACCEPTED."); // Log añadido
                        return new OperationResultDto { success = false, message = Lang.FriendshipAlreadyExists };

                    case FriendshipStatusConstants.PENDING:
                        if (existingFriendship.requester_id == requester.idPlayer)
                        {
                            logger.Warn("Send request failed: A PENDING request from {RequesterUsername} to {TargetUsername} already exists.", requester.username, target.username); // Log añadido
                            return new OperationResultDto { success = false, message = Lang.FriendRequestAlreadySent };
                        }
                        else
                        {
                            logger.Warn("Send request failed: A PENDING request from {TargetUsername} to {RequesterUsername} already exists. User should respond instead.", target.username, requester.username); // Log añadido
                            return new OperationResultDto { success = false, message = Lang.FriendRequestReceivedFromUser };
                        }

                    case FriendshipStatusConstants.REJECTED:
                        logger.Info("Found REJECTED friendship. Re-sending request from {RequesterUsername} to {TargetUsername}.", requester.username, target.username); // Log añadido
                        existingFriendship.requester_id = requester.idPlayer;
                        existingFriendship.addressee_id = target.idPlayer;
                        existingFriendship.status_id = FriendshipStatusConstants.PENDING;
                        existingFriendship.request_date = DateTime.UtcNow;
                        friendshipRepository.updateFriendship(existingFriendship);
                        logger.Debug("Saving updated (rejected to pending) friendship request to DB."); // Log añadido
                        await friendshipRepository.saveChangesAsync();
                        logger.Info("Successfully re-sent friend request (updated rejected status to pending)."); // Log añadido
                        return new OperationResultDto { success = true, message = Lang.FriendRequestSent };

                    default:
                        logger.Error("Send request failed: Unknown or unhandled friendship status ({StatusId}) between {RequesterUsername} and {TargetUsername}.", existingFriendship.status_id, requester.username, target.username); // Log añadido
                        return new OperationResultDto { success = false, message = Lang.FriendshipStatusPreventsRequest };
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception saving updated (rejected to pending) friendship request."); // Log añadido
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        private async Task<OperationResultDto> createNewFriendshipAsync(Player requester, Player target)
        {
            logger.Debug("Creating new PENDING friendship request from RequesterID: {RequesterId} to TargetID: {TargetId}", requester.idPlayer, target.idPlayer); // Log añadido
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
                logger.Debug("Saving new friendship request to DB."); // Log añadido
                await friendshipRepository.saveChangesAsync();
                // Verificar si se asignó un ID después de guardar
                if (newFriendship.friendships_id > 0)
                {
                    logger.Info("New friendship request (ID: {FriendshipId}) created successfully.", newFriendship.friendships_id); // Log añadido
                }
                else
                {
                    logger.Warn("New friendship request saved, but ID was not assigned or is zero."); // Log añadido
                }
                return new OperationResultDto { success = true, message = Lang.FriendRequestSent };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception saving new friendship request from {RequesterUsername} to {TargetUsername}", requester.username, target.username); // Log añadido
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }
    }
}