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

        private const string KICK_REASON_HOST_DISCONNECTED = "HOST_DISCONNECTED";

        public DisconnectionHandler(
            IUserSessionManager userSessionManager,
            IGameStateManager gameStateManager,
            GameSessionManager gameSessionManager)
        {
            this.userSessionManager = userSessionManager;
            this.gameStateManager = gameStateManager;
            this.gameSessionManager = gameSessionManager;
        }

        public async Task handleFullDisconnectionAsync(string username, string reason)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("DisconnectionHandler: Attempted to disconnect null/empty username.");
                return;
            }

            if (!tryAcquireDisconnectionLock(username))
            {
                return;
            }

            try
            {
                logger.Info("DISCONNECTION START (Reason: {Reason})", reason);

                await cleanupFromActiveGamesAsync(username);
                await cleanupFromLobbiesAsync(username);
                cleanupFromLobbyChats(username);
                cleanupMatchmakingCallbacks(username);
                await cleanupFromSocialAsync(username);
                cleanupAuthenticationSession(username);

                logger.Info("DISCONNECTION COMPLETE");
            }
            catch (EntityException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Database entity error during disconnection.");
            }
            catch (SqlException ex)
            {
                logger.Error(ex, "DisconnectionHandler: SQL error during disconnection.");
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Invalid operation during disconnection.");
            }
            finally
            {
                releaseDisconnectionLock(username);
            }
        }

        public async Task handleGameDisconnectionAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            if (!tryAcquireDisconnectionLock(username))
            {
                return;
            }

            try
            {
                logger.Info("GAME SERVICE DISCONNECTION");

                await cleanupFromActiveGamesAsync(username);
                await cleanupFromLobbiesAsync(username);
                cleanupFromLobbyChats(username);
                cleanupMatchmakingCallbacks(username);

                logger.Info("GAME DISCONNECTION COMPLETE");
            }
            catch (EntityException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Database entity error during game disconnection.");
            }
            catch (SqlException ex)
            {
                logger.Error(ex, "DisconnectionHandler: SQL error during game disconnection.");
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Invalid operation during game disconnection.");
            }
            finally
            {
                releaseDisconnectionLock(username);
            }
        }

        private bool tryAcquireDisconnectionLock(string username)
        {
            lock (disconnectionLock)
            {
                if (usersBeingDisconnected.Contains(username))
                {
                    logger.Info("DisconnectionHandler: Disconnection already in progress. Skipping duplicate call.");
                    return false;
                }

                usersBeingDisconnected.Add(username);
                return true;
            }
        }

        private void releaseDisconnectionLock(string username)
        {
            lock (disconnectionLock)
            {
                usersBeingDisconnected.Remove(username);
            }
        }

        private async Task cleanupFromActiveGamesAsync(string username)
        {
            try
            {
                var session = gameSessionManager.findSessionByUsername(username);

                if (session == null)
                {
                    logger.Debug("DisconnectionHandler: No active game session found for user.");
                    return;
                }

                string lobbyCode = session.LobbyCode;

                logger.Info("DisconnectionHandler: Found active game session {LobbyCode}. Processing leave.", lobbyCode);

                await gameSessionManager.handlePlayerLeaveAsync(lobbyCode, username);

                logger.Info("DisconnectionHandler: Successfully processed game exit from session {LobbyCode}.", lobbyCode);
            }
            catch (EntityException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Database error cleaning up games.");
            }
            catch (SqlException ex)
            {
                logger.Error(ex, "DisconnectionHandler: SQL error cleaning up games.");
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Invalid operation cleaning up games.");
            }
        }

        private async Task cleanupFromLobbiesAsync(string username)
        {
            try
            {
                var lobbiesToLeave = findLobbiesContainingUser(username);

                foreach (var lobbyCode in lobbiesToLeave)
                {
                    await processLobbyLeaveSafeAsync(lobbyCode, username);
                }

                cleanupGuestTracking(username);
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Error cleaning up lobbies.");
            }
        }

        private List<string> findLobbiesContainingUser(string username)
        {
            return gameStateManager.ActiveLobbies
                .Where(kvp =>
                {
                    lock (kvp.Value)
                    {
                        return kvp.Value.Players.Contains(username, StringComparer.OrdinalIgnoreCase);
                    }
                })
                .Select(kvp => kvp.Key)
                .ToList();
        }

        private async Task processLobbyLeaveSafeAsync(string lobbyCode, string username)
        {
            try
            {
                await processLobbyLeaveAsync(lobbyCode, username);
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Error leaving lobby {LobbyCode}.", lobbyCode);
            }
        }

        private void cleanupGuestTracking(string username)
        {
            var guestEntries = gameStateManager.GuestUsernamesInLobby
                .ToArray()
                .Where(kvp => kvp.Value.Contains(username));

            foreach (var kvp in guestEntries)
            {
                kvp.Value.Remove(username);
                logger.Debug("DisconnectionHandler: Removed user from guest tracking in lobby {LobbyCode}.", kvp.Key);

                if (kvp.Value.Count == 0)
                {
                    gameStateManager.GuestUsernamesInLobby.TryRemove(kvp.Key, out _);
                }
            }
        }

        private async Task processLobbyLeaveAsync(string lobbyCode, string username)
        {
            if (!gameStateManager.ActiveLobbies.TryGetValue(lobbyCode, out var lobby))
            {
                return;
            }

            var leaveResult = removePlayerFromLobby(lobby, username);

            if (!leaveResult.WasInLobby)
            {
                return;
            }

            logger.Info("DisconnectionHandler: Removed player from lobby {LobbyCode}. IsHost: {IsHost}", lobbyCode, leaveResult.IsHost);

            if (leaveResult.IsHost || (leaveResult.RemainingPlayers != null && leaveResult.RemainingPlayers.Count == 0))
            {
                closeLobbyAndNotify(lobbyCode, leaveResult.RemainingPlayers);
            }
            else
            {
                broadcastLobbyState(lobby);
            }

            await Task.CompletedTask;
        }

        private static LobbyLeaveResult removePlayerFromLobby(LobbyStateDto lobby, string username)
        {
            lock (lobby)
            {
                bool wasInLobby = lobby.Players.RemoveAll(p => p.Equals(username, StringComparison.OrdinalIgnoreCase)) > 0;
                bool isHost = lobby.HostUsername.Equals(username, StringComparison.OrdinalIgnoreCase);

                List<string> remainingPlayers = null;
                if (isHost || lobby.Players.Count == 0)
                {
                    remainingPlayers = lobby.Players.ToList();
                }

                return new LobbyLeaveResult
                {
                    WasInLobby = wasInLobby,
                    IsHost = isHost,
                    RemainingPlayers = remainingPlayers
                };
            }
        }

        private void closeLobbyAndNotify(string lobbyCode, List<string> playersToKick)
        {
            gameStateManager.ActiveLobbies.TryRemove(lobbyCode, out _);
            gameStateManager.GuestUsernamesInLobby.TryRemove(lobbyCode, out _);

            logger.Info("DisconnectionHandler: Lobby {LobbyCode} closed.", lobbyCode);

            if (playersToKick == null)
            {
                return;
            }

            foreach (var player in playersToKick)
            {
                notifyPlayerKicked(player, KICK_REASON_HOST_DISCONNECTED);
                cleanupMatchmakingCallbacks(player);
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
            catch (CommunicationException ex)
            {
                logger.Debug(ex, "CommunicationException while notifying player of lobby update.");
            }
            catch (TimeoutException ex)
            {
                logger.Debug(ex, "TimeoutException while notifying player of lobby update.");
            }
            catch (ObjectDisposedException ex)
            {
                logger.Debug(ex, "ObjectDisposedException while notifying player of lobby update.");
            }
        }

        private void notifyPlayerKicked(string username, string reason)
        {
            if (!gameStateManager.MatchmakingCallbacks.TryGetValue(username, out var callback))
            {
                return;
            }

            try
            {
                var commObj = callback as ICommunicationObject;
                if (commObj?.State == CommunicationState.Opened)
                {
                    callback.kickedFromLobby(reason);
                }
            }
            catch (CommunicationException ex)
            {
                logger.Debug(ex, "CommunicationException while notifying player of kick.");
            }
            catch (TimeoutException ex)
            {
                logger.Debug(ex, "TimeoutException while notifying player of kick.");
            }
            catch (ObjectDisposedException ex)
            {
                logger.Debug(ex, "ObjectDisposedException while notifying player of kick.");
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
                        logger.Debug("DisconnectionHandler: Removed user from chat in lobby {LobbyId}.", lobbyId);

                        if (usersInLobby.IsEmpty)
                        {
                            gameStateManager.LobbyChatUsers.TryRemove(lobbyId, out _);
                            gameStateManager.LobbyChatHistory.TryRemove(lobbyId, out _);
                        }
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Error cleaning up lobby chats.");
            }
        }

        private void cleanupMatchmakingCallbacks(string username)
        {
            if (gameStateManager.MatchmakingCallbacks.TryRemove(username, out _))
            {
                logger.Debug("DisconnectionHandler: Removed matchmaking callback for user.");
            }
        }

        private Task cleanupFromSocialAsync(string username)
        {
            try
            {
                if (!gameStateManager.isUserConnected(username))
                {
                    return Task.CompletedTask;
                }

                gameStateManager.removeConnectedUser(username);

                logger.Debug("DisconnectionHandler: Removed user from social/connected users.");
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Error cleaning up social.");
            }

            return Task.CompletedTask;
        }

        private void cleanupAuthenticationSession(string username)
        {
            try
            {
                if (userSessionManager.isUserLoggedIn(username))
                {
                    userSessionManager.removeSession(username);
                    logger.Debug("DisconnectionHandler: Removed authentication session for user.");
                }
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "DisconnectionHandler: Error cleaning up auth session.");
            }
        }

        private sealed class LobbyLeaveResult
        {
            public bool WasInLobby { get; set; }
            public bool IsHost { get; set; }
            public List<string> RemainingPlayers { get; set; }
        }
    }
}