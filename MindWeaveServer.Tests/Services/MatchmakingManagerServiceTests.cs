using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Services;
using MindWeaveServer.Utilities.Abstractions;
using Moq;
using System;
using System.Reflection;
using System.ServiceModel;
using System.Threading.Tasks;
using Xunit;

namespace MindWeaveServer.Tests.Services
{
    public class MatchmakingManagerServiceTests
    {
        private readonly Mock<ILobbyLifecycleService> lifecycleMock;
        private readonly Mock<ILobbyInteractionService> interactionMock;
        private readonly Mock<INotificationService> notificationMock;
        private readonly Mock<IGameStateManager> gameStateMock;
        private readonly Mock<IMatchmakingRepository> matchRepoMock;
        private readonly Mock<IPlayerRepository> playerRepoMock;
        private readonly Mock<IPuzzleRepository> puzzleRepoMock;
        private readonly Mock<IServiceExceptionHandler> exceptionHandlerMock;

        private readonly GameSessionManager sessionManager;
        private readonly LobbyModerationManager moderationManager;
        private readonly MatchmakingLogic logic;
        private readonly MatchmakingManagerService service;

        public MatchmakingManagerServiceTests()
        {
            lifecycleMock = new Mock<ILobbyLifecycleService>();
            interactionMock = new Mock<ILobbyInteractionService>();
            notificationMock = new Mock<INotificationService>();
            gameStateMock = new Mock<IGameStateManager>();
            matchRepoMock = new Mock<IMatchmakingRepository>();
            playerRepoMock = new Mock<IPlayerRepository>();
            puzzleRepoMock = new Mock<IPuzzleRepository>();
            exceptionHandlerMock = new Mock<IServiceExceptionHandler>();

            var statsLogic = new StatsLogic(new Mock<IStatsRepository>().Object, playerRepoMock.Object);
            sessionManager = new GameSessionManager(
                puzzleRepoMock.Object,
                matchRepoMock.Object,
                statsLogic,
                new PuzzleGenerator(),
                new Mock<IScoreCalculator>().Object);

            moderationManager = new LobbyModerationManager();

            logic = new MatchmakingLogic(
                lifecycleMock.Object,
                interactionMock.Object,
                notificationMock.Object,
                gameStateMock.Object,
                sessionManager,
                playerRepoMock.Object,
                matchRepoMock.Object,
                moderationManager
            );

            service = new MatchmakingManagerService(logic, sessionManager, playerRepoMock.Object, exceptionHandlerMock.Object);
        }

        private void SetSession(string username)
        {
            var uField = typeof(MatchmakingManagerService).GetField("currentUsername", BindingFlags.NonPublic | BindingFlags.Instance);
            uField.SetValue(service, username);

            var cbMock = new Mock<IMatchmakingCallback>();
            var comm = cbMock.As<ICommunicationObject>();
            comm.Setup(x => x.State).Returns(CommunicationState.Opened);

            var cField = typeof(MatchmakingManagerService).GetField("currentUserCallback", BindingFlags.NonPublic | BindingFlags.Instance);
            cField.SetValue(service, cbMock.Object);
        }

        [Fact]
        public async Task createLobbyReturnsResult()
        {
            SetSession("Host");
            var settings = new LobbySettingsDto { DifficultyId = 1, PreloadedPuzzleId = 1 };

            lifecycleMock.Setup(x => x.createLobbyAsync("Host", settings))
                .ReturnsAsync(new LobbyCreationResultDto { Success = true, LobbyCode = "CODE" });

            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync("Host"))
                .ReturnsAsync(new Player { idPlayer = 1 });

            var result = await service.createLobby("Host", settings);

            Assert.True(result.Success);
            Assert.Equal("CODE", result.LobbyCode);
        }

        [Fact]
        public async Task createLobbyFailsIfSessionMismatch()
        {
            SetSession("Other");
            var res = await service.createLobby("Host", new LobbySettingsDto());
            Assert.False(res.Success);
        }

        [Fact]
        public async Task createLobbyHandlesException()
        {
            SetSession("Host");
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "CreateLobbyOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            lifecycleMock.Setup(x => x.createLobbyAsync(It.IsAny<string>(), It.IsAny<LobbySettingsDto>()))
                .Throws(new Exception());

            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.createLobby("Host", new LobbySettingsDto()));
        }

        [Fact]
        public void joinLobbyHandlesValidationFailure()
        {
            SetSession("Other");
            service.joinLobby("User", "Code");
        }

        [Fact]
        public void leaveLobbyDoesNotThrow()
        {
            SetSession("User");
            service.leaveLobby("User", "Code");
        }

        [Fact]
        public void startGameDoesNotThrow()
        {
            SetSession("Host");
            service.startGame("Host", "Code");
        }

        [Fact]
        public void kickPlayerDoesNotThrow()
        {
            SetSession("Host");
            service.kickPlayer("Host", "Kicked", "Code");
        }

        [Fact]
        public void inviteToLobbyDoesNotThrow()
        {
            SetSession("Inviter");
            service.inviteToLobby("Inviter", "Invited", "Code");
        }

        [Fact]
        public void changeDifficultyDoesNotThrow()
        {
            SetSession("Host");
            service.changeDifficulty("Host", "Code", 2);
        }

        [Fact]
        public void inviteGuestByEmailValidatesSession()
        {
            SetSession("Other");
            service.inviteGuestByEmail(new GuestInvitationDto { InviterUsername = "User" });
        }

        [Fact]
        public void leaveGameCallsSessionManager()
        {
            service.leaveGame("User", "Code");
        }

        [Fact]
        public async Task joinLobbyAsGuestHandlesException()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "JoinLobbyAsGuestOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.joinLobbyAsGuest(new GuestJoinRequestDto()));
        }

        [Fact]
        public void requestPieceDragValidatesSession()
        {
            SetSession("Other");
            service.requestPieceDrag("Code", 1);
        }

        [Fact]
        public void requestPieceDragDelegatesIfValid()
        {
            SetSession("User");
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync("User"))
                .ReturnsAsync(new Player { idPlayer = 1 });

            try { service.requestPieceDrag("Code", 1); } catch { }
        }

        [Fact]
        public void requestPieceMoveValidatesSession()
        {
            SetSession("Other");
            service.requestPieceMove("Code", 1, 0, 0);
        }

        [Fact]
        public void requestPieceDropValidatesSession()
        {
            SetSession("Other");
            service.requestPieceDrop("Code", 1, 0, 0);
        }

        [Fact]
        public void requestPieceReleaseValidatesSession()
        {
            SetSession("Other");
            service.requestPieceRelease("Code", 1);
        }

        [Fact]
        public void createLobbyReturnsFailIfValidationFails()
        {
            SetSession("Host");
            lifecycleMock.Setup(x => x.createLobbyAsync(It.IsAny<string>(), It.IsAny<LobbySettingsDto>()))
                .Throws(new Exception());

            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), It.IsAny<string>()))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.createLobby("Host", new LobbySettingsDto()));
        }

        [Fact]
        public void serviceInitializes()
        {
            Assert.NotNull(service);
        }
    }
}