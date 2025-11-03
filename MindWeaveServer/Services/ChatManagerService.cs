using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.ServiceModel;
using System.Threading.Tasks;
using NLog;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class ChatManagerService : IChatManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ChatLogic chatLogic;
        private string currentUsername;
        private string currentLobbyId;
        private IChatCallback currentUserCallback;
        private bool isDisconnected;

        public ChatManagerService()
        {
            this.chatLogic = new ChatLogic();

            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed += channel_FaultedOrClosed;
                logger.Debug("Attached Faulted/Closed event handlers to the current WCF channel.");
            }
            else
            {
                logger.Warn("Could not attach channel event handlers - OperationContext or Channel is null.");
            }
            logger.Info("ChatManagerService instance created (PerSession).");
        }

        public void joinLobbyChat(string username, string lobbyId)
        {
            logger.Info("joinLobbyChat attempt by user: {Username} for lobby: {LobbyId}", username ?? "NULL", lobbyId ?? "NULL");
            if (isDisconnected)
            {
                logger.Warn("joinLobbyChat ignored: Session is marked as disconnected. User: {Username}", username ?? "NULL");
                return;
            }

            if (!registerSessionDetails(username, lobbyId))
            {
                logger.Warn("joinLobbyChat failed: Session could not be registered. User: {Username}", username ?? "NULL");
                return;
            }

            try
            {
                chatLogic.joinLobbyChat(currentUsername, currentLobbyId, currentUserCallback);
                logger.Info("User {Username} successfully joined chat for lobby {LobbyId}", currentUsername, currentLobbyId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception in joinLobbyChat for {Username}, lobby {LobbyId}. Triggering disconnect.", currentUsername ?? "NULL", currentLobbyId ?? "NULL");
                Task.Run(() => handleDisconnect());
            }
        }

        public void leaveLobbyChat(string username, string lobbyId)
        {
            logger.Info("leaveLobbyChat attempt by user: {Username} from lobby: {LobbyId}", username ?? "NULL", lobbyId ?? "NULL");
            if (string.IsNullOrEmpty(currentUsername) ||
                !currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase) ||
                !currentLobbyId.Equals(lobbyId, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("leaveLobbyChat ignored: Session validation failed or mismatch. Request: User={Username}, Lobby={LobbyId}. Session: User={CurrentUsername}, Lobby={CurrentLobbyId}", username, lobbyId, currentUsername, currentLobbyId);
                return;
            }

            if (isDisconnected)
            {
                logger.Warn("leaveLobbyChat ignored: Session is marked as disconnected. User: {Username}", username ?? "NULL");
                return;
            }

            try
            {
                chatLogic.leaveLobbyChat(username, lobbyId);
                logger.Info("User {Username} successfully left chat for lobby {LobbyId}", username, lobbyId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[ChatService LEAVE EXCEPTION] User: {Username}, Lobby: {LobbyId}.", username, lobbyId);
            }
        }

        public void sendLobbyMessage(string senderUsername, string lobbyId, string messageContent)
        {
            logger.Info("sendLobbyMessage attempt by user: {Username} in lobby: {LobbyId}", senderUsername ?? "NULL", lobbyId ?? "NULL");

            if (isDisconnected || currentUserCallback == null ||
                string.IsNullOrEmpty(currentUsername) ||
                !currentUsername.Equals(senderUsername, StringComparison.OrdinalIgnoreCase) ||
                !currentLobbyId.Equals(lobbyId, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("ChatService SEND Denied due to invalid state or mismatch. Request: Sender={SenderUsername}, Lobby={LobbyId}. Session: User={CurrentUsername}, Lobby={CurrentLobbyId}, Disconnected={IsDisconnected}, CallbackNull={CallbackNull}.",
                    senderUsername, lobbyId, currentUsername, currentLobbyId, isDisconnected, currentUserCallback == null);
                return;
            }

            try
            {
                chatLogic.processAndBroadcastMessage(senderUsername, lobbyId, messageContent);
                logger.Debug("sendLobbyMessage processed for {Username} in lobby {LobbyId}", senderUsername, lobbyId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[ChatService SEND EXCEPTION] Sender: {SenderUsername}, Lobby: {LobbyId}.", senderUsername, lobbyId);
            }
        }


        private bool registerSessionDetails(string username, string lobbyId)
        {
            if (currentUserCallback != null &&
                currentUsername == username &&
                currentLobbyId == lobbyId)
            {
                logger.Debug("Session details already registered for {Username}, lobby {LobbyId}", username, lobbyId);
                return true;
            }

            if (currentUserCallback == null || (currentUserCallback as ICommunicationObject)?.State != CommunicationState.Opened)
            {
                if (OperationContext.Current == null)
                {
                    logger.Error("[ChatService REGISTER FAILED] OperationContext is null for User: {Username}, Lobby: {LobbyId}.", username, lobbyId);
                    return false;
                }
                try
                {
                    currentUserCallback = OperationContext.Current.GetCallbackChannel<IChatCallback>();
                    if (currentUserCallback == null)
                    {
                        logger.Error("[ChatService REGISTER FAILED] GetCallbackChannel returned null for User: {Username}, Lobby: {LobbyId}.", username, lobbyId);
                        return false;
                    }
                    logger.Debug("[ChatService REGISTER] Callback channel obtained for User: {Username}, Lobby: {LobbyId}.", username, lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "[ChatService REGISTER FAILED] Exception getting callback channel for User: {Username}, Lobby: {LobbyId}.", username, lobbyId);
                    currentUserCallback = null;
                    return false;
                }
            }

            currentUsername = username;
            currentLobbyId = lobbyId;
            logger.Info("[ChatService REGISTER] Session details registered: User={CurrentUsername}, Lobby={CurrentLobbyId}.", currentUsername, currentLobbyId);
            return true;
        }

        private void channel_FaultedOrClosed(object sender, EventArgs e)
        {
            logger.Warn("WCF channel Faulted or Closed for user: {Username}, lobby {LobbyId}. Initiating disconnect.", currentUsername, currentLobbyId);
            Task.Run(() => handleDisconnect());
        }

        private void handleDisconnect()
        {
            if (isDisconnected)
            {
                logger.Debug("handleDisconnect ignored: Session already disconnected.");
                return;
            }

            isDisconnected = true;

            string userToDisconnect = currentUsername;
            string lobbyToDisconnect = currentLobbyId;

            logger.Info("Disconnect triggered for session. User: {Username}, Lobby: {LobbyId}", userToDisconnect ?? "UNKNOWN", lobbyToDisconnect ?? "UNKNOWN");

            cleanupCallbackEvents(OperationContext.Current?.Channel);
            cleanupCallbackEvents(currentUserCallback as ICommunicationObject);

            if (!string.IsNullOrWhiteSpace(userToDisconnect) && !string.IsNullOrWhiteSpace(lobbyToDisconnect))
            {
                try
                {
                    chatLogic.leaveLobbyChat(userToDisconnect, lobbyToDisconnect);
                    logger.Info("ChatLogic.leaveLobbyChat called successfully for {Username}, lobby {LobbyId}", userToDisconnect, lobbyToDisconnect);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "[ChatService DISCONNECT EXCEPTION] Error during ChatLogic.leave for {Username}, lobby {LobbyId}", userToDisconnect, lobbyToDisconnect);
                }
            }
            else
            {
                logger.Info("[ChatService DISCONNECT] No user/lobby associated with this session, skipping ChatLogic.leave call.");
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
                logger.Debug("Removed Faulted/Closed event handlers from a callback channel.");
            }
        }
    }
}