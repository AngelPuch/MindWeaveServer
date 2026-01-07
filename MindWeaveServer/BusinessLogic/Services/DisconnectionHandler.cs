using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using NLog;
using System;
using System.Collections.Generic;
using System.Data.Entity.Core;
using System.Data.SqlClient;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;

namespace MindWeaveServer.BusinessLogic.Services
{
    public class DisconnectionHandler : IDisconnectionHandler
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IUserSessionManager userSessionManager;
        private readonly IGameStateManager gameStateManager;
        private readonly GameSessionManager gameSessionManager;

        private readonly object disconnectionLock = new object();
        private readonly HashSet<string> usersBeingDisconnected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public DisconnectionHandler(
            IUserSessionManager userSessionManager,
            IGameStateManager gameStateManager,
            GameSessionManager gameSessionManager)
        {
            this.userSessionManager = userSessionManager ?? throw new ArgumentNullException(nameof(userSessionManager));
            this.gameStateManager = gameStateManager ?? throw new ArgumentNullException(nameof(gameStateManager));
            this.gameSessionManager = gameSessionManager ?? throw new ArgumentNullException(nameof(gameSessionManager));
        }

        public async Task handleFullDisconnectionAsync(string username, string reason)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("DisconnectionHandler: Attempted to disconnect null/empty username.");
                return;
            }

            // Verificar si ya estamos procesando este usuario
            lock (disconnectionLock)
            {
                if (usersBeingDisconnected.Contains(username))
                {
                    logger.Info("DisconnectionHandler: Disconnection already in progress for {0}. Skipping duplicate call.", username);
                    return;
                }

                usersBeingDisconnected.Add(username);
            }

            try
            {
                logger.Info("===== DISCONNECTION START: {0} (Reason: {1}) =====", username, reason);

                // 1. Limpiar de partidas activas
                await cleanupFromActiveGamesAsync(username);

                // 2. Limpiar de lobbies
                await cleanupFromLobbiesAsync(username);

                // 3. Limpiar de chats de lobby
                cleanupFromLobbyChats(username);

                // 4. Limpiar callbacks de matchmaking
                cleanupMatchmakingCallbacks(username);

                // 5. Limpiar del servicio social
                await cleanupFromSocialAsync(username);

                // 6. Limpiar sesión de autenticación
                cleanupAuthenticationSession(username);

                logger.Info("===== DISCONNECTION COMPLETE: {0} =====", username);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DisconnectionHandler: Critical error during disconnection of {0}.", username);
            }
            finally
            {
                lock (disconnectionLock)
                {
                    usersBeingDisconnected.Remove(username);
                }
            }
        }

        private async Task cleanupFromActiveGamesAsync(string username)
        {
            try
            {
                var session = gameSessionManager.findSessionByUsername(username);

                if (session == null)
                {
                    logger.Debug("DisconnectionHandler: No active game session found for {0}.", username);
                    return;
                }

                string lobbyCode = session.LobbyCode;

                logger.Info("DisconnectionHandler: Found active game session {0} for {1}. Processing leave.",
                    lobbyCode, username);

                await gameSessionManager.handlePlayerLeaveAsync(lobbyCode, username);

                logger.Info("DisconnectionHandler: Successfully processed game exit for {0} from session {1}.",
                    username, lobbyCode);
            }
            catch (EntityException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Database error cleaning up games for {0}.", username);
            }
            catch (SqlException ex)
            {
                logger.Error(ex, "DisconnectionHandler: SQL error cleaning up games for {0}.", username);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DisconnectionHandler: Unexpected error cleaning up games for {0}.", username);
            }
        }

        private async Task cleanupFromLobbiesAsync(string username)
        {
            try
            {
                var lobbiesToLeave = gameStateManager.ActiveLobbies
                    .Where(kvp =>
                    {
                        lock (kvp.Value)
                        {
                            return kvp.Value.Players.Contains(username, StringComparer.OrdinalIgnoreCase);
                        }
                    })
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var lobbyCode in lobbiesToLeave)
                {
                    try
                    {
                        await processLobbyLeaveAsync(lobbyCode, username);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "DisconnectionHandler: Error leaving lobby {0} for {1}.", lobbyCode, username);
                    }
                }

                // Limpiar de tracking de guests
                foreach (var kvp in gameStateManager.GuestUsernamesInLobby.ToArray())
                {
                    if (kvp.Value.Remove(username))
                    {
                        logger.Debug("DisconnectionHandler: Removed {0} from guest tracking in lobby {1}.", username, kvp.Key);

                        if (kvp.Value.Count == 0)
                        {
                            gameStateManager.GuestUsernamesInLobby.TryRemove(kvp.Key, out _);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DisconnectionHandler: Error cleaning up lobbies for {0}.", username);
            }
        }

        private async Task processLobbyLeaveAsync(string lobbyCode, string username)
        {
            if (!gameStateManager.ActiveLobbies.TryGetValue(lobbyCode, out var lobby))
            {
                return;
            }

            bool isHost;
            bool wasInLobby;
            List<string> remainingPlayers = null;

            lock (lobby)
            {
                wasInLobby = lobby.Players.RemoveAll(p => p.Equals(username, StringComparison.OrdinalIgnoreCase)) > 0;
                isHost = lobby.HostUsername.Equals(username, StringComparison.OrdinalIgnoreCase);

                if (isHost || lobby.Players.Count == 0)
                {
                    remainingPlayers = lobby.Players.ToList();
                }
            }

            if (!wasInLobby)
            {
                return;
            }

            logger.Info("DisconnectionHandler: Removed {0} from lobby {1}. IsHost: {2}", username, lobbyCode, isHost);

            if (isHost || (remainingPlayers != null && remainingPlayers.Count == 0))
            {
                closeLobbyAndNotify(lobbyCode, remainingPlayers);
            }
            else
            {
                broadcastLobbyState(lobby);
            }

            await Task.CompletedTask;
        }

        private void closeLobbyAndNotify(string lobbyCode, List<string> playersToKick)
        {
            gameStateManager.ActiveLobbies.TryRemove(lobbyCode, out _);
            gameStateManager.GuestUsernamesInLobby.TryRemove(lobbyCode, out _);

            logger.Info("DisconnectionHandler: Lobby {0} closed.", lobbyCode);

            if (playersToKick != null)
            {
                foreach (var player in playersToKick)
                {
                    notifyPlayerKicked(player, "HOST_DISCONNECTED");
                    cleanupMatchmakingCallbacks(player);
                }
            }
        }

        private void broadcastLobbyState(LobbyStateDto lobby)
        {
            if (lobby == null)
            {
                return;
            }

            List<string> playersSnapshot;
            lock (lobby)
            {
                playersSnapshot = lobby.Players.ToList();
            }

            foreach (var playerName in playersSnapshot)
            {
                tryNotifyPlayerOfLobbyUpdate(playerName, lobby);
            }
        }

        private void tryNotifyPlayerOfLobbyUpdate(string playerName, LobbyStateDto lobby)
        {
            if (!gameStateManager.MatchmakingCallbacks.TryGetValue(playerName, out var callback))
            {
                return;
            }

            try
            {
                var commObj = callback as ICommunicationObject;
                if (commObj?.State == CommunicationState.Opened)
                {
                    callback.updateLobbyState(lobby);
                }
            }
            catch (CommunicationException) { }
            catch (TimeoutException) { }
            catch (ObjectDisposedException) { }
        }

        private void notifyPlayerKicked(string username, string reason)
        {
            if (gameStateManager.MatchmakingCallbacks.TryGetValue(username, out var callback))
            {
                try
                {
                    var commObj = callback as ICommunicationObject;
                    if (commObj?.State == CommunicationState.Opened)
                    {
                        callback.kickedFromLobby(reason);
                    }
                }
                catch { }
            }
        }

        private void cleanupFromLobbyChats(string username)
        {
            try
            {
                foreach (var lobbyKvp in gameStateManager.LobbyChatUsers.ToArray())
                {
                    var lobbyId = lobbyKvp.Key;
                    var usersInLobby = lobbyKvp.Value;

                    if (usersInLobby.TryRemove(username, out _))
                    {
                        logger.Debug("DisconnectionHandler: Removed {0} from chat in lobby {1}.", username, lobbyId);

                        if (usersInLobby.IsEmpty)
                        {
                            gameStateManager.LobbyChatUsers.TryRemove(lobbyId, out _);
                            gameStateManager.LobbyChatHistory.TryRemove(lobbyId, out _);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DisconnectionHandler: Error cleaning up lobby chats for {0}.", username);
            }
        }

        private void cleanupMatchmakingCallbacks(string username)
        {
            if (gameStateManager.MatchmakingCallbacks.TryRemove(username, out _))
            {
                logger.Debug("DisconnectionHandler: Removed matchmaking callback for {0}.", username);
            }
        }

        private async Task cleanupFromSocialAsync(string username)
        {
            try
            {
                if (!gameStateManager.isUserConnected(username))
                {
                    return;
                }

                // Notificar a amigos que el usuario se desconectó
                await notifyFriendsOfDisconnectionAsync(username);

                gameStateManager.removeConnectedUser(username);

                logger.Debug("DisconnectionHandler: Removed {0} from social/connected users.", username);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DisconnectionHandler: Error cleaning up social for {0}.", username);
            }
        }

        private async Task notifyFriendsOfDisconnectionAsync(string username)
        {
            // TODO: Si quieres notificar a los amigos que el usuario se desconectó,
            // implementa la lógica aquí usando SocialLogic.getFriendsListAsync
            // y enviando notificaciones a cada amigo conectado.
            await Task.CompletedTask;
        }

        private void cleanupAuthenticationSession(string username)
        {
            try
            {
                if (userSessionManager.isUserLoggedIn(username))
                {
                    userSessionManager.removeSession(username);
                    logger.Debug("DisconnectionHandler: Removed authentication session for {0}.", username);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DisconnectionHandler: Error cleaning up auth session for {0}.", username);
            }
        }
    }
}