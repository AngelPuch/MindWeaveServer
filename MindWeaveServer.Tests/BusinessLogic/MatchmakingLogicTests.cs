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
        private readonly LobbyModerationManager moderationManager;
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

            moderationManager = new LobbyModerationManager();

            matchmakingLogic = new MatchmakingLogic(
                lifecycleServiceMock.Object,
                interactionServiceMock.Object,
                notificationServiceMock.Object,
                gameStateManagerMock.Object,
                gameSessionManager,
                playerRepositoryMock.Object,
                matchmakingRepositoryMock.Object,
                moderationManager
            );
        }


        [Fact]
        public async Task CreateLobbyAsync_ValidSettings_ReturnsSuccess()
        {
            var settings = new LobbySettingsDto();
            var expectedResult = new LobbyCreationResultDto { Success = true, LobbyCode = "CODE" };
            lifecycleServiceMock.Setup(x => x.createLobbyAsync("Host", settings)).ReturnsAsync(expectedResult);

            var result = await matchmakingLogic.createLobbyAsync("Host", settings);

            Assert.Equal("CODE", result.LobbyCode);
        }

        [Fact]
        public async Task CreateLobbyAsync_LifecycleFails_ReturnsFalse()
        {
            lifecycleServiceMock.Setup(x => x.createLobbyAsync("Host", It.IsAny<LobbySettingsDto>()))
                .ReturnsAsync(new LobbyCreationResultDto { Success = false });

            var result = await matchmakingLogic.createLobbyAsync("Host", new LobbySettingsDto());
            Assert.False(result.Success);
        }


        [Fact]
        public async Task JoinLobbyAsync_ValidData_DelegatesToLifecycle()
        {
            await matchmakingLogic.joinLobbyAsync("User", "CODE", null);
            lifecycleServiceMock.Verify(x => x.joinLobbyAsync(It.IsAny<LobbyActionContext>(), null), Times.Once);
        }

        [Fact]
        public async Task JoinLobbyAsync_NullUser_PassesToService()
        {
            await matchmakingLogic.joinLobbyAsync(null, "CODE", null);
            lifecycleServiceMock.Verify(x => x.joinLobbyAsync(It.Is<LobbyActionContext>(c => c.RequesterUsername == null), null), Times.Once);
        }

        [Fact]
        public async Task JoinLobbyAsync_EmptyCode_PassesToService()
        {
            await matchmakingLogic.joinLobbyAsync("User", "", null);
            lifecycleServiceMock.Verify(x => x.joinLobbyAsync(It.Is<LobbyActionContext>(c => c.LobbyCode == ""), null), Times.Once);
        }


        [Fact]
        public async Task LeaveLobbyAsync_ValidData_DelegatesToLifecycle()
        {
            await matchmakingLogic.leaveLobbyAsync("User", "CODE");
            lifecycleServiceMock.Verify(x => x.leaveLobbyAsync(It.Is<LobbyActionContext>(c => c.RequesterUsername == "User" && c.LobbyCode == "CODE")), Times.Once);
        }


        [Fact]
        public async Task StartGameAsync_ValidData_DelegatesToInteraction()
        {
            await matchmakingLogic.startGameAsync("Host", "CODE");
            interactionServiceMock.Verify(x => x.startGameAsync(It.IsAny<LobbyActionContext>()), Times.Once);
        }

        [Fact]
        public async Task StartGameAsync_NullCode_PassesToService()
        {
            await matchmakingLogic.startGameAsync("Host", null);
            interactionServiceMock.Verify(x => x.startGameAsync(It.Is<LobbyActionContext>(c => c.LobbyCode == null)), Times.Once);
        }


        [Fact]
        public async Task ExpelPlayerAsync_ValidData_NotifiesUser()
        {
            string lobbyCode = "L1";
            string user = "Target";
            var lobbyState = new LobbyStateDto
            {
                Players = new System.Collections.Generic.List<string> { "Host", "Target" },
                HostUsername = "Host"
            };

            activeLobbiesMock.TryAdd(lobbyCode, lobbyState);
            moderationManager.initializeLobby(lobbyCode);

            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Target")).ReturnsAsync(new Player { idPlayer = 2 });
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Host")).ReturnsAsync(new Player { idPlayer = 1 });
            matchmakingRepositoryMock.Setup(r => r.getMatchByLobbyCodeAsync(lobbyCode)).ReturnsAsync(new Matches { matches_id = 100 });

            await matchmakingLogic.expelPlayerAsync(lobbyCode, user, "Reason");

            notificationServiceMock.Verify(n => n.notifyKicked(user, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ExpelPlayerAsync_ProfanityReason_UsesProfanityCode()
        {
            string lobbyCode = "L1";
            string user = "Target";
            var lobbyState = new LobbyStateDto
            {
                Players = new System.Collections.Generic.List<string> { "Host", "Target" },
                HostUsername = "Host"
            };

            activeLobbiesMock.TryAdd(lobbyCode, lobbyState);
            moderationManager.initializeLobby(lobbyCode);

            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Target")).ReturnsAsync(new Player { idPlayer = 2 });
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Host")).ReturnsAsync(new Player { idPlayer = 1 });
            matchmakingRepositoryMock.Setup(r => r.getMatchByLobbyCodeAsync(lobbyCode)).ReturnsAsync(new Matches { matches_id = 100 });

            await matchmakingLogic.expelPlayerAsync(lobbyCode, user, MatchmakingLogic.PROFANITY_REASON_TEXT);

            notificationServiceMock.Verify(n => n.notifyKicked(user, MessageCodes.NOTIFY_KICKED_PROFANITY), Times.Once);
        }

        [Fact]
        public async Task ExpelPlayerAsync_LobbyNotFound_DoesNotCrash()
        {
            await matchmakingLogic.expelPlayerAsync("UnknownLobby", "User", "Reason");

            Assert.True(true);
        }

        [Fact]
        public async Task ExpelPlayerAsync_UserNotInLobby_DoesNotCrash()
        {
            string lobbyCode = "L1";
            var lobbyState = new LobbyStateDto
            {
                Players = new System.Collections.Generic.List<string> { "Host" },
                HostUsername = "Host"
            };
            activeLobbiesMock.TryAdd(lobbyCode, lobbyState);
            moderationManager.initializeLobby(lobbyCode);

            await matchmakingLogic.expelPlayerAsync(lobbyCode, "Target", "Reason");

            Assert.True(true);
        }

        [Fact]
        public async Task ExpelPlayerAsync_PlayerNotFoundInDb_StillBans()
        {
            string lobbyCode = "L1";
            string user = "Target";
            var lobbyState = new LobbyStateDto
            {
                Players = new System.Collections.Generic.List<string> { "Host", "Target" },
                HostUsername = "Host"
            };

            activeLobbiesMock.TryAdd(lobbyCode, lobbyState);
            moderationManager.initializeLobby(lobbyCode);

            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Target")).ReturnsAsync((Player)null!);
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Host")).ReturnsAsync(new Player { idPlayer = 1 });
            matchmakingRepositoryMock.Setup(r => r.getMatchByLobbyCodeAsync(lobbyCode)).ReturnsAsync(new Matches { matches_id = 100 });

            await matchmakingLogic.expelPlayerAsync(lobbyCode, user, "Reason");

            Assert.True(moderationManager.isBanned(lobbyCode, user));
        }


        [Fact]
        public async Task JoinLobbyAsGuestAsync_ValidRequest_Delegates()
        {
            var req = new GuestJoinRequestDto();
            await matchmakingLogic.joinLobbyAsGuestAsync(req, null);
            lifecycleServiceMock.Verify(x => x.joinLobbyAsGuestAsync(req, null), Times.Once);
        }

        [Fact]
        public async Task InviteGuestByEmailAsync_ValidData_DelegatesToInteraction()
        {
            var invitationData = new GuestInvitationDto { LobbyCode = "CODE" };
            await matchmakingLogic.inviteGuestByEmailAsync(invitationData);
            interactionServiceMock.Verify(x => x.inviteGuestByEmailAsync(It.IsAny<string>(), "CODE", It.IsAny<string>()), Times.Once);
        }


        [Fact]
        public async Task KickPlayerAsync_ValidData_DelegatesToInteraction()
        {
            await matchmakingLogic.kickPlayerAsync("Host", "Target", "CODE");
            interactionServiceMock.Verify(x => x.kickPlayerAsync(It.Is<LobbyActionContext>(
                c => c.RequesterUsername == "Host" && c.TargetUsername == "Target" && c.LobbyCode == "CODE")), Times.Once);
        }


        [Fact]
        public async Task InviteToLobbyAsync_ValidData_DelegatesToInteraction()
        {
            await matchmakingLogic.inviteToLobbyAsync("Inviter", "Invited", "CODE");
            interactionServiceMock.Verify(x => x.invitePlayerAsync(It.Is<LobbyActionContext>(
                c => c.RequesterUsername == "Inviter" && c.TargetUsername == "Invited" && c.LobbyCode == "CODE")), Times.Once);
        }


        [Fact]
        public async Task ChangeDifficultyAsync_ValidData_DelegatesToInteraction()
        {
            await matchmakingLogic.changeDifficultyAsync("Host", "CODE", 2);
            interactionServiceMock.Verify(x => x.changeDifficultyAsync(It.Is<LobbyActionContext>(
                c => c.RequesterUsername == "Host" && c.LobbyCode == "CODE"), 2), Times.Once);
        }


        [Fact]
        public void HandleUserDisconnect_ValidUsername_CallsLifecycle()
        {
            matchmakingLogic.handleUserDisconnect("User");
            lifecycleServiceMock.Verify(x => x.handleUserDisconnect("User"), Times.Once);
        }


        [Fact]
        public void RegisterCallback_ValidData_StoresCallback()
        {
            var callbackMock = new Mock<IMatchmakingCallback>();
            matchmakingLogic.registerCallback("User", callbackMock.Object);

            Assert.True(matchmakingCallbacksMock.ContainsKey("User"));
        }

        [Fact]
        public void RegisterCallback_NullUsername_DoesNotStore()
        {
            var callbackMock = new Mock<IMatchmakingCallback>();
            matchmakingLogic.registerCallback(null, callbackMock.Object);

            Assert.Empty(matchmakingCallbacksMock);
        }

        [Fact]
        public void RegisterCallback_NullCallback_DoesNotStore()
        {
            matchmakingLogic.registerCallback("User", null);

            Assert.False(matchmakingCallbacksMock.ContainsKey("User"));
        }

        [Fact]
        public void RegisterCallback_ExistingUser_UpdatesCallback()
        {
            var callbackMock1 = new Mock<IMatchmakingCallback>();
            var callbackMock2 = new Mock<IMatchmakingCallback>();

            matchmakingLogic.registerCallback("User", callbackMock1.Object);
            matchmakingLogic.registerCallback("User", callbackMock2.Object);

            Assert.True(matchmakingCallbacksMock.TryGetValue("User", out var storedCallback));
            Assert.Same(callbackMock2.Object, storedCallback);
        }
    }
}
