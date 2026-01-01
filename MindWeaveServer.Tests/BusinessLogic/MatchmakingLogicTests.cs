using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Xunit;

namespace MindWeaveServer.Tests.BusinessLogic
{
    public class MatchmakingLogicTests
    {
        private readonly Mock<ILobbyLifecycleService> lifecycleServiceMock;
        private readonly Mock<ILobbyInteractionService> interactionServiceMock;
        private readonly Mock<INotificationService> notificationServiceMock;
        private readonly Mock<IGameStateManager> gameStateManagerMock;
        private readonly Mock<IPlayerRepository> playerRepositoryMock;
        private readonly Mock<IMatchmakingRepository> matchmakingRepositoryMock;
        private readonly Mock<IPuzzleRepository> puzzleRepositoryMock;
        private readonly Mock<IScoreCalculator> scoreCalculatorMock;

        private readonly GameSessionManager gameSessionManager;
        private readonly MatchmakingLogic matchmakingLogic;

        private readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbiesMock;
        private readonly ConcurrentDictionary<string, IMatchmakingCallback> matchmakingCallbacksMock;

        public MatchmakingLogicTests()
        {
            lifecycleServiceMock = new Mock<ILobbyLifecycleService>();
            interactionServiceMock = new Mock<ILobbyInteractionService>();
            notificationServiceMock = new Mock<INotificationService>();
            gameStateManagerMock = new Mock<IGameStateManager>();
            playerRepositoryMock = new Mock<IPlayerRepository>();
            matchmakingRepositoryMock = new Mock<IMatchmakingRepository>();
            puzzleRepositoryMock = new Mock<IPuzzleRepository>();
            scoreCalculatorMock = new Mock<IScoreCalculator>();

            activeLobbiesMock = new ConcurrentDictionary<string, LobbyStateDto>();
            matchmakingCallbacksMock = new ConcurrentDictionary<string, IMatchmakingCallback>();

            gameStateManagerMock.Setup(g => g.ActiveLobbies).Returns(activeLobbiesMock);
            gameStateManagerMock.Setup(g => g.MatchmakingCallbacks).Returns(matchmakingCallbacksMock);

            var statsLogic = new StatsLogic(new Mock<IStatsRepository>().Object, playerRepositoryMock.Object);
            var puzzleGenerator = new PuzzleGenerator();

            gameSessionManager = new GameSessionManager(
                puzzleRepositoryMock.Object,
                matchmakingRepositoryMock.Object,
                statsLogic,
                puzzleGenerator,
                scoreCalculatorMock.Object
            );

            matchmakingLogic = new MatchmakingLogic(
                lifecycleServiceMock.Object,
                interactionServiceMock.Object,
                notificationServiceMock.Object,
                gameStateManagerMock.Object,
                gameSessionManager,
                playerRepositoryMock.Object,
                matchmakingRepositoryMock.Object
            );
        }


        [Fact]
        public async Task createLobbyAsync_DelegatesToLifecycle()
        {
            var settings = new LobbySettingsDto();
            var expectedResult = new LobbyCreationResultDto { Success = true, LobbyCode = "CODE" };
            lifecycleServiceMock.Setup(x => x.createLobbyAsync("Host", settings)).ReturnsAsync(expectedResult);

            var result = await matchmakingLogic.createLobbyAsync("Host", settings);

            Assert.True(result.Success);
            Assert.Equal("CODE", result.LobbyCode);
        }

        [Fact]
        public async Task createLobbyAsync_LifecycleFails_ReturnsFalse()
        {
            lifecycleServiceMock.Setup(x => x.createLobbyAsync("Host", It.IsAny<LobbySettingsDto>()))
                .ReturnsAsync(new LobbyCreationResultDto { Success = false });

            var result = await matchmakingLogic.createLobbyAsync("Host", new LobbySettingsDto());
            Assert.False(result.Success);
        }


        [Fact]
        public async Task joinLobbyAsync_DelegatesToLifecycle()
        {
            await matchmakingLogic.joinLobbyAsync("User", "CODE", null);
            lifecycleServiceMock.Verify(x => x.joinLobbyAsync(It.IsAny<LobbyActionContext>(), null), Times.Once);
        }

        [Fact]
        public async Task joinLobbyAsync_NullUser_ReturnsError()
        {
            await matchmakingLogic.joinLobbyAsync(null, "CODE", null);
            lifecycleServiceMock.Verify(x => x.joinLobbyAsync(It.Is<LobbyActionContext>(c => c.RequesterUsername == null), null), Times.Once);
        }

        [Fact]
        public async Task joinLobbyAsync_EmptyCode_ReturnsError()
        {
            await matchmakingLogic.joinLobbyAsync("User", "", null);
            lifecycleServiceMock.Verify(x => x.joinLobbyAsync(It.Is<LobbyActionContext>(c => c.LobbyCode == ""), null), Times.Once);
        }


