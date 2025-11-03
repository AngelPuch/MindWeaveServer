using MindWeaveServer.Contracts.DataContracts.Chat;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using NLog;

namespace MindWeaveServer.BusinessLogic
{
    public class ChatLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> lobbyChatUsers;
        private readonly ConcurrentDictionary<string, List<ChatMessageDto>> lobbyChatHistory;

        private const int MAX_HISTORY_PER_LOBBY = 50;
        private const int MAX_MESSAGE_LENGTH = 200;


        public ChatLogic(
            ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> lobbyUsers,
            ConcurrentDictionary<string, List<ChatMessageDto>> lobbyHistory)
        {
            this.lobbyChatUsers = lobbyUsers;
            this.lobbyChatHistory = lobbyHistory;
        }

        public void joinLobbyChat(string username, string lobbyId, IChatCallback userCallback)
        {
            logger.Info("joinLobbyChat called for User: {Username}, Lobby: {LobbyId}", username ?? "NULL", lobbyId ?? "NULL");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId) || userCallback == null)
            {
                logger.Warn("joinLobbyChat ignored: Invalid parameters (username, lobbyId, or callback is null/whitespace).");
                return;
            }

            registerUserCallback(username, lobbyId, userCallback); 
            sendLobbyHistoryToUser(username, lobbyId, userCallback);
        }

        public void leaveLobbyChat(string username, string lobbyId)
        {
            logger.Info("LeaveLobbyChat called for User: {Username}, Lobby: {LobbyId}", username ?? "NULL", lobbyId ?? "NULL");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId))
            {
                logger.Warn("LeaveLobbyChat ignored: Username or LobbyId is null/whitespace.");
                return;
            }

            if (!lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                logger.Warn("[ChatLogic LEAVE] Lobby '{LobbyId}' not found during leave attempt for user '{Username}'.", lobbyId, username);
                return;
            }

            if (!usersInLobby.TryRemove(username, out _))
            {
                logger.Warn("[ChatLogic LEAVE] User '{Username}' was not found in lobby '{LobbyId}' during leave attempt.", username, lobbyId);
                return;
            }

            logger.Info("User {Username} successfully removed from chat lobby {LobbyId}", username, lobbyId);

            if (usersInLobby.IsEmpty)
            {
                cleanUpEmptyLobby(lobbyId);
            }
        }

        public void processAndBroadcastMessage(string senderUsername, string lobbyId, string messageContent)
        {
            logger.Info("processAndBroadcastMessage called by User: {Username} in Lobby: {LobbyId}", senderUsername ?? "NULL", lobbyId ?? "NULL");

            if (string.IsNullOrWhiteSpace(senderUsername) || string.IsNullOrWhiteSpace(lobbyId) || string.IsNullOrWhiteSpace(messageContent))
            {
                logger.Warn("[ChatLogic SEND FAILED] Invalid parameters for sending message (sender, lobby, or content is null/whitespace).");
                return;
            }

            if (messageContent.Length > MAX_MESSAGE_LENGTH)
            {
                logger.Debug("Message from {Username} in lobby {LobbyId} exceeds max length ({MaxLength}). Truncating.", senderUsername, lobbyId, MAX_MESSAGE_LENGTH);
                messageContent = messageContent.Substring(0, MAX_MESSAGE_LENGTH) + "...";
            }

            var messageDto = new ChatMessageDto
            {
                senderUsername = senderUsername,
                content = messageContent,
                timestamp = DateTime.UtcNow 
            };

            addMessageToHistory(lobbyId, messageDto);
            broadcastMessage(lobbyId, messageDto);
        }

        private void addMessageToHistory(string lobbyId, ChatMessageDto messageDto)
        {
            var history = lobbyChatHistory.GetOrAdd(lobbyId, id => {
                logger.Debug("Creating new chat history list for lobby: {LobbyId}", id);
                return new List<ChatMessageDto>();
            });

            int countAfterAdd;
            lock (history) 
            {
                history.Add(messageDto);
                while (history.Count > MAX_HISTORY_PER_LOBBY)
                {
                    history.RemoveAt(0);
                }
                countAfterAdd = history.Count;
            }
            logger.Debug("[ChatLogic HISTORY] Message from {SenderUsername} added to history for lobby '{LobbyId}'. New Count: {HistoryCount}", messageDto.senderUsername, lobbyId, countAfterAdd);
        }

        private void broadcastMessage(string lobbyId, ChatMessageDto messageDto)
        {
            logger.Debug("Broadcasting message from {SenderUsername} to lobby {LobbyId}", messageDto.senderUsername, lobbyId);

            if (!lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                logger.Warn("[ChatLogic BROADCAST WARNING] Lobby '{LobbyId}' not found for broadcast (might have been cleaned up).", lobbyId);
                return;
            }

            var currentUsersSnapshot = usersInLobby.ToList();
            logger.Debug("Attempting to broadcast to {Count} users in lobby {LobbyId}", currentUsersSnapshot.Count, lobbyId);
            List<string> usersToRemove = sendMessagesToUsers(currentUsersSnapshot, messageDto, lobbyId);

            if (usersToRemove.Any())
            {
                handleFailedBroadcastUsers(usersToRemove, usersInLobby, lobbyId);
            }
        }

        private void registerUserCallback(string username, string lobbyId, IChatCallback userCallback)
        {
            var usersInLobby = lobbyChatUsers.GetOrAdd(lobbyId, id =>
            {
                logger.Debug("Creating new user list for lobby: {LobbyId}", id);
                return new ConcurrentDictionary<string, IChatCallback>(StringComparer.OrdinalIgnoreCase);
            });

            usersInLobby.AddOrUpdate(username, userCallback, (key, existingVal) =>
            {
                var existingComm = existingVal as ICommunicationObject;

                if (existingVal != userCallback && (existingComm == null || existingComm.State != CommunicationState.Opened))
                {
                    logger.Warn("Replacing existing non-opened chat callback for User: {Username} in Lobby: {LobbyId}", key, lobbyId);
                    return userCallback;
                }

                if (existingVal != userCallback)
                {
                    logger.Debug("Keeping existing OPEN chat callback for User: {Username} in Lobby: {LobbyId}", key, lobbyId);
                }
                else
                {
                    logger.Debug("Updating existing chat callback (same instance) for User: {Username} in Lobby: {LobbyId}", key, lobbyId);
                }

                return existingVal;
            });

            logger.Info("User {Username} added/updated in chat lobby {LobbyId}", username, lobbyId);
        }

        private void sendLobbyHistoryToUser(string username, string lobbyId, IChatCallback userCallback)
        {
            if (lobbyChatHistory.TryGetValue(lobbyId, out var history))
            {
                List<ChatMessageDto> historySnapshot;
                lock (history)
                {
                    historySnapshot = history.ToList();
                }

                logger.Debug("Sending {Count} historical messages to User: {Username} for Lobby: {LobbyId}", historySnapshot.Count, username, lobbyId);

                foreach (var msg in historySnapshot)
                {
                    try
                    {
                        var commObject = userCallback as ICommunicationObject;
                        if (commObject != null && commObject.State == CommunicationState.Opened)
                        {
                            userCallback.receiveLobbyMessage(msg);
                        }
                        else
                        {
                            logger.Warn("[ChatLogic JOIN] Callback channel for {Username} not open while sending history. Aborting history send. State: {State}", username, commObject?.State);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "[ChatLogic JOIN] Exception sending history message to {Username} for Lobby: {LobbyId}. Continuing...", username, lobbyId);
                    }
                }
                logger.Debug("Finished sending history to User: {Username}", username);
            }
            else
            {
                logger.Debug("No chat history found for Lobby: {LobbyId} to send to User: {Username}", lobbyId, username);
            }
        }

        private void cleanUpEmptyLobby(string lobbyId)
        {
            logger.Info("Chat lobby {LobbyId} is now empty. Removing user list and history.", lobbyId);

            if (!lobbyChatUsers.TryRemove(lobbyId, out _))
            {
                logger.Warn("Could not remove user list for empty lobby {LobbyId} (might have been removed already).", lobbyId);
            }

            if (lobbyChatHistory.TryRemove(lobbyId, out _))
            {
                logger.Debug("Successfully removed history for empty lobby {LobbyId}", lobbyId);
            }
            else
            {
                logger.Warn("Could not remove history for empty lobby {LobbyId} (might have been removed already).", lobbyId);
            }
        }

        private List<string> sendMessagesToUsers(List<KeyValuePair<string, IChatCallback>> usersSnapshot, ChatMessageDto messageDto, string lobbyId)
        {
            var usersToRemove = new List<string>();

            foreach (var userEntry in usersSnapshot)
            {
                string recipientUsername = userEntry.Key;
                IChatCallback recipientCallback = userEntry.Value;

                try
                {
                    var commObject = recipientCallback as ICommunicationObject;
                    if (commObject != null && commObject.State == CommunicationState.Opened)
                    {
                        recipientCallback.receiveLobbyMessage(messageDto);
                        logger.Debug("  -> Sent chat message to {RecipientUsername} in lobby {LobbyId}", recipientUsername, lobbyId);
                    }
                    else
                    {
                        logger.Warn("  -> FAILED sending chat message to {RecipientUsername} (Channel State: {State}). Marking for removal.", recipientUsername, commObject?.State);
                        usersToRemove.Add(recipientUsername);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "  -> EXCEPTION sending chat message to {RecipientUsername}. Marking for removal.", recipientUsername);
                    usersToRemove.Add(recipientUsername);
                }
            }

            return usersToRemove;
        }

        private void handleFailedBroadcastUsers(List<string> usersToRemove, ConcurrentDictionary<string, IChatCallback> usersInLobby, string lobbyId)
        {
            logger.Warn("Found {Count} users with failed channels during broadcast in lobby {LobbyId}. Removing them...", usersToRemove.Count, lobbyId);

            foreach (var userToRemove in usersToRemove)
            {
                if (usersInLobby.TryRemove(userToRemove, out _))
                {
                    logger.Info("[ChatLogic BROADCAST CLEANUP] Removed user {UserToRemove} from chat lobby {LobbyId} due to channel issue.", userToRemove, lobbyId);
                }
            }

            if (usersInLobby.IsEmpty)
            {
                logger.Info("Chat lobby {LobbyId} became empty after broadcast cleanup. Removing lobby resources.", lobbyId);
                cleanUpEmptyLobby(lobbyId);
            }
        }
    }
}