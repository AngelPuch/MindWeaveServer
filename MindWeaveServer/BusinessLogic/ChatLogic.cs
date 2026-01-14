using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.Contracts.DataContracts.Chat;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.Utilities;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Entity.Core;
using System.Data.SqlClient;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class ChatLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IGameStateManager gameStateManager;
        private readonly IPlayerExpulsionService expulsionService;
        private readonly LobbyModerationManager moderationManager;

        private const int MAX_HISTORY_PER_LOBBY = 50;
        private const int MAX_MESSAGE_LENGTH = 200;
        private const int MAX_STRIKES_BEFORE_EXPULSION = 3;
        private const string EXPULSION_REASON_PROFANITY = "Profanity";
        private const string MESSAGE_TRUNCATION_SUFFIX = "...";

        public ChatLogic(
            IGameStateManager gameStateManager,
            IPlayerExpulsionService expulsionService,
            LobbyModerationManager moderationManager)
        {
            this.gameStateManager = gameStateManager;
            this.expulsionService = expulsionService;
            this.moderationManager = moderationManager;
        }

        private ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> lobbyChatUsers =>
            gameStateManager.LobbyChatUsers;

        private ConcurrentDictionary<string, List<ChatMessageDto>> lobbyChatHistory =>
            gameStateManager.LobbyChatHistory;

        public void joinLobbyChat(string username, string lobbyId, IChatCallback userCallback)
        {
            validateJoinParameters(username, lobbyId, userCallback);

            registerUserCallback(username, lobbyId, userCallback);
            sendLobbyHistoryToUser(lobbyId, userCallback);
        }

        public void leaveLobbyChat(string username, string lobbyId)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId))
            {
                logger.Warn("leaveLobbyChat ignored: Username or LobbyId is null/whitespace.");
                return;
            }

            if (!tryRemoveUserFromLobby(username, lobbyId, out bool lobbyIsEmpty))
            {
                return;
            }

            if (lobbyIsEmpty)
            {
                cleanUpEmptyLobby(lobbyId);
            }
        }

        public async Task processAndBroadcastMessageAsync(
            string senderUsername,
            string lobbyId,
            string messageContent)
        {
            validateMessageParameters(senderUsername, lobbyId, messageContent);

            logger.Debug("Processing message in lobby {LobbyId}", lobbyId);

            string sanitizedContent = sanitizeMessageContent(messageContent);

            if (await handleProfanityCheckAsync(senderUsername, lobbyId, sanitizedContent))
            {
                return;
            }

            var messageDto = createMessageDto(senderUsername, sanitizedContent);

            addMessageToHistory(lobbyId, messageDto);
            broadcastMessage(lobbyId, messageDto);
        }

        private static void validateJoinParameters(string username, string lobbyId, IChatCallback userCallback)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentNullException(nameof(username));
            }

            if (string.IsNullOrWhiteSpace(lobbyId))
            {
                throw new ArgumentNullException(nameof(lobbyId));
            }

            if (userCallback == null)
            {
                throw new ArgumentNullException(nameof(userCallback));
            }
        }

        private static void validateMessageParameters(string senderUsername, string lobbyId, string messageContent)
        {
            if (string.IsNullOrWhiteSpace(senderUsername))
            {
                throw new ArgumentNullException(nameof(senderUsername));
            }

            if (string.IsNullOrWhiteSpace(lobbyId))
            {
                throw new ArgumentNullException(nameof(lobbyId));
            }

            if (string.IsNullOrWhiteSpace(messageContent))
            {
                throw new ArgumentException("Message content cannot be empty.", nameof(messageContent));
            }
        }

        private async Task<bool> handleProfanityCheckAsync(
            string senderUsername,
            string lobbyId,
            string messageContent)
        {
            if (!ProfanityFilter.containsProfanity(messageContent))
            {
                return false;
            }

            int strikes = moderationManager.addStrike(lobbyId, senderUsername);

            if (strikes >= MAX_STRIKES_BEFORE_EXPULSION)
            {
                await executePlayerExpulsionAsync(lobbyId, senderUsername);
            }
            else
            {
                sendStrikeWarningToUser(senderUsername, lobbyId, strikes);
            }

            return true;
        }

        private async Task executePlayerExpulsionAsync(string lobbyId, string senderUsername)
        {
            try
            {
                await expulsionService.expelPlayerAsync(lobbyId, senderUsername, EXPULSION_REASON_PROFANITY);
            }
            catch (EntityException dbEx)
            {
                logger.Error(dbEx, "Database error expelling player from lobby {LobbyId}", lobbyId);
            }
            catch (SqlException sqlEx)
            {
                logger.Error(sqlEx, "SQL error expelling player from lobby {LobbyId}", lobbyId);
            }
        }

        private void sendStrikeWarningToUser(string username, string lobbyId, int strikes)
        {
            IChatCallback senderCallback = findUserCallback(username, lobbyId);

            if (senderCallback == null)
            {
                return;
            }

            try
            {
                string warningMessage = $"{MessageCodes.CHAT_PROFANITY_WARNING}:{strikes}";
                senderCallback.receiveSystemMessage(warningMessage);
            }
            catch (CommunicationException commEx)
            {
                logger.Debug(commEx, "Failed to send strike warning in lobby {LobbyId}. Connection lost.", lobbyId);
            }
            catch (TimeoutException timeoutEx)
            {
                logger.Debug(timeoutEx, "Failed to send strike warning in lobby {LobbyId}. Timeout.", lobbyId);
            }
        }

        private IChatCallback findUserCallback(string username, string lobbyId)
        {
            if (!lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                return null;
            }

            usersInLobby.TryGetValue(username, out IChatCallback callback);
            return callback;
        }

        private void registerUserCallback(string username, string lobbyId, IChatCallback userCallback)
        {
            var usersInLobby = lobbyChatUsers.GetOrAdd(lobbyId, _ =>
                new ConcurrentDictionary<string, IChatCallback>(StringComparer.OrdinalIgnoreCase));

            usersInLobby.AddOrUpdate(
                username,
                userCallback,
                (key, existingCallback) => selectBestCallback(lobbyId, existingCallback, userCallback));
        }

        private static IChatCallback selectBestCallback(string lobbyId, IChatCallback existing, IChatCallback incoming)
        {
            if (existing == incoming)
            {
                return existing;
            }

            var existingComm = existing as ICommunicationObject;
            bool existingIsOpen = existingComm?.State == CommunicationState.Opened;

            if (!existingIsOpen)
            {
                logger.Warn("Replacing closed callback in lobby {LobbyId}", lobbyId);
                return incoming;
            }

            return existing;
        }

        private bool tryRemoveUserFromLobby(string username, string lobbyId, out bool lobbyIsEmpty)
        {
            lobbyIsEmpty = false;

            if (!lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                logger.Warn("Lobby {LobbyId} not found during leave attempt.", lobbyId);
                return false;
            }

            if (!usersInLobby.TryRemove(username, out _))
            {
                logger.Warn("User was not found in lobby {LobbyId}", lobbyId);
                return false;
            }

            lobbyIsEmpty = usersInLobby.IsEmpty;

            return true;
        }

        private void cleanUpEmptyLobby(string lobbyId)
        {
            lobbyChatUsers.TryRemove(lobbyId, out _);
            lobbyChatHistory.TryRemove(lobbyId, out _);
        }

        private static string sanitizeMessageContent(string messageContent)
        {
            if (messageContent.Length <= MAX_MESSAGE_LENGTH)
            {
                return messageContent;
            }

            return messageContent.Substring(0, MAX_MESSAGE_LENGTH) + MESSAGE_TRUNCATION_SUFFIX;
        }

        private static ChatMessageDto createMessageDto(string senderUsername, string content)
        {
            return new ChatMessageDto
            {
                SenderUsername = senderUsername,
                Content = content,
                Timestamp = DateTime.UtcNow
            };
        }

        private void addMessageToHistory(string lobbyId, ChatMessageDto messageDto)
        {
            var history = lobbyChatHistory.GetOrAdd(lobbyId, _ => new List<ChatMessageDto>());

            lock (history)
            {
                history.Add(messageDto);

                while (history.Count > MAX_HISTORY_PER_LOBBY)
                {
                    history.RemoveAt(0);
                }
            }
        }

        private void sendLobbyHistoryToUser(string lobbyId, IChatCallback userCallback)
        {
            if (!lobbyChatHistory.TryGetValue(lobbyId, out var history))
            {
                return;
            }

            List<ChatMessageDto> historySnapshot;
            lock (history)
            {
                historySnapshot = history.ToList();
            }

            foreach (var message in historySnapshot)
            {
                trySendHistoryMessage(userCallback, message, lobbyId);
            }
        }

        private static void trySendHistoryMessage(IChatCallback callback, ChatMessageDto message, string lobbyId)
        {
            var commObject = callback as ICommunicationObject;
            if (commObject?.State != CommunicationState.Opened)
            {
                logger.Warn("Callback not open for lobby {LobbyId}. Aborting history send.", lobbyId);
                return;
            }

            try
            {
                callback.receiveLobbyMessage(message);
            }
            catch (CommunicationException commEx)
            {
                logger.Warn(commEx, "Connection lost sending history to lobby {LobbyId}", lobbyId);
            }
            catch (TimeoutException timeEx)
            {
                logger.Warn(timeEx, "Timeout sending history to lobby {LobbyId}", lobbyId);
            }
            catch (ObjectDisposedException dispEx)
            {
                logger.Warn(dispEx, "Channel disposed sending history to lobby {LobbyId}", lobbyId);
            }
        }

        private void broadcastMessage(string lobbyId, ChatMessageDto messageDto)
        {
            if (!lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                logger.Warn("Lobby {LobbyId} not found for broadcast.", lobbyId);
                return;
            }

            var usersSnapshot = usersInLobby.ToList();
            var failedUsers = sendMessageToUsers(usersSnapshot, messageDto);

            if (failedUsers.Any())
            {
                removeFailedUsers(failedUsers, usersInLobby, lobbyId);
            }
        }

        private static List<string> sendMessageToUsers(List<KeyValuePair<string, IChatCallback>> usersSnapshot, ChatMessageDto messageDto)
        {
            return (from userEntry in usersSnapshot
                    where !trySendMessageToUser(userEntry.Value, messageDto)
                    select userEntry.Key).ToList();
        }

        private static bool trySendMessageToUser(IChatCallback callback, ChatMessageDto messageDto)
        {
            var commObject = callback as ICommunicationObject;
            if (commObject?.State != CommunicationState.Opened)
            {
                return false;
            }

            try
            {
                callback.receiveLobbyMessage(messageDto);
                return true;
            }
            catch (CommunicationException)
            {
                return false;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private void removeFailedUsers(List<string> failedUsers, ConcurrentDictionary<string, IChatCallback> usersInLobby,
            string lobbyId)
        {
            logger.Warn("Removing {Count} users with failed channels from lobby {LobbyId}",
                failedUsers.Count, lobbyId);

            foreach (var username in failedUsers)
            {
                usersInLobby.TryRemove(username, out _);
            }

            if (usersInLobby.IsEmpty)
            {
                cleanUpEmptyLobby(lobbyId);
            }
        }
    }
}