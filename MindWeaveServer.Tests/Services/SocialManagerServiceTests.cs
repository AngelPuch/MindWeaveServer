using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Services;
using MindWeaveServer.Utilities.Abstractions;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.ServiceModel;
using System.Threading.Tasks;
using Xunit;

namespace MindWeaveServer.Tests.Services
{
    public class SocialManagerServiceTests
    {
        private readonly Mock<IPlayerRepository> playerRepoMock;
        private readonly Mock<IFriendshipRepository> friendshipRepoMock;
        private readonly Mock<IGameStateManager> gameStateMock;
        private readonly Mock<IServiceExceptionHandler> exceptionHandlerMock;
        private readonly Mock<IDisconnectionHandler> disconnectionHandlerMock;
        private readonly SocialLogic logic;
        private readonly SocialManagerService service;

        public SocialManagerServiceTests()
        {
            playerRepoMock = new Mock<IPlayerRepository>();
            friendshipRepoMock = new Mock<IFriendshipRepository>();
            gameStateMock = new Mock<IGameStateManager>();
            exceptionHandlerMock = new Mock<IServiceExceptionHandler>();
            disconnectionHandlerMock = new Mock<IDisconnectionHandler>();

            gameStateMock.Setup(x => x.ConnectedUsers)
                .Returns(new ConcurrentDictionary<string, ISocialCallback>());

            logic = new SocialLogic(playerRepoMock.Object, friendshipRepoMock.Object);
            service = new SocialManagerService(logic, gameStateMock.Object, exceptionHandlerMock.Object, disconnectionHandlerMock.Object);
        }

        private void SetServiceSession(string username)
        {
            var field = typeof(SocialManagerService).GetField("currentUsername", BindingFlags.NonPublic | BindingFlags.Instance);
            field!.SetValue(service, username);
        }

