using MindWeaveServer.Contracts.DataContracts.Chat;
using MindWeaveServer.Contracts.ServiceContracts;
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
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> lobbyChatUsers =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, List<ChatMessageDto>> lobbyChatHistory =
            new ConcurrentDictionary<string, List<ChatMessageDto>>(StringComparer.OrdinalIgnoreCase);

        private const int MAX_HISTORY_PER_LOBBY = 50;
        private const int MAX_MESSAGE_LENGTH = 200;

        public async Task joinLobbyChat(string username, string lobbyId, IChatCallback userCallback)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId) || userCallback == null)
            {
                return;
            }

            var usersInLobby = lobbyChatUsers.GetOrAdd(lobbyId, _ => new ConcurrentDictionary<string, IChatCallback>(StringComparer.OrdinalIgnoreCase));

            usersInLobby.AddOrUpdate(username, userCallback, (key, existingVal) =>
            {
                var existingComm = existingVal as ICommunicationObject;
                if (existingVal != userCallback && (existingComm == null || existingComm.State != CommunicationState.Opened))
                {
                    return userCallback;
                }
                return existingVal;
            });

            if (lobbyChatHistory.TryGetValue(lobbyId, out var history))
            {
                List<ChatMessageDto> historySnapshot;
                lock (history)
                {
                    historySnapshot = history.ToList();
                }

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
                            Console.WriteLine($"[ChatLogic JOIN] Callback channel for {username} not open while sending history. Aborting history send.");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ChatLogic JOIN] Exception sending history message to {username}: {ex.Message}. Continuing...");
                    }
                }
            }
        }

        public void leaveLobbyChat(string username, string lobbyId)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId))
            {
                return;
            }

            if (lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                if (usersInLobby.TryRemove(username, out _))
                {
                    if (usersInLobby.IsEmpty)
                    {
                        if (lobbyChatUsers.TryRemove(lobbyId, out _))
                        {
                            lobbyChatHistory.TryRemove(lobbyId, out _);
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[ChatLogic LEAVE] User '{username}' was not found in lobby '{lobbyId}' during leave attempt.");
                }
            }
            else
            {
                Console.WriteLine($"[ChatLogic LEAVE] Lobby '{lobbyId}' not found during leave attempt for user '{username}'.");
            }
        }

        public void processAndBroadcastMessage(string senderUsername, string lobbyId, string messageContent)
        {
            if (string.IsNullOrWhiteSpace(senderUsername) || string.IsNullOrWhiteSpace(lobbyId) || string.IsNullOrWhiteSpace(messageContent))
            {
                Console.WriteLine("[ChatLogic SEND FAILED] Invalid parameters for sending message.");
                return;
            }

            if (messageContent.Length > MAX_MESSAGE_LENGTH)
            {
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
            var history = lobbyChatHistory.GetOrAdd(lobbyId, _ => new List<ChatMessageDto>());

            lock (history)
            {
                history.Add(messageDto);
                while (history.Count > MAX_HISTORY_PER_LOBBY)
                {
                    history.RemoveAt(0);
                }
            }
            Console.WriteLine($"[ChatLogic HISTORY] Message from {messageDto.senderUsername} added to history for lobby '{lobbyId}'. Count: {history.Count}");
        }

        private void broadcastMessage(string lobbyId, ChatMessageDto messageDto)
        {
            if (lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                var currentUsersSnapshot = usersInLobby.Keys.ToList();
                List<string> usersToRemove = new List<string>();

                foreach (string recipientUsername in currentUsersSnapshot)
                {
                    if (usersInLobby.TryGetValue(recipientUsername, out IChatCallback recipientCallback))
                    {
                        try
                        {
                            var commObject = recipientCallback as ICommunicationObject;
                            if (commObject != null && commObject.State == CommunicationState.Opened)
                            {
                                recipientCallback.receiveLobbyMessage(messageDto);
                                Console.WriteLine($"  -> Sent to {recipientUsername}");
                            }
                            else
                            {
                                Console.WriteLine($"  -> FAILED sending to {recipientUsername} (Channel State: {commObject?.State}). Marking for removal.");
                                usersToRemove.Add(recipientUsername);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  -> EXCEPTION sending to {recipientUsername}: {ex.Message}. Marking for removal.");
                            usersToRemove.Add(recipientUsername);
                        }
                    }
                }

                if (usersToRemove.Any())
                {
                    foreach (var userToRemove in usersToRemove)
                    {
                        if (usersInLobby.TryRemove(userToRemove, out _))
                        {
                            Console.WriteLine($"[ChatLogic BROADCAST CLEANUP] Removed user {userToRemove} from lobby {lobbyId} due to channel issue.");
                        }
                    }

                    if (usersInLobby.IsEmpty)
                    {
                        if (lobbyChatUsers.TryRemove(lobbyId, out _))
                        {
                            lobbyChatHistory.TryRemove(lobbyId, out _);
                            Console.WriteLine($"[ChatLogic CLEANUP] Lobby '{lobbyId}' chat resources released after broadcast cleanup (lobby empty).");
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine($"[ChatLogic BROADCAST WARNING] Lobby '{lobbyId}' not found for broadcast.");
            }
        }
    }
}