// MindWeaveServer/Services/ChatManagerService.cs
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class ChatManagerService : IChatManager
    {
        // Static dictionaries to track users and messages across all sessions
        // Key: lobbyId, Value: Set of usernames in that lobby's chat
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> lobbyChatUsers =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>>(StringComparer.OrdinalIgnoreCase);

        // Key: lobbyId, Value: List of recent messages (in-memory cache)
        private static readonly ConcurrentDictionary<string, List<ChatMessageDto>> lobbyChatHistory =
            new ConcurrentDictionary<string, List<ChatMessageDto>>(StringComparer.OrdinalIgnoreCase);
        private const int MAX_HISTORY_PER_LOBBY = 50; // Limit memory usage

        // Session-specific variables
        private string currentUsername = null;
        private string currentLobbyId = null;
        private IChatCallback currentUserCallback = null;

        public ChatManagerService()
        {
            // Subscribe to channel events for cleanup on disconnect/fault
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
                return Task.CompletedTask; // Or throw exception
            }

            currentUserCallback = OperationContext.Current.GetCallbackChannel<IChatCallback>();
            if (currentUserCallback == null)
            {
                Console.WriteLine($"[Chat JOIN FAILED] Could not get callback channel for {username}.");
                return Task.CompletedTask; // Or throw exception
            }

            currentUsername = username;
            currentLobbyId = lobbyId;

            // Ensure lobby entry exists
            var usersInLobby = lobbyChatUsers.GetOrAdd(lobbyId, new ConcurrentDictionary<string, IChatCallback>(StringComparer.OrdinalIgnoreCase));

            // Add or update the user's callback for this lobby
            usersInLobby.AddOrUpdate(username, currentUserCallback, (key, existingVal) => currentUserCallback);

            Console.WriteLine($"[Chat JOIN] User '{username}' joined chat for lobby '{lobbyId}'.");

            // Optionally send recent history to the joining user
            if (lobbyChatHistory.TryGetValue(lobbyId, out var history))
            {
                foreach (var msg in history)
                {
                    try { currentUserCallback.receiveLobbyMessage(msg); } catch { /* Ignore issues sending history */ }
                }
            }

            // Optional: Notify others in the lobby that user joined
            // broadcastMessage(lobbyId, new ChatMessageDto { ... system message ... });

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
                    // Optional: Notify others
                    // broadcastMessage(lobbyId, new ChatMessageDto { ... system message ... });

                    // Clean up lobby entry if empty
                    if (usersInLobby.IsEmpty)
                    {
                        lobbyChatUsers.TryRemove(lobbyId, out _);
                        lobbyChatHistory.TryRemove(lobbyId, out _); // Clear history too
                        Console.WriteLine($"[Chat CLEANUP] Lobby '{lobbyId}' chat resources released.");
                    }
                }
            }

            // Clear session variables
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
                return Task.CompletedTask; // Ignore invalid messages
            }

            // Basic sanitization/validation (more robust needed for production)
            if (messageContent.Length > 200) // Limit message length
            {
                messageContent = messageContent.Substring(0, 200) + "...";
            }
            // TODO Add bad word filtering here if needed (Alcance y reglas de juego.pdf)

            var messageDto = new ChatMessageDto
            {
                senderUsername = senderUsername,
                content = messageContent, // Use sanitized content
                timestamp = DateTime.UtcNow
                // lobbyId = lobbyId // Optional
            };

            // Add to history cache
            var history = lobbyChatHistory.GetOrAdd(lobbyId, new List<ChatMessageDto>());
            lock (history) // Lock history list for modification
            {
                history.Add(messageDto);
                if (history.Count > MAX_HISTORY_PER_LOBBY)
                {
                    history.RemoveAt(0); // Keep history size limited
                }
            }

            // Broadcast to users in the lobby
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
                            // Attempt to remove stale callback
                            usersInLobby.TryRemove(recipientUsername, out _);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  -> EXCEPTION sending to {recipientUsername}: {ex.Message}. Removing.");
                        // Attempt to remove faulty callback
                        usersInLobby.TryRemove(recipientUsername, out _);
                    }
                }

                // Cleanup lobby if empty after removals
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

        // --- Channel Cleanup ---
        private void Channel_FaultedOrClosed(object sender, EventArgs e)
        {
            Console.WriteLine($"[Chat Channel Event] Faulted or Closed detected for User: '{currentUsername}', Lobby: '{currentLobbyId}'");
            // Perform cleanup using the session's username and lobby ID
            if (!string.IsNullOrEmpty(currentUsername) && !string.IsNullOrEmpty(currentLobbyId))
            {
                // Use Task.Run to avoid blocking the WCF thread
                Task.Run(() => leaveLobbyChat(currentUsername, currentLobbyId));
            }
            CleanupCallbackEvents(sender as ICommunicationObject);
        }

        private void CleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= Channel_FaultedOrClosed;
                commObject.Closed -= Channel_FaultedOrClosed;
            }
        }
    }
}