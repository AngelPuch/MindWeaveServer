
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Chat;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class ChatManagerService : IChatManager
    {
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> lobbyChatUsers =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, List<ChatMessageDto>> lobbyChatHistory =
            new ConcurrentDictionary<string, List<ChatMessageDto>>(StringComparer.OrdinalIgnoreCase);
        private const int MAX_HISTORY_PER_LOBBY = 50;

        private string currentUsername = null;
        private string currentLobbyId = null;
        private IChatCallback currentUserCallback = null;

        public ChatManagerService()
        {
            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += Channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed += Channel_FaultedOrClosed;
            }
        }

        public Task joinLobbyChat(string username, string lobbyId)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId) || OperationContext.Current == null)
            {
                Console.WriteLine($"[Chat JOIN FAILED] Invalid parameters or context. User: {username}, Lobby: {lobbyId}");
                return Task.CompletedTask;
            }

            currentUserCallback = OperationContext.Current.GetCallbackChannel<IChatCallback>();
            if (currentUserCallback == null)
            {
                Console.WriteLine($"[Chat JOIN FAILED] Could not get callback channel for {username}.");
                return Task.CompletedTask;
            }

            currentUsername = username;
            currentLobbyId = lobbyId;

            var usersInLobby = lobbyChatUsers.GetOrAdd(lobbyId, new ConcurrentDictionary<string, IChatCallback>(StringComparer.OrdinalIgnoreCase));

            usersInLobby.AddOrUpdate(username, currentUserCallback, (key, existingVal) => currentUserCallback);

            Console.WriteLine($"[Chat JOIN] User '{username}' joined chat for lobby '{lobbyId}'.");

            if (lobbyChatHistory.TryGetValue(lobbyId, out var history))
            {
                foreach (var msg in history)
                {
                    try { currentUserCallback.receiveLobbyMessage(msg); } catch { /* Ignore issues sending history */ }
                }
            }

            return Task.CompletedTask;
        }

        public Task leaveLobbyChat(string username, string lobbyId)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId))
            {
                return Task.CompletedTask;
            }

            if (lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                if (usersInLobby.TryRemove(username, out _))
                {
                    Console.WriteLine($"[Chat LEAVE] User '{username}' left chat for lobby '{lobbyId}'.");
                    
                    if (usersInLobby.IsEmpty)
                    {
                        lobbyChatUsers.TryRemove(lobbyId, out _);
                        lobbyChatHistory.TryRemove(lobbyId, out _);
                        Console.WriteLine($"[Chat CLEANUP] Lobby '{lobbyId}' chat resources released.");
                    }
                }
            }

            if (username.Equals(currentUsername, StringComparison.OrdinalIgnoreCase) && lobbyId.Equals(currentLobbyId, StringComparison.OrdinalIgnoreCase))
            {
                currentUsername = null;
                currentLobbyId = null;
                currentUserCallback = null;
            }


            return Task.CompletedTask;
        }

        public Task sendLobbyMessage(string senderUsername, string lobbyId, string messageContent)
        {
            if (string.IsNullOrWhiteSpace(senderUsername) || string.IsNullOrWhiteSpace(lobbyId) || string.IsNullOrWhiteSpace(messageContent))
            {
                return Task.CompletedTask;
            }

            if (messageContent.Length > 200)
            {
                messageContent = messageContent.Substring(0, 200) + "...";
            }

            var messageDto = new ChatMessageDto
            {
                senderUsername = senderUsername,
                content = messageContent,
                timestamp = DateTime.UtcNow
            };

            var history = lobbyChatHistory.GetOrAdd(lobbyId, new List<ChatMessageDto>());
            lock (history) 
            {
                history.Add(messageDto);
                if (history.Count > MAX_HISTORY_PER_LOBBY)
                {
                    history.RemoveAt(0);
                }
            }

            broadcastMessage(lobbyId, messageDto);

            return Task.CompletedTask;
        }

        private void broadcastMessage(string lobbyId, ChatMessageDto messageDto)
        {
            Console.WriteLine($"[Chat BROADCAST] Lobby '{lobbyId}', Sender: {messageDto.senderUsername}, Msg: '{messageDto.content}'");
            if (lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                foreach (var userEntry in usersInLobby)
                {
                    string recipientUsername = userEntry.Key;
                    IChatCallback recipientCallback = userEntry.Value;
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
                            Console.WriteLine($"  -> FAILED sending to {recipientUsername} (Channel State: {commObject?.State}). Removing.");
                            usersInLobby.TryRemove(recipientUsername, out _);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  -> EXCEPTION sending to {recipientUsername}: {ex.Message}. Removing.");
                        usersInLobby.TryRemove(recipientUsername, out _);
                    }
                }

                if (usersInLobby.IsEmpty)
                {
                    lobbyChatUsers.TryRemove(lobbyId, out _);
                    lobbyChatHistory.TryRemove(lobbyId, out _);
                    Console.WriteLine($"[Chat CLEANUP] Lobby '{lobbyId}' chat resources released after broadcast failures.");
                }
            }
            else
            {
                Console.WriteLine($"[Chat BROADCAST WARNING] Lobby '{lobbyId}' not found in chat users dictionary.");
            }
        }

        private void Channel_FaultedOrClosed(object sender, EventArgs e)
        {
            Console.WriteLine($"[Chat Channel Event] Faulted or Closed detected for User: '{currentUsername}', Lobby: '{currentLobbyId}'");
            if (!string.IsNullOrEmpty(currentUsername) && !string.IsNullOrEmpty(currentLobbyId))
            {
                Task.Run(() => leaveLobbyChat(currentUsername, currentLobbyId));
            }
            cleanupCallbackEvents(sender as ICommunicationObject);
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= Channel_FaultedOrClosed;
                commObject.Closed -= Channel_FaultedOrClosed;
            }
        }
    }
}