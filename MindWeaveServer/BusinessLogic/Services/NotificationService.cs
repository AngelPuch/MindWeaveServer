using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;

namespace MindWeaveServer.BusinessLogic.Services
{
    public class NotificationService : INotificationService
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly IGameStateManager gameStateManager;

        public NotificationService(IGameStateManager gameStateManager)
        {
            this.gameStateManager = gameStateManager;
        }

        public void notifyLobbyState(string username, LobbyStateDto lobbyState)
        {
            sendToUser(username, cb => cb.updateLobbyState(lobbyState));
        }

        public void broadcastLobbyState(LobbyStateDto lobbyState)
        {
            if (lobbyState == null)
            {
                return;
            }

            List<string> playersSnapshot;
            lock (lobbyState)
            {
                playersSnapshot = lobbyState.Players.ToList();
            }

            foreach (var player in playersSnapshot)
            {
                notifyLobbyState(player, lobbyState);
            }
        }

        public void notifyActionFailed(string username, string message)
        {
            sendToUser(username, cb => cb.notifyLobbyActionFailed(message));
        }

        public void notifyKicked(string username, string reason)
        {
            sendToUser(username, cb => cb.kickedFromLobby(reason));
        }

        public void notifyLobbyCreationFailed(string username, string reason)
        {
            sendToUser(username, cb => cb.lobbyCreationFailed(reason));
        }

        public void sendToUser(string username, Action<IMatchmakingCallback> action)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            if (gameStateManager.MatchmakingCallbacks.TryGetValue(username, out IMatchmakingCallback callback))
            {
                executeCallbackSafe(callback, username, action, isMatchmaking: true);
            }
            else
            {
                logger.Warn("Could not send matchmaking notification: No callback found for target user");
            }
        }

        public void sendSocialNotification(string username, Action<ISocialCallback> action)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            var callback = gameStateManager.getUserCallback(username);

            if (callback != null)
            {
                executeCallbackSafe(callback, username, action, isMatchmaking: false);
            }
            else
            {
                logger.Warn("Could not send social notification: No callback found for target user");
            }
        }

        public void broadcastLobbyDestroyed(LobbyStateDto lobbyState, string reason)
        {
            if (lobbyState == null)
            {
                return;
            }

            List<string> playersSnapshot;
            lock (lobbyState)
            {
                playersSnapshot = lobbyState.Players.ToList();
            }

            foreach (var player in playersSnapshot)
            {
                sendToUser(player, cb => cb.lobbyDestroyed(reason));
            }
        }

        private void executeCallbackSafe<T>(T callback, string username, Action<T> action, bool isMatchmaking) where T : class
        {
            var commObject = callback as ICommunicationObject;

            if (commObject == null)
            {
                return;
            }

            if (commObject.State != CommunicationState.Opened)
            {
                logger.Warn("Callback channel is closed/faulted (State: {ChannelState}). Removing reference.", commObject.State);
                cleanupUserReference(username, isMatchmaking);
                return;
            }

            executeCallbackWithExceptionHandling(callback, username, action, isMatchmaking);
        }

        private void executeCallbackWithExceptionHandling<T>(T callback, string username, Action<T> action, bool isMatchmaking) where T : class
        {
            try
            {
                action(callback);
            }
            catch (CommunicationException ex)
            {
                logger.Warn(ex, "CommunicationException sending callback to user");
                cleanupUserReference(username, isMatchmaking);
            }
            catch (TimeoutException ex)
            {
                logger.Warn(ex, "TimeoutException sending callback to user");
                cleanupUserReference(username, isMatchmaking);
            }
            catch (ObjectDisposedException ex)
            {
                logger.Warn(ex, "ObjectDisposedException sending callback to user");
                cleanupUserReference(username, isMatchmaking);
            }
        }

        private void cleanupUserReference(string username, bool isMatchmaking)
        {
            if (isMatchmaking)
            {
                gameStateManager.MatchmakingCallbacks.TryRemove(username, out _);
            }
            else
            {
                gameStateManager.removeConnectedUser(username);
            }
        }
    }
}