using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.ServiceContracts;
using NLog;
using System;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(
        InstanceContextMode = InstanceContextMode.PerSession,
        ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class ChatManagerService : IChatManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ChatLogic chatLogic;

        private IChatCallback currentUserCallback;
        private string currentUsername;
        private string currentLobbyId;

        private volatile bool isDisconnecting;
        private readonly object disconnectLock = new object();

        public ChatManagerService()
        {
            Bootstrapper.init();
            this.chatLogic = Bootstrapper.Container.Resolve<ChatLogic>();
            Bootstrapper.Container.Resolve<IDisconnectionHandler>();

            subscribeToChannelEvents();
        }

        public ChatManagerService(ChatLogic chatLogic, IDisconnectionHandler disconnectionHandler)
        {
            this.chatLogic = chatLogic;

            subscribeToChannelEvents();
        }

        #region Channel Event Handlers (Reliable Session Detection)

        private void subscribeToChannelEvents()
        {
            if (OperationContext.Current?.Channel == null)
            {
                logger.Warn("ChatManagerService: Cannot subscribe to channel events - OperationContext or Channel is null.");
                return;
            }

            OperationContext.Current.Channel.Faulted += onChannelFaulted;
            OperationContext.Current.Channel.Closed += onChannelClosed;

            logger.Debug("ChatManagerService: Subscribed to channel Faulted/Closed events.");
        }

        private void onChannelFaulted(object sender, EventArgs e)
        {
            logger.Warn("ChatManagerService: Channel FAULTED for user {0}. Initiating cleanup.",
                currentUsername ?? "Unknown");

            initiateCleanupAsync();
        }

        private void onChannelClosed(object sender, EventArgs e)
        {
            logger.Info("ChatManagerService: Channel CLOSED for user {0}. Initiating cleanup.",
                currentUsername ?? "Unknown");

            initiateCleanupAsync();
        }

        private void initiateCleanupAsync()
        {
            string usernameToCleanup;
            string lobbyToLeave;

            lock (disconnectLock)
            {
                if (isDisconnecting)
                {
                    logger.Debug("ChatManagerService: Cleanup already in progress for {0}.", currentUsername);
                    return;
                }

                isDisconnecting = true;
                usernameToCleanup = currentUsername;
                lobbyToLeave = currentLobbyId;
            }

            Task.Run(() =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(usernameToCleanup) && !string.IsNullOrWhiteSpace(lobbyToLeave))
                    {
                        logger.Info("ChatManagerService: Cleaning up chat for {0} from lobby {1}.",
                            usernameToCleanup, lobbyToLeave);

                        chatLogic.leaveLobbyChat(usernameToCleanup, lobbyToLeave);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "ChatManagerService: Error during chat cleanup for {0}.", usernameToCleanup);
                }
                finally
                {
                    cleanupLocalState();
                }
            });
        }

        private void cleanupLocalState()
        {
            cleanupCallbackEvents(OperationContext.Current?.Channel);
            cleanupCallbackEvents(currentUserCallback as ICommunicationObject);

            currentUsername = null;
            currentLobbyId = null;
            currentUserCallback = null;
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= onChannelFaulted;
                commObject.Closed -= onChannelClosed;
            }
        }

        #endregion

        #region Service Operations

        public void joinLobbyChat(string username, string lobbyId)
        {
            logger.Info("joinLobbyChat request for lobby {LobbyId}", lobbyId ?? "NULL");

            if (isDisconnecting)
            {
                logger.Warn("joinLobbyChat ignored: Session is marked as disconnecting.");
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

        #endregion

        #region Private Helper Methods

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
                    setupCallbackEvents(currentUserCallback as ICommunicationObject);
                }
                catch (InvalidCastException ex)
                {
                    logger.Error(ex, "Callback channel casting failed.");
                    currentUserCallback = null;
                    return false;
                }
                catch (InvalidOperationException ex)
                {
                    logger.Error(ex, "Callback channel retrieval invalid op (Context closed?).");
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

            if (isDisconnecting)
            {
                logger.Warn("leaveLobbyChat ignored: Session is marked as disconnecting.");
                return false;
            }

            return true;
        }

        private bool validateSessionForMessage(string senderUsername, string lobbyId)
        {
            if (isDisconnecting || currentUserCallback == null || string.IsNullOrEmpty(currentUsername))
            {
                logger.Warn("sendLobbyMessage denied: Invalid session state. Disconnecting={IsDisconnecting}, CallbackNull={CallbackNull}",
                    isDisconnecting, currentUserCallback == null);
                return false;
            }

            bool usernameMatches = currentUsername.Equals(senderUsername, StringComparison.OrdinalIgnoreCase);
            bool lobbyMatches = currentLobbyId != null && currentLobbyId.Equals(lobbyId, StringComparison.OrdinalIgnoreCase);

            return usernameMatches && lobbyMatches;
        }

        private void setupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= onChannelFaulted;
                commObject.Closed -= onChannelClosed;
                commObject.Faulted += onChannelFaulted;
                commObject.Closed += onChannelClosed;
            }
        }

        #endregion
    }
}