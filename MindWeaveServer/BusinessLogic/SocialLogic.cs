using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources; // For Lang messages
using MindWeaveServer.Utilities; // For FriendshipStatusConstants
using System;
using System.Collections.Generic;
using System.Data.Entity; // Required for Include extension method
using System.Linq;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Social;

namespace MindWeaveServer.BusinessLogic
{
    public class SocialLogic
    {
        private readonly IPlayerRepository playerRepository;
        private readonly IFriendshipRepository friendshipRepository;

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
                // Should not happen if the requester is logged in, but good to check
                return new List<PlayerSearchResultDto>();
            }

            // Find players matching the query, excluding the requester
            // NOTE: Using DbContext directly here as PlayerRepository doesn't have a search method.
            // Consider adding a search method to IPlayerRepository for better abstraction.
            using (var context = new MindWeaveDBEntities1()) // Ideally inject context or use repository method
            {
                var potentialMatches = await context.Player
                    .Where(p => p.username.Contains(query) && p.idPlayer != requester.idPlayer)
                    .Select(p => new { p.idPlayer, p.username, p.avatar_path }) // Select only needed fields
                    .Take(10) // Limit results for performance
                    .ToListAsync();

                var results = new List<PlayerSearchResultDto>();
                foreach (var potentialMatch in potentialMatches)
                {
                    // Check if there's an existing friendship (any status)
                    var existingFriendship =
                        await friendshipRepository.findFriendshipAsync(requester.idPlayer, potentialMatch.idPlayer);
                    if (existingFriendship == null) // Only add if no relationship exists
                    {
                        results.Add(new PlayerSearchResultDto
                        {
                            username = potentialMatch.username,
                            avatarPath = potentialMatch.avatar_path // Assuming PlayerSearchResultDto has this
                            // Add other fields if needed
                        });
                    }
                    // Optionally: could add logic here to show relationship status (e.g., "Pending", "Already Friends")
                }

                return results;
            }
        }

        /// <summary>
        /// Sends a friend request from requester to target.
        /// </summary>
        public async Task<OperationResultDto> sendFriendRequestAsync(string requesterUsername, string targetUsername)
        {
            if (string.IsNullOrWhiteSpace(requesterUsername) || string.IsNullOrWhiteSpace(targetUsername))
            {
                return new OperationResultDto { success = false, message = "Usernames cannot be empty." };
            }

            if (requesterUsername.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
            {
                return new OperationResultDto
                    { success = false, message = "Cannot send a friend request to yourself." };
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
                // Handle existing relationship based on status
                switch (existingFriendship.status_id)
                {
                    case FriendshipStatusConstants.ACCEPTED:
                        return new OperationResultDto
                            { success = false, message = "You are already friends with this player." };
                    case FriendshipStatusConstants.PENDING:
                        // If the current user sent the request, it's already pending.
                        // If the other user sent it, the current user should accept/reject it.
                        if (existingFriendship.requester_id == requester.idPlayer)
                            return new OperationResultDto
                                { success = false, message = "Friend request already sent and pending." };
                        else
                            return new OperationResultDto
                                { success = false, message = "This player has already sent you a friend request." };
                    case FriendshipStatusConstants.REJECTED:
                        // Maybe allow resending after some time? For now, just indicate it was rejected.
                        // Or update the existing rejected request to pending again. Let's update it.
                        existingFriendship.requester_id =
                            requester.idPlayer; // Ensure the sender is correct for the new request
                        existingFriendship.addressee_id = target.idPlayer;
                        existingFriendship.status_id = FriendshipStatusConstants.PENDING;
                        existingFriendship.request_date = DateTime.UtcNow;
                        friendshipRepository.updateFriendship(existingFriendship);
                        await friendshipRepository.saveChangesAsync();
                        // TODO: Notify target user via callback
                        return new OperationResultDto { success = true, message = "Friend request sent." };

                    // Add cases for BLOCKED or other statuses if necessary
                    default:
                        return new OperationResultDto
                        {
                            success = false,
                            message = "Cannot send friend request due to an existing relationship status."
                        };
                }
            }

            // Create new friendship request
            var newFriendship = new Friendships
            {
                requester_id = requester.idPlayer,
                addressee_id = target.idPlayer,
                request_date = DateTime.UtcNow,
                status_id = FriendshipStatusConstants.PENDING // Pending
            };

            friendshipRepository.addFriendship(newFriendship);
            await friendshipRepository.saveChangesAsync();

            // TODO: Notify target user via callback using ISocialCallback

            return new OperationResultDto { success = true, message = "Friend request sent." };
        }

        /// <summary>
        /// Responds to a pending friend request.
        /// </summary>
        public async Task<OperationResultDto> respondToFriendRequestAsync(string responderUsername,
            string requesterUsername, bool accepted)
        {
            if (string.IsNullOrWhiteSpace(responderUsername) || string.IsNullOrWhiteSpace(requesterUsername))
            {
                return new OperationResultDto { success = false, message = "Usernames cannot be empty." };
            }

            var responder = await playerRepository.getPlayerByUsernameAsync(responderUsername);
            var requester = await playerRepository.getPlayerByUsernameAsync(requesterUsername);

            if (responder == null || requester == null)
            {
                return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
            }

            // Find the PENDING request where the responder is the addressee
            var friendship = await friendshipRepository.findFriendshipAsync(requester.idPlayer, responder.idPlayer);

            if (friendship == null || friendship.status_id != FriendshipStatusConstants.PENDING ||
                friendship.addressee_id != responder.idPlayer)
            {
                return new OperationResultDto
                    { success = false, message = "No pending friend request found from this user." };
            }

            friendship.status_id = accepted ? FriendshipStatusConstants.ACCEPTED : FriendshipStatusConstants.REJECTED;
            // Optionally update request_date to reflect response time? No, keep original request date.

            friendshipRepository.updateFriendship(friendship); // Mark for update
            await friendshipRepository.saveChangesAsync();

            // TODO: Notify requester user via callback using ISocialCallback (accepted/rejected)

            return new OperationResultDto
                { success = true, message = accepted ? "Friend request accepted." : "Friend request rejected." };
        }

        /// <summary>
        /// Gets the list of accepted friends for a user.
        /// </summary>
        public async Task<List<FriendDto>> getFriendsListAsync(string username)
        {
            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                return new List<FriendDto>(); // Or throw exception/return error DTO
            }

            var friendships = await friendshipRepository.getAcceptedFriendshipsAsync(player.idPlayer);

            // TODO: Need a way to check online status (likely involves another service/cache)
            // For now, assume everyone is offline.
            bool isOnlinePlaceholder = false;

            var friendDtos = friendships.Select(f =>
            {
                // Determine who the friend is in the relationship
                var friendEntity = f.requester_id == player.idPlayer ? f.Player1 : f.Player;
                return new FriendDto
                {
                    username = friendEntity.username,
                    isOnline = isOnlinePlaceholder // Replace with actual online status check
                    // avatarPath = friendEntity.avatar_path // Add if needed in FriendDto
                };
            }).ToList();

            return friendDtos;
        }

        /// <summary>
        /// Gets the list of pending friend requests received by a user.
        /// </summary>
        public async Task<List<FriendRequestInfoDto>> getFriendRequestsAsync(string username)
        {
            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                return new List<FriendRequestInfoDto>();
            }

            var pendingRequests = await friendshipRepository.getPendingFriendRequestsAsync(player.idPlayer);

            var requestInfoDtos = pendingRequests.Select(req => new FriendRequestInfoDto
            {
                requesterUsername = req.Player1.username, // Player1 is the requester
                requestDate = req.request_date
            }).ToList();

            return requestInfoDtos;
        }

        /// <summary>
        /// Removes a friend relationship.
        /// </summary>
        public async Task<OperationResultDto> removeFriendAsync(string username, string friendToRemoveUsername)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(friendToRemoveUsername))
            {
                return new OperationResultDto { success = false, message = "Usernames cannot be empty." };
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
                return new OperationResultDto { success = false, message = "You are not friends with this player." };
            }

            // Option 1: Delete the record
            friendshipRepository.removeFriendship(friendship);

            // Option 2: Set status to something like 'Removed' (if you want to keep history)
            // friendship.status_id = FriendshipStatusConstants.REMOVED; // Assuming REMOVED = 5 or similar
            // friendshipRepository.updateFriendship(friendship);

            await friendshipRepository.saveChangesAsync();

            // TODO: Notify the removed friend via callback? (Optional)

            return new OperationResultDto { success = true, message = "Friend removed successfully." };
        }

    }
}

