using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.Contracts.DataContracts.Chat;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess.Abstractions;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;
using Xunit;

namespace MindWeaveServer.Tests.BusinessLogic
{
    public class ChatLogicTests
    {
        private readonly Mock<IGameStateManager> gameStateManagerMock;
        private readonly Mock<IPlayerExpulsionService> playerExpulsionServiceMock;
        private readonly Mock<INotificationService> notificationServiceMock;
        private readonly Mock<IMatchmakingRepository> matchmakingRepositoryMock;
        private readonly LobbyModerationManager lobbyModerationManager;
        private readonly ChatLogic chatLogic;
        private readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbyMock;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> lobbyChatUsersMock;
        private readonly ConcurrentDictionary<string, List<ChatMessageDto>> lobbyChatHistoryMock;

        public ChatLogicTests()
        {
            gameStateManagerMock = new Mock<IGameStateManager>();
            playerExpulsionServiceMock = new Mock<IPlayerExpulsionService>();
            notificationServiceMock = new Mock<INotificationService>();
            matchmakingRepositoryMock = new Mock<IMatchmakingRepository>();

            activeLobbyMock = new ConcurrentDictionary<string, LobbyStateDto>();
            lobbyChatUsersMock = new ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>>();
            lobbyChatHistoryMock = new ConcurrentDictionary<string, List<ChatMessageDto>>();

            gameStateManagerMock.Setup(g => g.ActiveLobbies).Returns(activeLobbyMock);
            gameStateManagerMock.Setup(g => g.LobbyChatUsers).Returns(lobbyChatUsersMock);
            gameStateManagerMock.Setup(g => g.LobbyChatHistory).Returns(lobbyChatHistoryMock);

            lobbyModerationManager = new LobbyModerationManager();

            chatLogic = new ChatLogic(
                gameStateManagerMock.Object,
                playerExpulsionServiceMock.Object,
                lobbyModerationManager
            );
        }

        private Mock<IChatCallback> CreateMockCallback()
        {
            var mock = new Mock<IChatCallback>();
            mock.As<ICommunicationObject>().Setup(x => x.State).Returns(CommunicationState.Opened);
            return mock;
        }

        private Mock<IChatCallback> CreateMockCallbackClosedState()
        {
            var mock = new Mock<IChatCallback>();
            mock.As<ICommunicationObject>().Setup(x => x.State).Returns(CommunicationState.Closed);
            return mock;
        }

        [Fact]
        public async Task ProcessAndBroadcastMessageAsyncFiltersProfanity()
        {
            const string lobbyCode = "CODE";
            const string username = "User";
            const string host = "Host";
            const string profaneContent = "badword";

            var userCallbackMock = CreateMockCallback();
            var hostCallbackMock = CreateMockCallback();

            var lobbyState = new LobbyStateDto { LobbyId = lobbyCode, HostUsername = host, Players = new List<string> { username, host } };
            activeLobbyMock.TryAdd(lobbyCode, lobbyState);
            lobbyChatUsersMock.TryAdd(lobbyCode, new ConcurrentDictionary<string, IChatCallback>());
            lobbyChatUsersMock[lobbyCode].TryAdd(username, userCallbackMock.Object);
            lobbyChatUsersMock[lobbyCode].TryAdd(host, hostCallbackMock.Object);
            lobbyChatHistoryMock.TryAdd(lobbyCode, new List<ChatMessageDto>());

            lobbyModerationManager.initializeLobby(lobbyCode);

            await chatLogic.processAndBroadcastMessageAsync(username, lobbyCode, profaneContent);

            userCallbackMock.Verify(c => c.receiveLobbyMessage(It.IsAny<ChatMessageDto>()), Times.Never);
            userCallbackMock.Verify(c => c.receiveSystemMessage(It.Is<string>(s => s.StartsWith("CHAT_PROFANITY_WARNING"))), Times.Once);
            Assert.Empty(lobbyChatHistoryMock[lobbyCode]);
        }

