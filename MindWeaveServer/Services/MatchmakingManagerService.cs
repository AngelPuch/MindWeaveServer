using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class MatchmakingManagerService : IMatchmakingManager
    {
        private readonly MatchmakingLogic matchmakingLogic;
        private static readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbies = new ConcurrentDictionary<string, LobbyStateDto>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, IMatchmakingCallback> userCallbacks = new ConcurrentDictionary<string, IMatchmakingCallback>(StringComparer.OrdinalIgnoreCase);

        private string currentUsername = null;
        private IMatchmakingCallback currentUserCallback = null;
        private bool isDisconnected = false;

        public MatchmakingManagerService()
        {
            var dbContext = new MindWeaveDBEntities1();
            var matchmakingRepository = new MatchmakingRepository(dbContext);
            var playerRepository = new PlayerRepository(dbContext);
            matchmakingLogic = new MatchmakingLogic(matchmakingRepository, playerRepository, activeLobbies, userCallbacks);

            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed += channel_FaultedOrClosed;
            }
        }

        public async Task<LobbyCreationResultDto> createLobby(string hostUsername, LobbySettingsDto settingsDto)
        {
            if (!ensureSessionIsRegistered(hostUsername))
            {
                return new LobbyCreationResultDto { success = false, message = "Failed to establish communication channel." }; // TODO: Lang
            }

            try
            {
                return await matchmakingLogic.createLobbyAsync(hostUsername, settingsDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] !!! Service Exception in createLobby for {hostUsername}: {ex.ToString()}");
                return new LobbyCreationResultDto { success = false, message = Resources.Lang.GenericServerError };
            }
        }

        public async Task joinLobby(string username, string lobbyId)
        {
            if (!ensureSessionIsRegistered(username))
            {
                try { currentUserCallback?.lobbyCreationFailed("Failed to establish communication channel."); } catch { }
                return;
            }

            try
            {
                await matchmakingLogic.joinLobbyAsync(username, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] !!! Service Exception in joinLobby for {username}: {ex.ToString()}");
                try { currentUserCallback?.lobbyCreationFailed(Resources.Lang.GenericServerError); } catch { }
                await handleDisconnect();
            }
        }

        public async Task leaveLobby(string username, string lobbyId)
        {
            if (!ensureSessionIsRegistered(username)) { return; }

            try
            {
                await matchmakingLogic.leaveLobbyAsync(username, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] !!! Service Exception in leaveLobby for {username}: {ex.ToString()}");
            }
        }

        public async Task startGame(string hostUsername, string lobbyId)
        {
            if (!ensureSessionIsRegistered(hostUsername)) { return; }

            try
            {
                await matchmakingLogic.startGameAsync(hostUsername, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] !!! Service Exception in startGame by {hostUsername}: {ex.ToString()}");
            }
        }

        public async Task kickPlayer(string hostUsername, string playerToKickUsername, string lobbyId)
        {
            if (!ensureSessionIsRegistered(hostUsername)) { return; }

            try
            {
                await matchmakingLogic.kickPlayerAsync(hostUsername, playerToKickUsername, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] !!! Service Exception in kickPlayer by {hostUsername}: {ex.ToString()}");
            }
        }

        public async Task inviteToLobby(string inviterUsername, string invitedUsername, string lobbyId)
        {
            if (!ensureSessionIsRegistered(inviterUsername)) { return; }

            try
            {
                await matchmakingLogic.inviteToLobbyAsync(inviterUsername, invitedUsername, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] !!! Service Exception in inviteToLobby from {inviterUsername}: {ex.ToString()}");
            }
        }

        public async Task changeDifficulty(string hostUsername, string lobbyId, int newDifficultyId)
        {
            if (!ensureSessionIsRegistered(hostUsername)) { return; }

            try
            {
                await matchmakingLogic.changeDifficultyAsync(hostUsername, lobbyId, newDifficultyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] !!! Service Exception in changeDifficulty by {hostUsername}: {ex.ToString()}");
            }
        }

        private async void channel_FaultedOrClosed(object sender, EventArgs e)
        {
            await handleDisconnect();
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= channel_FaultedOrClosed;
                commObject.Closed -= channel_FaultedOrClosed;
            }
        }

        private bool tryRegisterCurrentUserCallback(string username)
        {
            if (OperationContext.Current == null)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] !!! CRITICAL: tryRegisterCurrentUserCallback failed, OperationContext is null for {username}.");
                return false;
            }

            try
            {
                IMatchmakingCallback callback = OperationContext.Current.GetCallbackChannel<IMatchmakingCallback>();
                if (callback == null)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] !!! CRITICAL: GetCallbackChannel returned null for {username}.");
                    return false;
                }

                currentUserCallback = callback;
                currentUsername = username;

                matchmakingLogic.registerCallback(username, currentUserCallback);

                ICommunicationObject commObject = currentUserCallback as ICommunicationObject;
                if (commObject != null)
                {
                    commObject.Faulted += channel_FaultedOrClosed;
                    commObject.Closed += channel_FaultedOrClosed;
                }

                Console.WriteLine($"[{DateTime.UtcNow:O}] Session and Callback registered for {username}.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] !!! CRITICAL: Exception in tryRegisterCurrentUserCallback for {username}: {ex.ToString()}");
                currentUsername = null;
                currentUserCallback = null;
                return false;
            }
        }

        private bool ensureSessionIsRegistered(string username)
        {
            if (isDisconnected) return false;

            if (!string.IsNullOrEmpty(currentUsername))
            {
                if (currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                Console.WriteLine($"[{DateTime.UtcNow:O}] !!! CRITICAL: Session previously registered for '{currentUsername}' received a call for '{username}'. Aborting and disconnecting session.");
                Task.Run(async () => await handleDisconnect());
                return false;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] !!! CRITICAL: Method called with null or empty username before session was registered.");
                return false;
            }

            return tryRegisterCurrentUserCallback(username);
        }

        private async Task handleDisconnect()
        {
            if (isDisconnected) return;
            isDisconnected = true;

            string userToDisconnect = currentUsername;

            Console.WriteLine($"[{DateTime.UtcNow:O}] Disconnect triggered for session. User: '{userToDisconnect ?? "UNKNOWN"}'");

            if (OperationContext.Current?.Channel != null)
            {
                OperationContext.Current.Channel.Faulted -= channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed -= channel_FaultedOrClosed;
            }

            if (currentUserCallback != null)
            {
                cleanupCallbackEvents(currentUserCallback as ICommunicationObject);
            }

            if (!string.IsNullOrWhiteSpace(userToDisconnect))
            {
                try
                {
                    await Task.Run(() => matchmakingLogic.handleUserDisconnect(userToDisconnect));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] !!! Exception during logic layer disconnect notification for {userToDisconnect}: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] No username was associated with this disconnecting session, skipping logic notification.");
            }

            currentUsername = null;
            currentUserCallback = null;
        }
    }
}