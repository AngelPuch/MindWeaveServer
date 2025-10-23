using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities; 
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
                return new List<PlayerSearchResultDto>();
            }

            using (var context = new MindWeaveDBEntities1()) 
            {
                var potentialMatchIds = await context.Player
                    .Where(p => p.username.Contains(query) && p.idPlayer != requester.idPlayer)
                    .Select(p => p.idPlayer)
                    .Take(20) 
                    .ToListAsync();

                if (!potentialMatchIds.Any())
                {
                    return new List<PlayerSearchResultDto>();
                }

                var existingRelationshipIds = await context.Friendships
                    .Where(f => (f.requester_id == requester.idPlayer && potentialMatchIds.Contains(f.addressee_id)) ||
                                (f.addressee_id == requester.idPlayer && potentialMatchIds.Contains(f.requester_id)))
                    .Where(f => f.status_id == FriendshipStatusConstants.PENDING || f.status_id == FriendshipStatusConstants.ACCEPTED)
                    .Select(f => f.requester_id == requester.idPlayer ? f.addressee_id : f.requester_id) 
                    .Distinct()
                    .ToListAsync();

                var validResultIds = potentialMatchIds.Except(existingRelationshipIds).ToList();

                if (!validResultIds.Any())
                {
                    return new List<PlayerSearchResultDto>();
                }

                var finalResults = await context.Player
                    .Where(p => validResultIds.Contains(p.idPlayer))
                     .Select(p => new PlayerSearchResultDto 
                     {
                         username = p.username,
                         avatarPath = p.avatar_path ?? "/Resources/Images/Avatar/default_avatar.png"
                     })
                    .Take(10) 
                    .ToListAsync();

                return finalResults;
            }
        }

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
                switch (existingFriendship.status_id)
                {
                    case FriendshipStatusConstants.ACCEPTED:
                        return new OperationResultDto
                        { success = false, message = "You are already friends with this player." };
                    case FriendshipStatusConstants.PENDING:
                        if (existingFriendship.requester_id == requester.idPlayer)
                            return new OperationResultDto
                            { success = false, message = "Friend request already sent and pending." };
                        else // The target sent the request to the requester
                            return new OperationResultDto
                            { success = false, message = "This player has already sent you a friend request. Respond to it instead." }; // Mensaje más claro
                    case FriendshipStatusConstants.REJECTED:
                        existingFriendship.requester_id = requester.idPlayer;
                        existingFriendship.addressee_id = target.idPlayer;
                        existingFriendship.status_id = FriendshipStatusConstants.PENDING; 
                        existingFriendship.request_date = DateTime.UtcNow; 
                        friendshipRepository.updateFriendship(existingFriendship);
                        await friendshipRepository.saveChangesAsync();
                        // TODO: Notify target user via callback
                        return new OperationResultDto { success = true, message = "Friend request sent." }; 

                    default:
                        return new OperationResultDto
                        {
                            success = false,
                            message = "Cannot send friend request due to an existing relationship status."
                        };
                }
            }

            var newFriendship = new Friendships
            {
                requester_id = requester.idPlayer,
                addressee_id = target.idPlayer,
                request_date = DateTime.UtcNow,
                status_id = FriendshipStatusConstants.PENDING 
            };

            friendshipRepository.addFriendship(newFriendship);
            await friendshipRepository.saveChangesAsync();

            // TODO: Notify target user via callback using ISocialCallback

            return new OperationResultDto { success = true, message = "Friend request sent." };
        }

      
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

            var friendship = await friendshipRepository.findFriendshipAsync(requester.idPlayer, responder.idPlayer);

            if (friendship == null || friendship.status_id != FriendshipStatusConstants.PENDING ||
                friendship.addressee_id != responder.idPlayer)
            {
                return new OperationResultDto
                { success = false, message = "No pending friend request found from this user to respond to." }; 
            }

            friendship.status_id = accepted ? FriendshipStatusConstants.ACCEPTED : FriendshipStatusConstants.REJECTED;

            friendshipRepository.updateFriendship(friendship); 
            await friendshipRepository.saveChangesAsync();

            // TODO: Notify requester user via callback using ISocialCallback (accepted/rejected)

            return new OperationResultDto
            { success = true, message = accepted ? "Friend request accepted." : "Friend request rejected." };
        }

        public async Task<List<FriendDto>> getFriendsListAsync(string username, ICollection<string> connectedUsernames)
        {
            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                Console.WriteLine($"[getFriendsListAsync] Error: Player '{username}' not found.");
                return new List<FriendDto>();
            }

            var friendships = await friendshipRepository.getAcceptedFriendshipsAsync(player.idPlayer);
            Console.WriteLine($"[getFriendsListAsync] Found {friendships.Count} accepted friendships for {username} (PlayerID: {player.idPlayer}).");


            var onlineUsersSet = connectedUsernames != null
                                    ? new HashSet<string>(connectedUsernames, StringComparer.OrdinalIgnoreCase)
                                    : new HashSet<string>();

            var friendDtos = new List<FriendDto>();
            foreach (var f in friendships)
            {
                int friendId = (f.requester_id == player.idPlayer) ? f.addressee_id : f.requester_id;

                Player friendEntity = null;
                if (f.Player != null && f.Player.idPlayer == friendId)
                {
                    friendEntity = f.Player;
                }
                else if (f.Player1 != null && f.Player1.idPlayer == friendId)
                {
                    friendEntity = f.Player1;
                }

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
                    Console.WriteLine($"[getFriendsListAsync] Warning: Could not find friend entity for friendship ID {f.friendships_id}. Friend ID sought: {friendId}. f.Player?.idPlayer={f.Player?.idPlayer}, f.Player1?.idPlayer={f.Player1?.idPlayer}");
                }
            }

            Console.WriteLine($"[getFriendsListAsync] Returning {friendDtos.Count} friends for {username}.");
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


                }).ToList();

            return requestInfoDtos;
        }

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

            friendshipRepository.removeFriendship(friendship);
            await friendshipRepository.saveChangesAsync();

            // TODO: Notify the removed friend via callback

            return new OperationResultDto { success = true, message = "Friend removed successfully." };
        }
    }
}