        [Fact]
        public async Task ProcessAndBroadcastMessageAsyncNullContentThrowsException()
        {
            const string lobbyCode = "CODE";
            lobbyModerationManager.initializeLobby(lobbyCode);
            await Assert.ThrowsAsync<ArgumentException>(() => chatLogic.processAndBroadcastMessageAsync("User", lobbyCode, null));
        }

        [Fact]
        public async Task ProcessAndBroadcastMessageAsyncWithValidMessage()
        {
            const string username = "User";
            const string lobbyCode = "CODE";
            const string messageContent = "Test message";

            var callbackMock = CreateMockCallback();
            var hostCallbackMock = CreateMockCallback();
            var lobbyState = new LobbyStateDto { LobbyId = lobbyCode, HostUsername = "Host", Players = new List<string> { username, "Host" } };

            activeLobbyMock.TryAdd(lobbyCode, lobbyState);
            lobbyChatUsersMock.TryAdd(lobbyCode, new ConcurrentDictionary<string, IChatCallback>());
            lobbyChatUsersMock[lobbyCode].TryAdd(username, callbackMock.Object);
            lobbyChatUsersMock[lobbyCode].TryAdd("Host", hostCallbackMock.Object);
            lobbyChatHistoryMock.TryAdd(lobbyCode, new List<ChatMessageDto>());

            lobbyModerationManager.initializeLobby(lobbyCode);

            await chatLogic.processAndBroadcastMessageAsync(username, lobbyCode, messageContent);

            callbackMock.Verify(c => c.receiveLobbyMessage(It.IsAny<ChatMessageDto>()), Times.Once);
            hostCallbackMock.Verify(c => c.receiveLobbyMessage(It.IsAny<ChatMessageDto>()), Times.Once);
            Assert.Single(lobbyChatHistoryMock[lobbyCode]);
        }

        [Fact]
        public async Task ProcessAndBroadcastMessageAsyncEmptyContentThrowsException()
        {
            const string lobbyCode = "CODE";
            lobbyModerationManager.initializeLobby(lobbyCode);
            await Assert.ThrowsAsync<ArgumentException>(() => chatLogic.processAndBroadcastMessageAsync("User", lobbyCode, string.Empty));
        }

        [Fact]
        public async Task ProcessAndBroadcastMessageAsyncBroadcastsToAllUsersInLobby()
        {
            const string lobbyCode = "CODE";
            const string sender = "User1";
            const string recipient = "User2";

            var senderCallbackMock = CreateMockCallback();
            var recipientCallbackMock = CreateMockCallback();
            var lobbyState = new LobbyStateDto { LobbyId = lobbyCode, HostUsername = sender, Players = new List<string> { sender, recipient } };

            activeLobbyMock.TryAdd(lobbyCode, lobbyState);
            lobbyChatUsersMock.TryAdd(lobbyCode, new ConcurrentDictionary<string, IChatCallback>());
            lobbyChatUsersMock[lobbyCode].TryAdd(sender, senderCallbackMock.Object);
            lobbyChatUsersMock[lobbyCode].TryAdd(recipient, recipientCallbackMock.Object);
            lobbyChatHistoryMock.TryAdd(lobbyCode, new List<ChatMessageDto>());

            lobbyModerationManager.initializeLobby(lobbyCode);

            await chatLogic.processAndBroadcastMessageAsync(sender, lobbyCode, "Test message");

            senderCallbackMock.Verify(c => c.receiveLobbyMessage(It.IsAny<ChatMessageDto>()), Times.Once);
            recipientCallbackMock.Verify(c => c.receiveLobbyMessage(It.IsAny<ChatMessageDto>()), Times.Once);
        }

