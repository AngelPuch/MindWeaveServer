using Moq;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities;

namespace MindWeaveServer.Tests.BusinessLogic
{
    public class SocialLogicTests
    {
        private readonly Mock<IPlayerRepository> mockPlayerRepository;
        private readonly Mock<IFriendshipRepository> mockFriendshipRepository;

        private readonly SocialLogic socialLogic;

        private readonly Player player1;
        private readonly Player player2;
        private readonly Player player3;
        private readonly string defaultAvatar = "/Resources/Images/Avatar/default_avatar.png";

        public SocialLogicTests()
        {
            mockPlayerRepository = new Mock<IPlayerRepository>();
            mockFriendshipRepository = new Mock<IFriendshipRepository>();

            socialLogic = new SocialLogic(mockPlayerRepository.Object, mockFriendshipRepository.Object);

            player1 = new Player { idPlayer = 1, username = "UserOne", email = "one@example.com", avatar_path = "/path/avatar1.png" };
            player2 = new Player { idPlayer = 2, username = "UserTwo", email = "two@example.com", avatar_path = null };
            player3 = new Player { idPlayer = 3, username = "UserThree", email = "three@example.com", avatar_path = "/path/avatar3.png" };

            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(player1.username)).ReturnsAsync(player1);
            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(player2.username)).ReturnsAsync(player2);
            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(player3.username)).ReturnsAsync(player3);
            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(It.Is<string>(s => 
                    s != player1.username && s != player2.username && s != player3.username))).ReturnsAsync((Player)null);
        }


        [Fact]
        public async Task SearchPlayersAsync_WithValidQueryAndRequester_ShouldReturnMatchingUsersExcludingSelfAndFriends()
        {
            string requesterUsername = player1.username;
            string query = "User";
            var searchResultsFromRepo = new List<PlayerSearchResultDto> {
                new PlayerSearchResultDto { username = player2.username, avatarPath = defaultAvatar },
                new PlayerSearchResultDto { username = player3.username, avatarPath = player3.avatar_path }
            };
            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(requesterUsername)).ReturnsAsync(player1);
            mockPlayerRepository.Setup(r => r.SearchPlayersAsync(player1.idPlayer, query, It.IsAny<int>()))
                                 .ReturnsAsync(searchResultsFromRepo);

            var results = await socialLogic.searchPlayersAsync(requesterUsername, query);

            Assert.NotNull(results);
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.username == player2.username && r.avatarPath == defaultAvatar);
            Assert.Contains(results, r => r.username == player3.username && r.avatarPath == player3.avatar_path);
            mockPlayerRepository.Verify(r => r.SearchPlayersAsync(player1.idPlayer, query, It.IsAny<int>()), Times.Once);
        }

        [Fact]
        public async Task SearchPlayersAsync_WhenRequesterNotFound_ShouldReturnEmptyList()
        {
            string requesterUsername = "NonExistentUser";
            string query = "User";
            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(requesterUsername)).ReturnsAsync((Player)null);

            var results = await socialLogic.searchPlayersAsync(requesterUsername, query);

            Assert.NotNull(results);
            Assert.Empty(results);
            mockPlayerRepository.Verify(r => r.SearchPlayersAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never); // No busca si el requester no existe
        }

        [Fact]
        public async Task SearchPlayersAsync_WhenRepositoryThrowsException_ShouldReturnEmptyList()
        {
            string requesterUsername = player1.username;
            string query = "User";
            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(requesterUsername)).ReturnsAsync(player1);
            mockPlayerRepository.Setup(r => r.SearchPlayersAsync(player1.idPlayer, query, It.IsAny<int>()))
                                 .ThrowsAsync(new Exception("Database connection failed"));

            var results = await socialLogic.searchPlayersAsync(requesterUsername, query);

            Assert.NotNull(results);
            Assert.Empty(results); 
        }

        [Fact]
        public async Task SendFriendRequestAsync_ToNewUser_ShouldReturnSuccessAndAddFriendship()
        {
            string requesterUsername = player1.username;
            string targetUsername = player2.username;

            mockFriendshipRepository.Setup(fr => fr.findFriendshipAsync(player1.idPlayer, player2.idPlayer)).ReturnsAsync((Friendships)null);
            mockFriendshipRepository.Setup(fr => fr.saveChangesAsync()).ReturnsAsync(1);

            var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);

            Assert.True(result.success);
            Assert.Equal(Lang.FriendRequestSent, result.message);
            mockFriendshipRepository.Verify(fr => fr.saveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task SendFriendRequestAsync_ToSelf_ShouldReturnFailure()
        {
            string requesterUsername = player1.username;
            string targetUsername = player1.username;

            var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);

            Assert.False(result.success);
            Assert.Equal(Lang.ErrorCannotSelfFriend, result.message);
            mockFriendshipRepository.Verify(fr => fr.findFriendshipAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            mockFriendshipRepository.Verify(fr => fr.addFriendship(It.IsAny<Friendships>()), Times.Never);
            mockFriendshipRepository.Verify(fr => fr.saveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task SendFriendRequestAsync_WhenTargetNotFound_ShouldReturnFailure()
        {
            string requesterUsername = player1.username;
            string targetUsername = "NotFoundUser";
            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(targetUsername)).ReturnsAsync((Player)null);

            var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);

            Assert.False(result.success);
            Assert.Equal(Lang.ErrorPlayerNotFound, result.message);
            mockFriendshipRepository.Verify(fr => fr.findFriendshipAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task SendFriendRequestAsync_WhenAlreadyFriends_ShouldReturnFailure()
        {
            string requesterUsername = player1.username;
            string targetUsername = player2.username;
            var existingFriendship = new Friendships { requester_id = player1.idPlayer, addressee_id = player2.idPlayer, status_id = FriendshipStatusConstants.ACCEPTED };
            mockFriendshipRepository.Setup(fr => fr.findFriendshipAsync(player1.idPlayer, player2.idPlayer)).ReturnsAsync(existingFriendship);

            var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);

            Assert.False(result.success);
            Assert.Equal(Lang.FriendshipAlreadyExists, result.message);
            mockFriendshipRepository.Verify(fr => fr.addFriendship(It.IsAny<Friendships>()), Times.Never);
            mockFriendshipRepository.Verify(fr => fr.updateFriendship(It.IsAny<Friendships>()), Times.Never);
        }

        [Fact]
        public async Task SendFriendRequestAsync_WhenRequestAlreadySentByRequester_ShouldReturnFailure()
        {
            string requesterUsername = player1.username;
            string targetUsername = player2.username;
            var existingFriendship = new Friendships { requester_id = player1.idPlayer, addressee_id = player2.idPlayer, status_id = FriendshipStatusConstants.PENDING };
            mockFriendshipRepository.Setup(fr => fr.findFriendshipAsync(player1.idPlayer, player2.idPlayer)).ReturnsAsync(existingFriendship);

            var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);

            Assert.False(result.success);
            Assert.Equal(Lang.FriendRequestAlreadySent, result.message);
        }

        [Fact]
        public async Task SendFriendRequestAsync_WhenRequestAlreadySentByTarget_ShouldReturnFailure()
        {
            string requesterUsername = player1.username;
            string targetUsername = player2.username;
            var existingFriendship = new Friendships { requester_id = player2.idPlayer, addressee_id = player1.idPlayer, status_id = FriendshipStatusConstants.PENDING };
            mockFriendshipRepository.Setup(fr => fr.findFriendshipAsync(player1.idPlayer, player2.idPlayer)).ReturnsAsync(existingFriendship);

            var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);

            Assert.False(result.success);
            Assert.Equal(Lang.FriendRequestReceivedFromUser, result.message);
        }

        [Fact]
        public async Task SendFriendRequestAsync_WhenPreviouslyRejected_ShouldReturnSuccessAndUpdateToPending()
        {
            string requesterUsername = player1.username;
            string targetUsername = player2.username;
            
            var existingFriendship = new Friendships { friendships_id = 5, requester_id = player2.idPlayer, addressee_id = player1.idPlayer, status_id = FriendshipStatusConstants.REJECTED, request_date = DateTime.UtcNow.AddDays(-1) };
            mockFriendshipRepository.Setup(fr => fr.findFriendshipAsync(player1.idPlayer, player2.idPlayer)).ReturnsAsync(existingFriendship);
            mockFriendshipRepository.Setup(fr => fr.saveChangesAsync()).ReturnsAsync(1);

            var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);

            Assert.True(result.success);
            Assert.Equal(Lang.FriendRequestSent, result.message);
            mockFriendshipRepository.Verify(fr => fr.saveChangesAsync(), Times.Once);
            mockFriendshipRepository.Verify(fr => fr.addFriendship(It.IsAny<Friendships>()), Times.Never);
        }

        [Fact]
        public async Task RespondToFriendRequestAsync_AcceptingValidRequest_ShouldReturnSuccessAndUpdateStatus()
        {
            string responderUsername = player1.username;
            string requesterUsername = player2.username;
            var pendingFriendship = new Friendships { requester_id = player2.idPlayer, addressee_id = player1.idPlayer, status_id = FriendshipStatusConstants.PENDING };
            mockFriendshipRepository.Setup(fr => fr.findFriendshipAsync(player2.idPlayer, player1.idPlayer)).ReturnsAsync(pendingFriendship);
            mockFriendshipRepository.Setup(fr => fr.saveChangesAsync()).ReturnsAsync(1);

            var result = await socialLogic.respondToFriendRequestAsync(responderUsername, requesterUsername, true);

            Assert.True(result.success);
            Assert.Equal(Lang.FriendRequestAccepted, result.message);
            
            mockFriendshipRepository.Verify(fr => fr.updateFriendship(It.Is<Friendships>(
                f => f == pendingFriendship && f.status_id == FriendshipStatusConstants.ACCEPTED
            )), Times.Once);

            mockFriendshipRepository.Verify(fr => fr.saveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task RespondToFriendRequestAsync_RejectingValidRequest_ShouldReturnSuccessAndUpdateStatus()
        {
            string responderUsername = player1.username;
            string requesterUsername = player2.username;
            var pendingFriendship = new Friendships { requester_id = player2.idPlayer, addressee_id = player1.idPlayer, status_id = FriendshipStatusConstants.PENDING };
            mockFriendshipRepository.Setup(fr => fr.findFriendshipAsync(player2.idPlayer, player1.idPlayer)).ReturnsAsync(pendingFriendship);
            mockFriendshipRepository.Setup(fr => fr.saveChangesAsync()).ReturnsAsync(1);

            var result = await socialLogic.respondToFriendRequestAsync(responderUsername, requesterUsername, false);

            Assert.True(result.success);
            Assert.Equal(Lang.FriendRequestRejected, result.message);

            mockFriendshipRepository.Verify(fr => fr.updateFriendship(It.Is<Friendships>(
                f => f == pendingFriendship && f.status_id == FriendshipStatusConstants.REJECTED
            )), Times.Once);

            mockFriendshipRepository.Verify(fr => fr.saveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task RespondToFriendRequestAsync_WhenRequesterNotFound_ShouldReturnFailure()
        {
            string responderUsername = player1.username;
            string requesterUsername = "NonExistent";
            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(requesterUsername)).ReturnsAsync((Player)null);

            var result = await socialLogic.respondToFriendRequestAsync(responderUsername, requesterUsername, true);

            Assert.False(result.success);
            Assert.Equal(Lang.ErrorPlayerNotFound, result.message);
            mockFriendshipRepository.Verify(fr => fr.findFriendshipAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task GetFriendsListAsync_WithExistingFriends_ShouldReturnCorrectDtoList()
        {
            string username = player1.username;
            var friendships = new List<Friendships>
            {
                new Friendships { requester_id = player1.idPlayer, addressee_id = player2.idPlayer, status_id = FriendshipStatusConstants.ACCEPTED, Player = player1, Player1 = player2 },
                new Friendships { requester_id = player3.idPlayer, addressee_id = player1.idPlayer, status_id = FriendshipStatusConstants.ACCEPTED, Player = player3, Player1 = player1 }
            };
            mockFriendshipRepository.Setup(fr => fr.getAcceptedFriendshipsAsync(player1.idPlayer)).ReturnsAsync(friendships);
            var connectedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { player1.username, player3.username };
            
            var result = await socialLogic.getFriendsListAsync(username, connectedUsers);

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);

            var friend2 = result.FirstOrDefault(f => f.username == player2.username);
            Assert.NotNull(friend2);
            Assert.False(friend2.isOnline);
            Assert.Equal(defaultAvatar, friend2.avatarPath);

            var friend3 = result.FirstOrDefault(f => f.username == player3.username);
            Assert.NotNull(friend3);
            Assert.True(friend3.isOnline);
            Assert.Equal(player3.avatar_path, friend3.avatarPath);
        }

        [Fact]
        public async Task GetFriendsListAsync_WithNoFriends_ShouldReturnEmptyList()
        {
            string username = player1.username;
            mockFriendshipRepository.Setup(fr => fr.getAcceptedFriendshipsAsync(player1.idPlayer)).ReturnsAsync(new List<Friendships>());
            var connectedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { player1.username };

            var result = await socialLogic.getFriendsListAsync(username, connectedUsers);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetFriendsListAsync_WhenPlayerNotFound_ShouldReturnEmptyList()
        {
            string username = "NonExistent";
            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(username)).ReturnsAsync((Player)null);

            var result = await socialLogic.getFriendsListAsync(username, new HashSet<string>());

            Assert.NotNull(result);
            Assert.Empty(result);
            mockFriendshipRepository.Verify(fr => fr.getAcceptedFriendshipsAsync(It.IsAny<int>()), Times.Never);
        }


        [Fact]
        public async Task GetFriendsListAsync_WithNullConnectedUsernames_ShouldTreatAllOffline()
        {
            string username = player1.username;
            var friendships = new List<Friendships> {
                 new Friendships { requester_id = player1.idPlayer, addressee_id = player2.idPlayer, status_id = FriendshipStatusConstants.ACCEPTED, Player = player1, Player1 = player2 }
            };
            mockFriendshipRepository.Setup(fr => fr.getAcceptedFriendshipsAsync(player1.idPlayer)).ReturnsAsync(friendships);
            ICollection<string> connectedUsers = null;

            var result = await socialLogic.getFriendsListAsync(username, connectedUsers);

            Assert.NotNull(result);
            Assert.Single(result);
            var friend2 = result.First();
            Assert.Equal(player2.username, friend2.username);
            Assert.False(friend2.isOnline);
        }

        [Fact]
        public async Task GetFriendRequestsAsync_WithPendingRequests_ShouldReturnCorrectDtoList()
        {
            string username = player1.username;
            var requests = new List<Friendships>
            {
                 new Friendships { requester_id = player2.idPlayer, addressee_id = player1.idPlayer, status_id = FriendshipStatusConstants.PENDING, request_date = DateTime.UtcNow.AddHours(-1), Player1 = player2 },
                 new Friendships { requester_id = player3.idPlayer, addressee_id = player1.idPlayer, status_id = FriendshipStatusConstants.PENDING, request_date = DateTime.UtcNow.AddMinutes(-30), Player1 = player3 }
            };
            mockFriendshipRepository.Setup(fr => fr.getPendingFriendRequestsAsync(player1.idPlayer)).ReturnsAsync(requests);
            var result = await socialLogic.getFriendRequestsAsync(username);

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal(player3.username, result[0].requesterUsername);
            Assert.Equal(player2.username, result[1].requesterUsername);
          
            Assert.Equal(requests[1].request_date, result[0].requestDate);
            Assert.Equal(player3.avatar_path, result[0].avatarPath);
            Assert.Equal(requests[0].request_date, result[1].requestDate);
            Assert.Equal(defaultAvatar, result[1].avatarPath);
        }

        [Fact]
        public async Task GetFriendRequestsAsync_WithNoPendingRequests_ShouldReturnEmptyList()
        {
            string username = player1.username;
            mockFriendshipRepository.Setup(fr => fr.getPendingFriendRequestsAsync(player1.idPlayer)).ReturnsAsync(new List<Friendships>()); // Lista vacía

            var result = await socialLogic.getFriendRequestsAsync(username);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetFriendRequestsAsync_WhenPlayerNotFound_ShouldReturnEmptyList()
        {
            string username = "NonExistent";
            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(username)).ReturnsAsync((Player)null);

            var result = await socialLogic.getFriendRequestsAsync(username);

            Assert.NotNull(result);
            Assert.Empty(result);
            mockFriendshipRepository.Verify(fr => fr.getPendingFriendRequestsAsync(It.IsAny<int>()), Times.Never);
        }


        // --- Pruebas para removeFriendAsync ---

        [Fact]
        public async Task RemoveFriendAsync_WithExistingFriendship_ShouldReturnSuccessAndRemoveFriendship()
        {
            string username = player1.username;
            string friendToRemove = player2.username;
            var friendship = new Friendships { requester_id = player1.idPlayer, addressee_id = player2.idPlayer, status_id = FriendshipStatusConstants.ACCEPTED };
            mockFriendshipRepository.Setup(fr => fr.findFriendshipAsync(player1.idPlayer, player2.idPlayer)).ReturnsAsync(friendship);
            mockFriendshipRepository.Setup(fr => fr.saveChangesAsync()).ReturnsAsync(1);

            var result = await socialLogic.removeFriendAsync(username, friendToRemove);

            Assert.True(result.success);
            Assert.Equal(Lang.FriendRemovedSuccessfully, result.message);
            mockFriendshipRepository.Verify(fr => fr.removeFriendship(friendship), Times.Once);
            mockFriendshipRepository.Verify(fr => fr.saveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task RemoveFriendAsync_WhenFriendToRemoveNotFound_ShouldReturnFailure()
        {
            string username = player1.username;
            string friendToRemove = "NonExistent";
            mockPlayerRepository.Setup(r => r.getPlayerByUsernameAsync(friendToRemove)).ReturnsAsync((Player)null);

            var result = await socialLogic.removeFriendAsync(username, friendToRemove);

            Assert.False(result.success);
            Assert.Equal(Lang.ErrorPlayerNotFound, result.message);
            mockFriendshipRepository.Verify(fr => fr.findFriendshipAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task RemoveFriendAsync_WhenFriendshipNotFound_ShouldReturnFailure()
        {
            string username = player1.username;
            string friendToRemove = player2.username;
            mockFriendshipRepository.Setup(fr => fr.findFriendshipAsync(player1.idPlayer, player2.idPlayer)).ReturnsAsync((Friendships)null);

            var result = await socialLogic.removeFriendAsync(username, friendToRemove);

            Assert.False(result.success);
            Assert.Equal(Lang.ErrorFriendshipNotFound, result.message);
            mockFriendshipRepository.Verify(fr => fr.removeFriendship(It.IsAny<Friendships>()), Times.Never);
        }
    }
}