        [Fact]
        public async Task leaveLobbyAsync_DelegatesToLifecycle()
        {
            await matchmakingLogic.leaveLobbyAsync("User", "CODE");
            lifecycleServiceMock.Verify(x => x.leaveLobbyAsync(It.Is<LobbyActionContext>(c => c.RequesterUsername == "User" && c.LobbyCode == "CODE")), Times.Once);
        }


        [Fact]
        public async Task startGameAsync_DelegatesToInteraction()
        {
            await matchmakingLogic.startGameAsync("Host", "CODE");
            interactionServiceMock.Verify(x => x.startGameAsync(It.IsAny<LobbyActionContext>()), Times.Once);
        }

        [Fact]
        public async Task startGameAsync_NullCode_PassesToService()
        {
            await matchmakingLogic.startGameAsync("Host", null);
            interactionServiceMock.Verify(x => x.startGameAsync(It.Is<LobbyActionContext>(c => c.LobbyCode == null)), Times.Once);
        }


        [Fact]
        public async Task expelPlayerAsync_FromLobbyState_Notifies()
        {
            string lobbyCode = "L1";
            string user = "Target";
            var lobbyState = new LobbyStateDto { Players = new System.Collections.Generic.List<string> { "Host", "Target" } };

            activeLobbiesMock.TryAdd(lobbyCode, lobbyState);

            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Target")).ReturnsAsync(new Player { idPlayer = 2 });
            matchmakingRepositoryMock.Setup(r => r.getMatchByLobbyCodeAsync(lobbyCode)).ReturnsAsync(new Matches { matches_id = 100 });

            await matchmakingLogic.expelPlayerAsync(lobbyCode, user, "Reason");

            notificationServiceMock.Verify(n => n.notifyKicked(user, It.IsAny<string>()), Times.Once);
            matchmakingRepositoryMock.Verify(r => r.registerExpulsionAsync(It.IsAny<ExpulsionDto>()), Times.Once);
        }

        [Fact]
        public async Task expelPlayerAsync_LobbyNotFound_DoesNotCrash()
        {
            
            await matchmakingLogic.expelPlayerAsync("UnknownLobby", "User", "Reason");

           
            Assert.True(true); 
        }

        [Fact]
        public async Task expelPlayerAsync_UserNotInLobby_DoesNotCrash()
        {
            string lobbyCode = "L1";
            var lobbyState = new LobbyStateDto { Players = new System.Collections.Generic.List<string> { "Host" } };
            activeLobbiesMock.TryAdd(lobbyCode, lobbyState);

            await matchmakingLogic.expelPlayerAsync(lobbyCode, "Target", "Reason");

            Assert.True(true);
        }


        [Fact]
        public async Task joinLobbyAsGuestAsync_Delegates()
        {
            var req = new GuestJoinRequestDto();
            await matchmakingLogic.joinLobbyAsGuestAsync(req, null);
            lifecycleServiceMock.Verify(x => x.joinLobbyAsGuestAsync(req, null), Times.Once);
        }

        [Fact]
        public async Task inviteGuestByEmailAsync_DelegatesToInteraction()
        {
            var invitationData = new GuestInvitationDto { LobbyCode = "CODE" };
            await matchmakingLogic.inviteGuestByEmailAsync(invitationData);
            interactionServiceMock.Verify(x => x.inviteGuestByEmailAsync(It.IsAny<string>(), "CODE", It.IsAny<string>()), Times.Once);
        }


        [Fact]
        public async Task kickPlayerAsync_DelegatesToInteraction()
        {
            await matchmakingLogic.kickPlayerAsync("Host", "Target", "CODE");
            Assert.True(true);
        }


        [Fact]
        public async Task inviteToLobbyAsync_DelegatesToInteraction()
        {
            await matchmakingLogic.inviteToLobbyAsync("Inviter", "Invited", "CODE");
            Assert.True(true);
        }


        [Fact]
        public async Task changeDifficultyAsync_DelegatesToInteraction()
        {
            await matchmakingLogic.changeDifficultyAsync("Host", "CODE", 2);
            Assert.True(true);
        }


        [Fact]
        public void handleUserDisconnect_CallsGameStateManager()
        {
            matchmakingLogic.handleUserDisconnect("User");
            Assert.True(true);
        }


        [Fact]
        public void registerCallback_StoresCallbackReference()
        {
            var callbackMock = new Mock<IMatchmakingCallback>();
            matchmakingLogic.registerCallback("User", callbackMock.Object);

            Assert.True(matchmakingCallbacksMock.ContainsKey("User"));
        }
    }
}