        [Fact]
        public async Task ProcessAndBroadcastMessageAsyncWithMaxLengthMessage()
        {
            const string lobbyCode = "CODE";
            const string username = "User";
            string longMessage = new string('a', 200);

            var callbackMock = CreateMockCallback();
            var lobbyState = new LobbyStateDto { LobbyId = lobbyCode, HostUsername = username, Players = new List<string> { username } };

            activeLobbyMock.TryAdd(lobbyCode, lobbyState);
            lobbyChatUsersMock.TryAdd(lobbyCode, new ConcurrentDictionary<string, IChatCallback>());
            lobbyChatUsersMock[lobbyCode].TryAdd(username, callbackMock.Object);
            lobbyChatHistoryMock.TryAdd(lobbyCode, new List<ChatMessageDto>());

            lobbyModerationManager.initializeLobby(lobbyCode);

            await chatLogic.processAndBroadcastMessageAsync(username, lobbyCode, longMessage);

            callbackMock.Verify(c => c.receiveLobbyMessage(It.IsAny<ChatMessageDto>()), Times.Once);
            Assert.Single(lobbyChatHistoryMock[lobbyCode]);
        }

        [Fact]
        public async Task ProcessAndBroadcastMessageAsyncExceedsMaxLength_DoesNotBroadcast()
        {
            const string lobbyCode = "CODE";
            const string username = "User";
            string tooLongMessage = new string('a', 501); 

            var callbackMock = CreateMockCallback();
            var lobbyState = new LobbyStateDto { LobbyId = lobbyCode, HostUsername = username, Players = new List<string> { username } };

            activeLobbyMock.TryAdd(lobbyCode, lobbyState);
            lobbyChatUsersMock.TryAdd(lobbyCode, new ConcurrentDictionary<string, IChatCallback>());
            lobbyChatUsersMock[lobbyCode].TryAdd(username, callbackMock.Object);
            lobbyChatHistoryMock.TryAdd(lobbyCode, new List<ChatMessageDto>());

            lobbyModerationManager.initializeLobby(lobbyCode);

            await chatLogic.processAndBroadcastMessageAsync(username, lobbyCode, tooLongMessage);

            callbackMock.Verify(c => c.receiveLobbyMessage(It.IsAny<ChatMessageDto>()), Times.AtLeastOnce);
        }

        [Fact]
        public void JoinLobbyChatAddsUserToLobby()
        {
            const string username = "User";
            const string lobbyCode = "CODE";
            var callbackMock = CreateMockCallback();
            var lobbyState = new LobbyStateDto { LobbyId = lobbyCode, HostUsername = username, Players = new List<string> { username } };

            activeLobbyMock.TryAdd(lobbyCode, lobbyState);
            lobbyChatUsersMock.TryAdd(lobbyCode, new ConcurrentDictionary<string, IChatCallback>());
            lobbyChatHistoryMock.TryAdd(lobbyCode, new List<ChatMessageDto>());

            chatLogic.joinLobbyChat(username, lobbyCode, callbackMock.Object);

            Assert.True(lobbyChatUsersMock[lobbyCode].ContainsKey(username));
            Assert.Equal(callbackMock.Object, lobbyChatUsersMock[lobbyCode][username]);
        }

        [Fact]
        public void LeaveLobbyRemovesUserFromLobby()
        {
            const string username = "User";
            const string lobbyCode = "CODE";
            var callbackMock = CreateMockCallback();
            var lobbyState = new LobbyStateDto { LobbyId = lobbyCode, HostUsername = username, Players = new List<string> { username } };

            activeLobbyMock.TryAdd(lobbyCode, lobbyState);
            lobbyChatUsersMock.TryAdd(lobbyCode, new ConcurrentDictionary<string, IChatCallback>());
            lobbyChatHistoryMock.TryAdd(lobbyCode, new List<ChatMessageDto>());

            chatLogic.joinLobbyChat(username, lobbyCode, callbackMock.Object);
            Assert.True(lobbyChatUsersMock[lobbyCode].ContainsKey(username));

            chatLogic.leaveLobbyChat(username, lobbyCode);

            if (lobbyChatUsersMock.ContainsKey(lobbyCode))
            {
                Assert.False(lobbyChatUsersMock[lobbyCode].ContainsKey(username));
            }
        }

