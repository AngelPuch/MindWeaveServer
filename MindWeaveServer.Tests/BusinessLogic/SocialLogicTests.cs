using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Utilities;

namespace MindWeaveServer.Tests.BusinessLogic
{
    public class SocialLogicTests
    {
        private readonly Mock<IFriendshipRepository> friendshipRepositoryMock;
        private readonly Mock<IPlayerRepository> playerRepositoryMock;
        private readonly SocialLogic socialLogic;

        public SocialLogicTests()
        {
            friendshipRepositoryMock = new Mock<IFriendshipRepository>();
            playerRepositoryMock = new Mock<IPlayerRepository>();

            socialLogic = new SocialLogic(
                playerRepositoryMock.Object,
                friendshipRepositoryMock.Object
            );
        }


        [Fact]
        public async Task sendFriendRequestAsyncSuccess_CreatesNew()
        {
            var requester = new Player { idPlayer = 1, username = "Me" };
            var target = new Player { idPlayer = 2, username = "Target" };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(requester);
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Target")).ReturnsAsync(target);
            friendshipRepositoryMock.Setup(r => r.findFriendshipAsync(1, 2)).ReturnsAsync((Friendships)null);

            var result = await socialLogic.sendFriendRequestAsync("Me", "Target");

            Assert.True(result.Success);
            friendshipRepositoryMock.Verify(r => r.addFriendship(It.IsAny<Friendships>()), Times.Once);
        }

        [Fact]
        public async Task sendFriendRequestAsync_TargetNotFound_ReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(new Player { idPlayer = 1 });
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Target")).ReturnsAsync((Player)null);

            var result = await socialLogic.sendFriendRequestAsync("Me", "Target");

            Assert.False(result.Success);
        }

        [Fact]
        public async Task sendFriendRequestAsync_Self_ReturnsError()
        {
            var result = await socialLogic.sendFriendRequestAsync("Me", "Me");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task sendFriendRequestAsync_AlreadyAccepted_ReturnsError()
        {
            var requester = new Player { idPlayer = 1 };
            var target = new Player { idPlayer = 2 };
            var existing = new Friendships { status_id = FriendshipStatusConstants.ACCEPTED };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(requester);
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Target")).ReturnsAsync(target);
            friendshipRepositoryMock.Setup(r => r.findFriendshipAsync(1, 2)).ReturnsAsync(existing);

            var result = await socialLogic.sendFriendRequestAsync("Me", "Target");

            Assert.False(result.Success);
        }

        [Fact]
        public async Task sendFriendRequestAsync_PendingRequest_ReturnsError()
        {
            var requester = new Player { idPlayer = 1 };
            var target = new Player { idPlayer = 2 };
            var existing = new Friendships { status_id = FriendshipStatusConstants.PENDING };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(requester);
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Target")).ReturnsAsync(target);
            friendshipRepositoryMock.Setup(r => r.findFriendshipAsync(1, 2)).ReturnsAsync(existing);

            var result = await socialLogic.sendFriendRequestAsync("Me", "Target");

            Assert.False(result.Success);
        }

        [Fact]
        public async Task sendFriendRequestAsync_RejectedThenResend_Success()
        {
            var requester = new Player { idPlayer = 1 };
            var target = new Player { idPlayer = 2 };
            var rejectedRequest = new Friendships { status_id = FriendshipStatusConstants.REJECTED };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(requester);
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Target")).ReturnsAsync(target);
            friendshipRepositoryMock.Setup(r => r.findFriendshipAsync(1, 2)).ReturnsAsync(rejectedRequest);

            var result = await socialLogic.sendFriendRequestAsync("Me", "Target");

            Assert.True(result.Success);
            friendshipRepositoryMock.Verify(r => r.updateFriendship(It.IsAny<Friendships>()), Times.Once);
        }


        [Fact]
        public async Task getFriendsListAsync_ReturnsMappedList()
        {
            var me = new Player { idPlayer = 1, username = "Me" };
            var friendPlayer = new Player { idPlayer = 2, username = "F1", avatar_path = "path" };

           
            var friendship = new Friendships
            {
                requester_id = 1,
                addressee_id = 2,
                Player = me,          
                Player1 = friendPlayer 
            };

            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(me);
            friendshipRepositoryMock.Setup(r => r.getAcceptedFriendshipsAsync(1))
                .ReturnsAsync(new List<Friendships> { friendship });

            var connectedUsers = new List<string> { "F1" };

            var result = await socialLogic.getFriendsListAsync("Me", connectedUsers);

            Assert.Single(result);
            Assert.Equal("F1", result[0].Username);
            Assert.True(result[0].IsOnline, "El amigo debería aparecer como conectado");
        }

        [Fact]
        public async Task getFriendsListAsync_UserNotFound_ReturnsEmpty()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync((Player)null);

            var result = await socialLogic.getFriendsListAsync("Me", new List<string>());

            Assert.Empty(result);
        }

