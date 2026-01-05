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
            if (lobbyState == null) return;

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
            if (string.IsNullOrWhiteSpace(username)) return;

            if (gameStateManager.MatchmakingCallbacks.TryGetValue(username, out IMatchmakingCallback callback))
            {
                executeCallbackSafe(callback, username, action, isMatchmaking: true);
            }
            else
            {
                logger.Warn("Could not send matchmaking notification to {0}: No callback found.", username);
            }
        }

        public void sendSocialNotification(string username, Action<ISocialCallback> action)
        {
            if (string.IsNullOrWhiteSpace(username)) return;

            var callback = gameStateManager.getUserCallback(username);

            if (callback != null)
            {
                executeCallbackSafe(callback, username, action, isMatchmaking: false);
            }
            else
            {
                logger.Warn("Could not send social notification to {0}: No callback found.", username);
            }
        }

        private void executeCallbackSafe<T>(T callback, string username, Action<T> action, bool isMatchmaking) where T : class
        {
            var commObject = callback as ICommunicationObject;

            if (commObject == null) return;

            try
            {
                if (commObject.State == CommunicationState.Opened)
                {
                    action(callback);
                }
                else
                {
                    logger.Warn("Callback channel for {0} is closed/faulted (State: {1}). Removing reference.", username, commObject.State);
                    cleanupUserReference(username, isMatchmaking);
                }
            }
            catch (CommunicationException ex)
            {
                logger.Warn(ex, "CommunicationException sending callback to {0}", username);
                cleanupUserReference(username, isMatchmaking);
            }
            catch (TimeoutException ex)
            {
                logger.Warn(ex, "TimeoutException sending callback to {0}", username);
                cleanupUserReference(username, isMatchmaking);
            }
            catch (ObjectDisposedException ex)
            {
                logger.Warn(ex, "ObjectDisposedException sending callback to {0}", username);
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