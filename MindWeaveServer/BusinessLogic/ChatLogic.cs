using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Chat;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.Utilities;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private const string STRIKE_WARNING_PREFIX = "WARN_STRIKE:";
        private const string EXPULSION_REASON_PROFANITY = "Profanity";

        public ChatLogic(
            IGameStateManager gameStateManager,
            IPlayerExpulsionService expulsionService,
            LobbyModerationManager moderationManager)
        {
            this.gameStateManager = gameStateManager
                ?? throw new ArgumentNullException(nameof(gameStateManager));
            this.expulsionService = expulsionService
                ?? throw new ArgumentNullException(nameof(expulsionService));
            this.moderationManager = moderationManager
                ?? throw new ArgumentNullException(nameof(moderationManager));
        }

        private ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> lobbyChatUsers
            => gameStateManager.LobbyChatUsers;

        private ConcurrentDictionary<string, List<ChatMessageDto>> lobbyChatHistory
            => gameStateManager.LobbyChatHistory;

        public void joinLobbyChat(string username, string lobbyId, IChatCallback userCallback)
        {
            validateJoinParameters(username, lobbyId, userCallback);

            logger.Info("User joining chat for lobby {LobbyId}", lobbyId);

            registerUserCallback(username, lobbyId, userCallback);
            sendLobbyHistoryToUser(username, lobbyId, userCallback);
        }

        public void leaveLobbyChat(string username, string lobbyId)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId))
            {
                logger.Warn("leaveLobbyChat ignored: Username or LobbyId is null/whitespace.");
                return;
            }

            logger.Info("User leaving chat for lobby {LobbyId}", lobbyId);

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

            if (await handleProfanityCheckAsync(senderUsername, lobbyId, messageContent))
            {
                return;
            }

            string sanitizedContent = sanitizeMessageContent(messageContent);
            var messageDto = createMessageDto(senderUsername, sanitizedContent);

            addMessageToHistory(lobbyId, messageDto);
            broadcastMessage(lobbyId, messageDto);
        }

        private void validateJoinParameters(string username, string lobbyId, IChatCallback userCallback)
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

        private void validateMessageParameters(string senderUsername, string lobbyId, string messageContent)
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
            if (!ProfanityFilter.ContainsProfanity(messageContent))
            {
                return false;
            }

            logger.Warn("Profanity detected in lobby {LobbyId}", lobbyId);

            int strikes = moderationManager.AddStrike(lobbyId, senderUsername);

            if (strikes >= MAX_STRIKES_BEFORE_EXPULSION)
            {
                logger.Info("User reached {MaxStrikes} strikes in lobby {LobbyId}. Initiating expulsion.",
                    MAX_STRIKES_BEFORE_EXPULSION, lobbyId);

                await expulsionService.expelPlayerAsync(lobbyId, senderUsername, EXPULSION_REASON_PROFANITY);
            }
            else
            {
                sendStrikeWarningToUser(senderUsername, lobbyId, strikes);
            }

            return true;
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
                senderCallback.receiveSystemMessage($"{STRIKE_WARNING_PREFIX}{strikes}");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to send strike warning in lobby {LobbyId}", lobbyId);
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

        private string sanitizeMessageContent(string messageContent)
        {
            if (messageContent.Length <= MAX_MESSAGE_LENGTH)
            {
                return messageContent;
            }

            return messageContent.Substring(0, MAX_MESSAGE_LENGTH) + "...";
        }

        private ChatMessageDto createMessageDto(string senderUsername, string content)
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

            logger.Debug("Message added to history for lobby {LobbyId}. Count: {Count}",
                lobbyId, history.Count);
        }

        private void broadcastMessage(string lobbyId, ChatMessageDto messageDto)
        {
            if (!lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                logger.Warn("Lobby {LobbyId} not found for broadcast.", lobbyId);
                return;
            }

            var usersSnapshot = usersInLobby.ToList();
            var failedUsers = sendMessageToUsers(usersSnapshot, messageDto, lobbyId);

            if (failedUsers.Any())
            {
                removeFailedUsers(failedUsers, usersInLobby, lobbyId);
            }
        }

        private void registerUserCallback(string username, string lobbyId, IChatCallback userCallback)
        {
            var usersInLobby = lobbyChatUsers.GetOrAdd(lobbyId, _ =>
                new ConcurrentDictionary<string, IChatCallback>(StringComparer.OrdinalIgnoreCase));

            usersInLobby.AddOrUpdate(
                username,
                userCallback,
                (key, existingCallback) => selectBestCallback(lobbyId, existingCallback, userCallback));

            logger.Info("User registered in chat lobby {LobbyId}", lobbyId);
        }

        private IChatCallback selectBestCallback(
            string lobbyId,
            IChatCallback existing,
            IChatCallback incoming)
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

        private void sendLobbyHistoryToUser(string username, string lobbyId, IChatCallback userCallback)
        {
            if (!lobbyChatHistory.TryGetValue(lobbyId, out var history))
            {
                logger.Debug("No chat history for lobby {LobbyId}", lobbyId);
                return;
            }

            List<ChatMessageDto> historySnapshot;
            lock (history)
            {
                historySnapshot = history.ToList();
            }

            logger.Debug("Sending {Count} historical messages for lobby {LobbyId}", historySnapshot.Count, lobbyId);

            foreach (var message in historySnapshot)
            {
                if (!trySendHistoryMessage(userCallback, message, lobbyId))
                {
                    break;
                }
            }
        }

        private bool trySendHistoryMessage(IChatCallback callback, ChatMessageDto message, string lobbyId)
        {
            var commObject = callback as ICommunicationObject;

            if (commObject?.State != CommunicationState.Opened)
            {
                logger.Warn("Callback not open for lobby {LobbyId}. Aborting history send.", lobbyId);
                return false;
            }

            try
            {
                callback.receiveLobbyMessage(message);
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error sending history for lobby {LobbyId}", lobbyId);
                return true;
            }
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

            logger.Info("User removed from chat lobby {LobbyId}", lobbyId);
            lobbyIsEmpty = usersInLobby.IsEmpty;

            return true;
        }

        private void cleanUpEmptyLobby(string lobbyId)
        {
            logger.Info("Chat lobby {LobbyId} is empty. Cleaning up resources.", lobbyId);

            lobbyChatUsers.TryRemove(lobbyId, out _);
            lobbyChatHistory.TryRemove(lobbyId, out _);
        }

        private List<string> sendMessageToUsers(
            List<KeyValuePair<string, IChatCallback>> usersSnapshot,
            ChatMessageDto messageDto,
            string lobbyId)
        {
            var failedUsers = new List<string>();

            foreach (var userEntry in usersSnapshot)
            {
                if (!trySendMessageToUser(userEntry.Value, lobbyId))
                {
                    failedUsers.Add(userEntry.Key);
                }
                else
                {
                    try
                    {
                        userEntry.Value.receiveLobbyMessage(messageDto);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Exception sending message in lobby {LobbyId}. Marking for removal.", lobbyId);
                        failedUsers.Add(userEntry.Key);
                    }
                }
            }

            return failedUsers;
        }

        private bool trySendMessageToUser(IChatCallback callback, string lobbyId)
        {
            var commObject = callback as ICommunicationObject;

            if (commObject?.State != CommunicationState.Opened)
            {
                logger.Warn("Channel not open in lobby {LobbyId}. Marking for removal.", lobbyId);
                return false;
            }

            return true;
        }

        private void removeFailedUsers(
            List<string> failedUsers,
            ConcurrentDictionary<string, IChatCallback> usersInLobby,
            string lobbyId)
        {
            logger.Warn("Removing {Count} users with failed channels from lobby {LobbyId}",
                failedUsers.Count, lobbyId);

            foreach (var username in failedUsers)
            {
                usersInLobby.TryRemove(username, out _);
            }

            logger.Info("Removed users with failed channels from lobby {LobbyId}", lobbyId);

            if (usersInLobby.IsEmpty)
            {
                cleanUpEmptyLobby(lobbyId);
            }
        }
    }
}