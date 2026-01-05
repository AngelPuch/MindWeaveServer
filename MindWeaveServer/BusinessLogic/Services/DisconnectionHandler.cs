using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.DataAccess.Abstractions;
using NLog;
using System;
using System.Collections.Generic;
using System.Data.Entity.Core;
using System.Data.SqlClient;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic.Services
{
    public class DisconnectionHandler : IDisconnectionHandler
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IUserSessionManager userSessionManager;
        private readonly IGameStateManager gameStateManager;
        private readonly GameSessionManager gameSessionManager;
        private readonly IPlayerRepository playerRepository;

        private readonly object disconnectionLock = new object();
        private readonly HashSet<string> usersBeingDisconnected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public DisconnectionHandler(
            IUserSessionManager userSessionManager,
            IGameStateManager gameStateManager,
            GameSessionManager gameSessionManager,
            IPlayerRepository playerRepository)
        {
            this.userSessionManager = userSessionManager ?? throw new ArgumentNullException(nameof(userSessionManager));
            this.gameStateManager = gameStateManager ?? throw new ArgumentNullException(nameof(gameStateManager));
            this.gameSessionManager = gameSessionManager ?? throw new ArgumentNullException(nameof(gameSessionManager));
            this.playerRepository = playerRepository ?? throw new ArgumentNullException(nameof(playerRepository));
        }

        public async Task handleFullDisconnectionAsync(string username, string reason)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("DisconnectionHandler: Attempted to disconnect null/empty username.");
                return;
            }

            lock (disconnectionLock)
            {
                if (usersBeingDisconnected.Contains(username))
                {
                    logger.Warn("DisconnectionHandler: Disconnection already in progress for {0}. Skipping.", username);
                    return;
                }

                usersBeingDisconnected.Add(username);
            }

            try
            {
                logger.Info("===== DISCONNECTION START: {0} (Reason: {1}) =====", username, reason);


                await cleanupFromActiveGamesAsync(username);

                await cleanupFromLobbiesAsync(username);

                cleanupFromLobbyChats(username);

                cleanupMatchmakingCallbacks(username);

                await cleanupFromSocialAsync(username);

                cleanupAuthenticationSession(username);

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
                // ╔═══════════════════════════════════════════════════════════════════════════════════╗
                // ║ CRITICAL FIX: Use findSessionByUsername to locate the player's active game        ║
                // ║ Then use handlePlayerLeaveAsync - the SAME method used by ALT+F4 / voluntary exit ║
                // ╚═══════════════════════════════════════════════════════════════════════════════════╝

                var session = gameSessionManager.findSessionByUsername(username);

                if (session == null)
                {
                    logger.Info("DisconnectionHandler: No active game session found for {0}.", username);
                    return;
                }

                string lobbyCode = session.LobbyCode;

                logger.Info("DisconnectionHandler: Found active game session {0} for {1}. " +
                            "Using handlePlayerLeaveAsync (same as ALT+F4 flow).", lobbyCode, username);

                // ╔═══════════════════════════════════════════════════════════════════════════════════╗
                // ║ THIS IS THE KEY FIX: Use handlePlayerLeaveAsync instead of handlePlayerDisconnect ║
                // ║ This ensures:                                                                      ║
                // ║   1. Other players are notified via broadcast(onPlayerLeftMatch)                   ║
                // ║   2. If only 1 player remains, endGameAsync(FORFEIT) is called                     ║
                // ║   3. The remaining player wins automatically                                       ║
                // ║   4. Stats are properly updated                                                    ║
                // ╚═══════════════════════════════════════════════════════════════════════════════════╝
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

                foreach (var kvp in gameStateManager.GuestUsernamesInLobby.ToArray())
                {
                    if (kvp.Value.Remove(username))
                    {
                        logger.Info("DisconnectionHandler: Removed {0} from guest tracking in lobby {1}.", username, kvp.Key);

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

        private void broadcastLobbyState(MindWeaveServer.Contracts.DataContracts.Matchmaking.LobbyStateDto lobby)
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

        private void tryNotifyPlayerOfLobbyUpdate(string playerName, MindWeaveServer.Contracts.DataContracts.Matchmaking.LobbyStateDto lobby)
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
            catch (CommunicationException)
            {
                // Ignore
            }
            catch (TimeoutException)
            {
                // Ignore
            }
            catch (ObjectDisposedException)
            {
                // Ignore
            }
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
                catch
                {
                    // Ignorar
                }
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
                        logger.Info("DisconnectionHandler: Removed {0} from chat in lobby {1}.", username, lobbyId);

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
                logger.Info("DisconnectionHandler: Removed matchmaking callback for {0}.", username);
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

                await notifyFriendsOfDisconnectionAsync();

                gameStateManager.removeConnectedUser(username);

                logger.Info("DisconnectionHandler: Removed {0} from social/connected users.", username);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DisconnectionHandler: Error cleaning up social for {0}.", username);
            }
        }

        private async Task notifyFriendsOfDisconnectionAsync()
        {
            await Task.CompletedTask;
        }

        private void cleanupAuthenticationSession(string username)
        {
            try
            {
                if (userSessionManager.isUserLoggedIn(username))
                {
                    userSessionManager.removeSession(username);
                    logger.Info("DisconnectionHandler: Removed authentication session for {0}.", username);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "DisconnectionHandler: Error cleaning up auth session for {0}.", username);
            }
        }

        private async Task<int?> getPlayerIdAsync(string username)
        {
            try
            {
                var player = await playerRepository.getPlayerByUsernameAsync(username);
                return player?.idPlayer;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "DisconnectionHandler: Could not get player ID for {0}.", username);
                return null;
            }
        }

    }
}