        [Fact]
        public async Task SearchPlayers_InvalidSession_ThrowsFault()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), It.IsAny<string>()))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.SecurityError, "E")));

            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.searchPlayers("User", "query"));
        }

        [Fact]
        public async Task SearchPlayers_ValidSession_Delegates()
        {
            SetServiceSession("User");
            playerRepoMock.Setup(x => x.searchPlayersAsync(It.IsAny<int>(), "query", 10))
                .ReturnsAsync(new List<PlayerSearchResultDto>());
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync("User")).ReturnsAsync(new Player { idPlayer = 1 });

            var result = await service.searchPlayers("User", "query");
            Assert.NotNull(result);
        }

        [Fact]
        public async Task SendFriendRequest_InvalidSession_ThrowsFault()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), It.IsAny<string>()))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.SecurityError, "E")));
            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.sendFriendRequest("A", "B"));
        }

        [Fact]
        public async Task SendFriendRequest_ValidSession_Delegates()
        {
            SetServiceSession("Requester");
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>()))
                .ReturnsAsync(new Player { idPlayer = 1 });
            friendshipRepoMock.Setup(x => x.findFriendshipAsync(1, 1)).ReturnsAsync((Friendships)null!);

            var res = await service.sendFriendRequest("Requester", "Target");
            Assert.NotNull(res);
        }

        [Fact]
        public async Task RespondToFriendRequest_InvalidSession_ThrowsFault()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), It.IsAny<string>()))
               .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.SecurityError, "E")));
            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.respondToFriendRequest("Me", "Other", true));
        }

        [Fact]
        public async Task RespondToFriendRequest_ValidSession_Delegates()
        {
            SetServiceSession("Me");
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>()))
                .ReturnsAsync(new Player { idPlayer = 1 });
            friendshipRepoMock.Setup(x => x.findFriendshipAsync(1, 1)).ReturnsAsync((Friendships)null!);

            var res = await service.respondToFriendRequest("Me", "Sender", true);
            Assert.False(res.Success);
        }

        [Fact]
        public async Task RemoveFriend_InvalidSession_ThrowsFault()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), It.IsAny<string>()))
               .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.SecurityError, "E")));
            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.removeFriend("Me", "Friend"));
        }

        [Fact]
        public async Task RemoveFriend_ValidSession_Delegates()
        {
            SetServiceSession("Me");
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>()))
                .ReturnsAsync(new Player { idPlayer = 1 });
            friendshipRepoMock.Setup(x => x.findFriendshipAsync(1, 1))
                 .ReturnsAsync((Friendships)null!);

            var res = await service.removeFriend("Me", "Friend");
            Assert.False(res.Success);
        }

        [Fact]
        public async Task GetFriendsList_InvalidSession_ThrowsFault()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), It.IsAny<string>()))
               .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.SecurityError, "E")));
            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.getFriendsList("Me"));
        }

        [Fact]
        public async Task GetFriendsList_ValidSession_Delegates()
        {
            SetServiceSession("Me");
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>()))
                .ReturnsAsync(new Player { idPlayer = 1 });
            friendshipRepoMock.Setup(x => x.getAcceptedFriendshipsAsync(It.IsAny<int>()))
                .ReturnsAsync(new List<Friendships>());

            var res = await service.getFriendsList("Me");
            Assert.NotNull(res);
        }

        [Fact]
        public async Task GetFriendRequests_InvalidSession_ThrowsFault()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), It.IsAny<string>()))
               .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.SecurityError, "E")));
            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.getFriendRequests("Me"));
        }

        [Fact]
        public async Task GetFriendRequests_ValidSession_Delegates()
        {
            SetServiceSession("Me");
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync("Me"))
                .ReturnsAsync(new Player { idPlayer = 1 });
            friendshipRepoMock.Setup(x => x.getPendingFriendRequestsAsync(1))
                .ReturnsAsync(new List<Friendships>());

            var res = await service.getFriendRequests("Me");
            Assert.NotNull(res);
        }

        [Fact]
        public async Task SearchPlayers_Exception_HandlesGracefully()
        {
            SetServiceSession("User");
            playerRepoMock.Setup(x => x.searchPlayersAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()))
                .Throws(new Exception());

            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "SearchPlayersOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            var res = await service.searchPlayers("User", "Q");
            Assert.True(res == null || res.Count == 0);
        }

        [Fact]
        public async Task SendFriendRequest_Exception_HandlesGracefully()
        {
            SetServiceSession("A");
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>())).Throws(new Exception());
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "SendFriendRequestOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            try
            {
                var res = await service.sendFriendRequest("A", "B");
                Assert.False(res.Success);
            }
            catch (FaultException<ServiceFaultDto>) { Assert.True(true); }
        }

        [Fact]
        public async Task RespondToFriendRequest_Exception_HandlesGracefully()
        {
            SetServiceSession("A");
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>())).Throws(new Exception());
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "RespondToFriendRequestOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            try
            {
                var res = await service.respondToFriendRequest("A", "B", true);
                Assert.False(res.Success);
            }
            catch (FaultException<ServiceFaultDto>) { Assert.True(true); }
        }

        [Fact]
        public async Task RemoveFriend_Exception_HandlesGracefully()
        {
            SetServiceSession("A");
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>())).Throws(new Exception());
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "RemoveFriendOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            try
            {
                var res = await service.removeFriend("A", "B");
                Assert.False(res.Success);
            }
            catch (FaultException<ServiceFaultDto>) { Assert.True(true); }
        }

        [Fact]
        public async Task GetFriendsList_Exception_ThrowsFault()
        {
            SetServiceSession("A");
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>())).Throws(new Exception());

            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "GetFriendsListOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.getFriendsList("A"));
        }

        [Fact]
        public async Task GetFriendRequests_Exception_ThrowsFault()
        {
            SetServiceSession("A");
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>())).Throws(new Exception());
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "GetFriendRequestsOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.getFriendRequests("A"));
        }



        [Fact]
        public void Constructor_ValidParams_Initializes()
        {
            Assert.NotNull(service);
        }
    }
}
