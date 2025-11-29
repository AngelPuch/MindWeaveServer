using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.ServiceContracts;
using NLog;
using System;
using System.Collections.Generic;
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

        public ChatManagerService() : this(resolveLogic())
        {
        }

        public ChatManagerService(ChatLogic chatLogic)
        {
            this.chatLogic = chatLogic ?? throw new ArgumentNullException(nameof(chatLogic));
            attachChannelEventHandlers();
        }

        private static ChatLogic resolveLogic()
        {
            Bootstrapper.init();
            return Bootstrapper.Container.Resolve<ChatLogic>();
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
                logger.Info("User successfully joined chat for lobby {LobbyId}", currentLobbyId);
            }
            catch (TimeoutException ex)
            {
                logger.Error(ex, "Chat Service Timeout for lobby {LobbyId}", lobbyId);
                initiateDisconnectAsync();
            }
            catch (ArgumentNullException ex)
            {
                logger.Warn(ex, "Chat Service Validation Error for lobby {LobbyId}", lobbyId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Chat Service Unexpected Error inside joinLobbyChat for lobby {LobbyId}", lobbyId);
                initiateDisconnectAsync();
            }
        }

        public void leaveLobbyChat(string username, string lobbyId)
        {
            logger.Info("leaveLobbyChat request for lobby {LobbyId}", lobbyId ?? "NULL");

            if (!validateSessionForLeave(username, lobbyId))
            {
                return;
            }

            try
            {
                chatLogic.leaveLobbyChat(username, lobbyId);
                logger.Info("User successfully left chat for lobby {LobbyId}", lobbyId);
            }
            catch (KeyNotFoundException ex)
            {
                logger.Warn(ex, "Chat Service Warning: User tried to leave non-existent lobby {LobbyId}", lobbyId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Chat Service Error inside leaveLobbyChat for lobby {LobbyId}", lobbyId);
            }
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
                catch (KeyNotFoundException ex)
                {
                    logger.Warn(ex, "Chat Message Error: Lobby {LobbyId} not found", lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Chat Service Critical Error processing message for lobby {LobbyId}", lobbyId);
                }
            });

            logger.Debug("sendLobbyMessage dispatched for lobby {LobbyId}", lobbyId);
        }

        private bool tryRegisterSession(string username, string lobbyId)
        {
            if (currentUserCallback != null && currentUsername == username && currentLobbyId == lobbyId)
            {
                logger.Debug("Session details already registered for lobby {LobbyId}", lobbyId);
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
                    logger.Debug("Callback channel obtained for lobby {LobbyId}.", lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Exception getting callback channel for lobby {LobbyId}.", lobbyId);
                    currentUserCallback = null;
                    return false;
                }
            }

            currentUsername = username;
            currentLobbyId = lobbyId;
            logger.Info("Session details registered for lobby {LobbyId}.", currentLobbyId);
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

            if (!usernameMatches || !lobbyMatches)
            {
                logger.Warn("sendLobbyMessage denied: Session mismatch for lobby {LobbyId}.", lobbyId);
                return false;
            }

            return true;
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
            logger.Debug("Attached Faulted/Closed event handlers to the current WCF channel.");
        }

        private void onChannelFaultedOrClosed(object sender, EventArgs e)
        {
            logger.Warn("WCF channel Faulted or Closed for lobby {LobbyId}. Initiating disconnect.", currentLobbyId);
            initiateDisconnectAsync();
        }

        private void initiateDisconnectAsync()
        {
            Task.Run(() => handleDisconnect());
        }

        private void handleDisconnect()
        {
            lock (disconnectLock)
            {
                if (isDisconnected)
                {
                    logger.Debug("handleDisconnect ignored: Session already disconnected.");
                    return;
                }
                isDisconnected = true;
            }

            string userToDisconnect = currentUsername;
            string lobbyToDisconnect = currentLobbyId;

            logger.Info("Disconnect triggered for session in lobby {LobbyId}", lobbyToDisconnect ?? "UNKNOWN");

            cleanupCallbackEvents(OperationContext.Current?.Channel);
            cleanupCallbackEvents(currentUserCallback as ICommunicationObject);

            if (!string.IsNullOrWhiteSpace(userToDisconnect) && !string.IsNullOrWhiteSpace(lobbyToDisconnect))
            {
                try
                {
                    chatLogic.leaveLobbyChat(userToDisconnect, lobbyToDisconnect);
                    logger.Info("ChatLogic.leaveLobbyChat called successfully for lobby {LobbyId}", lobbyToDisconnect);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error during ChatLogic.leave for lobby {LobbyId}", lobbyToDisconnect);
                }
            }
            else
            {
                logger.Info("No user/lobby associated with this session, skipping ChatLogic.leave call.");
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
                logger.Debug("Removed Faulted/Closed event handlers from a callback channel.");
            }
        }
    }
}