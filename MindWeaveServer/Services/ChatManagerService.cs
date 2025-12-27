using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.ServiceContracts;
using NLog;
using System;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class ChatManagerService : IChatManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ChatLogic chatLogic;
        private IChatCallback currentUserCallback;

        private string currentUsername;
        private string currentLobbyId;
        private volatile bool isDisconnected;
        private readonly object disconnectLock = new object();

        public ChatManagerService()
        {
            Bootstrapper.init();
            this.chatLogic = Bootstrapper.Container.Resolve<ChatLogic>();
        }

        public ChatManagerService(ChatLogic chatLogic)
        {
            this.chatLogic = chatLogic;
            attachChannelEventHandlers();
        }


        public void joinLobbyChat(string username, string lobbyId)
        {
            logger.Info("joinLobbyChat request for lobby {LobbyId}", lobbyId ?? "NULL");

            if (isDisconnected)
            {
                logger.Warn("joinLobbyChat ignored: Session is marked as disconnected.");
                return;
            }

            if (!tryRegisterSession(username, lobbyId))
            {
                logger.Warn("joinLobbyChat failed: Session could not be registered for lobby {LobbyId}", lobbyId);
                return;
            }

            try
            {
                chatLogic.joinLobbyChat(currentUsername, currentLobbyId, currentUserCallback);
            }
            catch (ArgumentNullException ex)
            {
                logger.Warn(ex, "Chat Service Validation Error for lobby {LobbyId}", lobbyId);
            }
            
        }

        public void leaveLobbyChat(string username, string lobbyId)
        {
            logger.Info("leaveLobbyChat request for lobby {LobbyId}", lobbyId ?? "NULL");

            if (!validateSessionForLeave(username, lobbyId))
            {
                return;
            }

            chatLogic.leaveLobbyChat(username, lobbyId);
        }

        public void sendLobbyMessage(string senderUsername, string lobbyId, string messageContent)
        {
            logger.Debug("sendLobbyMessage request for lobby {LobbyId}", lobbyId ?? "NULL");

            if (!validateSessionForMessage(senderUsername, lobbyId))
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await chatLogic.processAndBroadcastMessageAsync(senderUsername, lobbyId, messageContent);
                }
                catch (ArgumentException ex)
                {
                    logger.Warn(ex, "Chat Message Validation Failed for lobby {LobbyId}", lobbyId);
                }
            });
            }

        private bool tryRegisterSession(string username, string lobbyId)
        {
            if (currentUserCallback != null && currentUsername == username && currentLobbyId == lobbyId)
            {
                return true;
            }

            if (currentUserCallback == null || (currentUserCallback as ICommunicationObject)?.State != CommunicationState.Opened)
            {
                if (OperationContext.Current == null)
                {
                    logger.Error("OperationContext is null for lobby {LobbyId}.", lobbyId);
                    return false;
                }

                try
                {
                    currentUserCallback = OperationContext.Current.GetCallbackChannel<IChatCallback>();
                    if (currentUserCallback == null)
                    {
                        logger.Error("GetCallbackChannel returned null for lobby {LobbyId}.", lobbyId);
                        return false;
                    }
                }
                catch (InvalidCastException)
                {
                    logger.Error("Callback channel casting failed.");
                    currentUserCallback = null;
                    return false;
                }
                catch (InvalidOperationException)
                {
                    logger.Error("Callback channel retrieval invalid op (Context closed?).");
                    currentUserCallback = null;
                    return false;
                }
            }

            currentUsername = username;
            currentLobbyId = lobbyId;
            return true;
        }

        private bool validateSessionForLeave(string username, string lobbyId)
        {
            if (string.IsNullOrEmpty(currentUsername))
            {
                logger.Warn("leaveLobbyChat ignored: No session registered.");
                return false;
            }

            bool usernameMatches = currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase);
            bool lobbyMatches = currentLobbyId != null && currentLobbyId.Equals(lobbyId, StringComparison.OrdinalIgnoreCase);

            if (!usernameMatches || !lobbyMatches)
            {
                logger.Warn("leaveLobbyChat ignored: Session validation failed for lobby {LobbyId}.", lobbyId);
                return false;
            }

            if (isDisconnected)
            {
                logger.Warn("leaveLobbyChat ignored: Session is marked as disconnected.");
                return false;
            }

            return true;
        }

        private bool validateSessionForMessage(string senderUsername, string lobbyId)
        {
            if (isDisconnected || currentUserCallback == null || string.IsNullOrEmpty(currentUsername))
            {
                logger.Warn("sendLobbyMessage denied: Invalid session state. Disconnected={IsDisconnected}, CallbackNull={CallbackNull}",
                    isDisconnected, currentUserCallback == null);
                return false;
            }

            bool usernameMatches = currentUsername.Equals(senderUsername, StringComparison.OrdinalIgnoreCase);
            bool lobbyMatches = currentLobbyId != null && currentLobbyId.Equals(lobbyId, StringComparison.OrdinalIgnoreCase);

            return usernameMatches && lobbyMatches;
        }

        private void attachChannelEventHandlers()
        {
            if (OperationContext.Current?.Channel == null)
            {
                logger.Warn("Could not attach channel event handlers - OperationContext or Channel is null.");
                return;
            }

            OperationContext.Current.Channel.Faulted += onChannelFaultedOrClosed;
            OperationContext.Current.Channel.Closed += onChannelFaultedOrClosed;
        }

        private void onChannelFaultedOrClosed(object sender, EventArgs e)
        {
            initiateDisconnectAsync();
        }

        private void initiateDisconnectAsync()
        {
            Task.Run(handleDisconnect);
        }

        private void handleDisconnect()
        {
            lock (disconnectLock)
            {
                if (isDisconnected)
                {
                    return;
                }
                isDisconnected = true;
            }

            string userToDisconnect = currentUsername;
            string lobbyToDisconnect = currentLobbyId;


            cleanupCallbackEvents(OperationContext.Current?.Channel);
            cleanupCallbackEvents(currentUserCallback as ICommunicationObject);

            if (!string.IsNullOrWhiteSpace(userToDisconnect) && !string.IsNullOrWhiteSpace(lobbyToDisconnect))
            {
                chatLogic.leaveLobbyChat(userToDisconnect, lobbyToDisconnect);
            }

            currentUsername = null;
            currentLobbyId = null;
            currentUserCallback = null;
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= onChannelFaultedOrClosed;
                commObject.Closed -= onChannelFaultedOrClosed;
            }
        }
    }
}