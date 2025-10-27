using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Chat;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class ChatManagerService : IChatManager
    {
        private readonly ChatLogic chatLogic;
        private string currentUsername = null;
        private string currentLobbyId = null;
        private IChatCallback currentUserCallback = null;
        private bool isDisconnected = false;

        public ChatManagerService()
        {
            this.chatLogic = new ChatLogic();

            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed += channel_FaultedOrClosed;
            }
        }

        public Task joinLobbyChat(string username, string lobbyId)
        {
            if (isDisconnected)
            {
                return Task.CompletedTask;
            }

            if (!registerSessionDetails(username, lobbyId))
            {
                return Task.CompletedTask;
            }

            try
            {
                chatLogic.joinLobbyChat(currentUsername, currentLobbyId, currentUserCallback);
            }
            catch (Exception ex)
            {
                Task.Run(() => handleDisconnect());
            }

            return Task.CompletedTask;
        }

        public Task leaveLobbyChat(string username, string lobbyId)
        {
            if (string.IsNullOrEmpty(currentUsername) ||
                !currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase) ||
                !currentLobbyId.Equals(lobbyId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            if (isDisconnected)
            {
                return Task.CompletedTask;
            }

            try
            {
                chatLogic.leaveLobbyChat(username, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatService LEAVE EXCEPTION] User: {username}, Lobby: {lobbyId}. Error: {ex.ToString()}");
            }

            return Task.CompletedTask;
        }

        public Task sendLobbyMessage(string senderUsername, string lobbyId, string messageContent)
        {
            if (isDisconnected || currentUserCallback == null ||
                string.IsNullOrEmpty(currentUsername) ||
                !currentUsername.Equals(senderUsername, StringComparison.OrdinalIgnoreCase) ||
                !currentLobbyId.Equals(lobbyId, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[ChatService SEND] Denied due to invalid state or mismatch. Request: Sender={senderUsername}, Lobby={lobbyId}. Session: User={currentUsername}, Lobby={currentLobbyId}, Disconnected={isDisconnected}, CallbackNull={currentUserCallback == null}.");
                return Task.CompletedTask;
            }

            try
            {
                chatLogic.processAndBroadcastMessage(senderUsername, lobbyId, messageContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatService SEND EXCEPTION] Sender: {senderUsername}, Lobby: {lobbyId}. Error: {ex.ToString()}");
            }

            return Task.CompletedTask;
        }

        
        private bool registerSessionDetails(string username, string lobbyId)
        {
            if (currentUserCallback != null &&
                currentUsername == username &&
                currentLobbyId == lobbyId)
            {
                return true;
            }

            if (currentUserCallback == null || (currentUserCallback as ICommunicationObject)?.State != CommunicationState.Opened)
            {
                if (OperationContext.Current == null)
                {
                    Console.WriteLine($"[ChatService REGISTER FAILED] OperationContext is null for User: {username}, Lobby: {lobbyId}.");
                    return false;
                }
                try
                {
                    currentUserCallback = OperationContext.Current.GetCallbackChannel<IChatCallback>();
                    if (currentUserCallback == null)
                    {
                        Console.WriteLine($"[ChatService REGISTER FAILED] GetCallbackChannel returned null for User: {username}, Lobby: {lobbyId}.");
                        return false;
                    }
                    Console.WriteLine($"[ChatService REGISTER] Callback channel obtained for User: {username}, Lobby: {lobbyId}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ChatService REGISTER FAILED] Exception getting callback channel for User: {username}, Lobby: {lobbyId}. Error: {ex.Message}");
                    currentUserCallback = null; // Ensure it's null on failure
                    return false;
                }
            }

            currentUsername = username;
            currentLobbyId = lobbyId;
            Console.WriteLine($"[ChatService REGISTER] Session details registered: User={currentUsername}, Lobby={currentLobbyId}.");
            return true;
        }

        private void channel_FaultedOrClosed(object sender, EventArgs e)
        {
            Task.Run(() => handleDisconnect());
        }

        private void handleDisconnect()
        {
            if (isDisconnected)
            {
                return;
            }

            isDisconnected = true;

            string userToDisconnect = currentUsername;
            string lobbyToDisconnect = currentLobbyId;
            cleanupCallbackEvents(OperationContext.Current?.Channel);
            cleanupCallbackEvents(currentUserCallback as ICommunicationObject);

            if (!string.IsNullOrWhiteSpace(userToDisconnect) && !string.IsNullOrWhiteSpace(lobbyToDisconnect))
            {
                try
                {
                    chatLogic.leaveLobbyChat(userToDisconnect, lobbyToDisconnect);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ChatService DISCONNECT EXCEPTION] Error during ChatLogic.leave for {userToDisconnect}: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("[ChatService DISCONNECT] No user/lobby associated with this session, skipping ChatLogic.leave call.");
            }

            currentUsername = null;
            currentLobbyId = null;
            currentUserCallback = null;
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= channel_FaultedOrClosed;
                commObject.Closed -= channel_FaultedOrClosed;
            }
        }
    }
}