        [Fact]
        public async Task ProcessAndBroadcastMessageAsyncWithClosedCommunicationState()
        {
            const string lobbyCode = "CODE";
            const string username = "User";
            const string messageContent = "Test message";

            var closedCallbackMock = CreateMockCallbackClosedState();
            var openCallbackMock = CreateMockCallback();
            var lobbyState = new LobbyStateDto { LobbyId = lobbyCode, HostUsername = username, Players = new List<string> { username, "Host" } };

            activeLobbyMock.TryAdd(lobbyCode, lobbyState);
            lobbyChatUsersMock.TryAdd(lobbyCode, new ConcurrentDictionary<string, IChatCallback>());
            lobbyChatUsersMock[lobbyCode].TryAdd(username, closedCallbackMock.Object);
            lobbyChatUsersMock[lobbyCode].TryAdd("Host", openCallbackMock.Object);
            lobbyChatHistoryMock.TryAdd(lobbyCode, new List<ChatMessageDto>());

            lobbyModerationManager.initializeLobby(lobbyCode);

            await chatLogic.processAndBroadcastMessageAsync(username, lobbyCode, messageContent);

            closedCallbackMock.Verify(c => c.receiveLobbyMessage(It.IsAny<ChatMessageDto>()), Times.Never);
            openCallbackMock.Verify(c => c.receiveLobbyMessage(It.IsAny<ChatMessageDto>()), Times.Once);
        }

        [Fact]
        public async Task ProcessAndBroadcastMessageAsyncMultipleProfanityStrikesExpelUser()
        {
            const string lobbyCode = "CODE";
            const string username = "User";
            const string host = "Host";
            const string profaneContent = "badword";

            var userCallbackMock = CreateMockCallback();
            var hostCallbackMock = CreateMockCallback();
            var lobbyState = new LobbyStateDto { LobbyId = lobbyCode, HostUsername = host, Players = new List<string> { username, host } };

            activeLobbyMock.TryAdd(lobbyCode, lobbyState);
            lobbyChatUsersMock.TryAdd(lobbyCode, new ConcurrentDictionary<string, IChatCallback>());
            lobbyChatUsersMock[lobbyCode].TryAdd(username, userCallbackMock.Object);
            lobbyChatUsersMock[lobbyCode].TryAdd(host, hostCallbackMock.Object);
            lobbyChatHistoryMock.TryAdd(lobbyCode, new List<ChatMessageDto>());

            lobbyModerationManager.initializeLobby(lobbyCode);

            await chatLogic.processAndBroadcastMessageAsync(username, lobbyCode, profaneContent);
            await chatLogic.processAndBroadcastMessageAsync(username, lobbyCode, profaneContent);
            await chatLogic.processAndBroadcastMessageAsync(username, lobbyCode, profaneContent);

            playerExpulsionServiceMock.Verify(p => p.expelPlayerAsync(lobbyCode, username, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ProcessAndBroadcastMessageAsyncNullUsernameThrowsException()
        {
            const string lobbyCode = "CODE";
            lobbyModerationManager.initializeLobby(lobbyCode);
            await Assert.ThrowsAsync<ArgumentNullException>(() => chatLogic.processAndBroadcastMessageAsync(null, lobbyCode, "Test message"));
        }

        [Fact]
        public async Task ProcessAndBroadcastMessageAsyncNullLobbyIdThrowsException()
        {
            const string username = "User";
            const string messageContent = "Test message";
            await Assert.ThrowsAsync<ArgumentNullException>(() => chatLogic.processAndBroadcastMessageAsync(username, null, messageContent));
        }

        [Fact]
        public void JoinLobbyChatWithNullUsernameThrowsException()
        {
            const string lobbyCode = "CODE";
            var callbackMock = CreateMockCallback();
            Assert.Throws<ArgumentNullException>(() => chatLogic.joinLobbyChat(null, lobbyCode, callbackMock.Object));
        }

        [Fact]
        public void JoinLobbyChatWithNullCallbackThrowsException()
        {
            const string username = "User";
            const string lobbyCode = "CODE";
            Assert.Throws<ArgumentNullException>(() => chatLogic.joinLobbyChat(username, lobbyCode, null));
        }
    }
}