        [Fact]
        public async Task getFriendsListAsync_MultipleFriendsOffline()
        {
            var me = new Player { idPlayer = 1, username = "Me" };
            var friend1 = new Player { idPlayer = 2, username = "F1" };
            var friend2 = new Player { idPlayer = 3, username = "F2" };

            var friendship1 = new Friendships { requester_id = 1, addressee_id = 2, Player = me, Player1 = friend1 };
            var friendship2 = new Friendships { requester_id = 1, addressee_id = 3, Player = me, Player1 = friend2 };

            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(me);
            friendshipRepositoryMock.Setup(r => r.getAcceptedFriendshipsAsync(1))
                .ReturnsAsync(new List<Friendships> { friendship1, friendship2 });

            var result = await socialLogic.getFriendsListAsync("Me", new List<string>());

            Assert.Equal(2, result.Count);
            Assert.All(result, f => Assert.False(f.IsOnline));
        }


        [Fact]
        public async Task respondToFriendRequestAsync_Accept_Success()
        {
            var responder = new Player { idPlayer = 1 };
            var requester = new Player { idPlayer = 2 };
            var friendship = new Friendships { status_id = FriendshipStatusConstants.PENDING, addressee_id = 1, requester_id = 2 };

            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(responder);
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Other")).ReturnsAsync(requester);
            friendshipRepositoryMock.Setup(r => r.findFriendshipAsync(2, 1)).ReturnsAsync(friendship);

            var result = await socialLogic.respondToFriendRequestAsync("Me", "Other", true);

            Assert.True(result.Success);
            friendshipRepositoryMock.Verify(r => r.updateFriendship(friendship), Times.Once);
        }

        [Fact]
        public async Task respondToFriendRequestAsync_Reject_Success()
        {
            var responder = new Player { idPlayer = 1 };
            var requester = new Player { idPlayer = 2 };
            var friendship = new Friendships { status_id = FriendshipStatusConstants.PENDING, addressee_id = 1 };

            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(responder);
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Other")).ReturnsAsync(requester);
            friendshipRepositoryMock.Setup(r => r.findFriendshipAsync(2, 1)).ReturnsAsync(friendship);

            var result = await socialLogic.respondToFriendRequestAsync("Me", "Other", false);

            Assert.True(result.Success);
            Assert.Equal(FriendshipStatusConstants.REJECTED, friendship.status_id);
        }

        [Fact]
        public async Task respondToFriendRequestAsync_NoRequest_ReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(new Player { idPlayer = 1 });
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Other")).ReturnsAsync(new Player { idPlayer = 2 });
            friendshipRepositoryMock.Setup(r => r.findFriendshipAsync(2, 1)).ReturnsAsync((Friendships)null);

            var result = await socialLogic.respondToFriendRequestAsync("Me", "Other", true);

            Assert.False(result.Success);
        }


        [Fact]
        public async Task removeFriendAsync_Success()
        {
            var me = new Player { idPlayer = 1 };
            var friend = new Player { idPlayer = 2 };

            var friendship = new Friendships
            {
                friendships_id = 10,
                status_id = FriendshipStatusConstants.ACCEPTED
            };

            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(me);
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Friend")).ReturnsAsync(friend);
            friendshipRepositoryMock.Setup(r => r.findFriendshipAsync(1, 2)).ReturnsAsync(friendship);

            var result = await socialLogic.removeFriendAsync("Me", "Friend");

            Assert.True(result.Success);
            friendshipRepositoryMock.Verify(r => r.removeFriendship(friendship), Times.Once);
        }

        [Fact]
        public async Task removeFriendAsync_NotFriends_ReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(new Player { idPlayer = 1 });
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Friend")).ReturnsAsync(new Player { idPlayer = 2 });
            friendshipRepositoryMock.Setup(r => r.findFriendshipAsync(1, 2)).ReturnsAsync((Friendships)null);

            var result = await socialLogic.removeFriendAsync("Me", "Friend");

            Assert.False(result.Success);
        }


        [Fact]
        public async Task searchPlayersAsync_ReturnsDtos()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(new Player { idPlayer = 1 });
            var searchResults = new List<PlayerSearchResultDto> { new PlayerSearchResultDto { Username = "FoundUser" } };
            playerRepositoryMock.Setup(r => r.searchPlayersAsync(1, "query", It.IsAny<int>())).ReturnsAsync(searchResults);

            var result = await socialLogic.searchPlayersAsync("Me", "query");

            Assert.Single(result);
        }

        [Fact]
        public async Task searchPlayersAsync_EmptyQuery_ReturnsEmpty()
        {
            var result = await socialLogic.searchPlayersAsync("Me", "");

            Assert.Empty(result);
        }

        [Fact]
        public async Task searchPlayersAsync_NullUser_ReturnsEmpty()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync((Player)null);

            var result = await socialLogic.searchPlayersAsync("Me", "query");

            Assert.Empty(result);
        }

        [Fact]
        public async Task searchPlayersAsync_MultipleResults()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Me")).ReturnsAsync(new Player { idPlayer = 1 });
            var searchResults = new List<PlayerSearchResultDto>
            {
                new PlayerSearchResultDto { Username = "User1" },
                new PlayerSearchResultDto { Username = "User2" },
                new PlayerSearchResultDto { Username = "User3" }
            };
            playerRepositoryMock.Setup(r => r.searchPlayersAsync(1, "Us", It.IsAny<int>())).ReturnsAsync(searchResults);

            var result = await socialLogic.searchPlayersAsync("Me", "Us");

            Assert.Equal(3, result.Count);
        }
    }
}