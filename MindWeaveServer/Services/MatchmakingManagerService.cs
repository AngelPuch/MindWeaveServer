using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Utilities.Email;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class MatchmakingManagerService : IMatchmakingManager
    {
        private readonly MatchmakingLogic matchmakingLogic;

        private static readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbies =
            new ConcurrentDictionary<string, LobbyStateDto>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, IMatchmakingCallback> userCallbacks =
            new ConcurrentDictionary<string, IMatchmakingCallback>(StringComparer.OrdinalIgnoreCase);

        private string currentUsername;
        private IMatchmakingCallback currentUserCallback;
        private bool isDisconnected;

        public MatchmakingManagerService()
        {
            var dbContext = new MindWeaveDBEntities1();
            var matchmakingRepository = new MatchmakingRepository(dbContext);
            var playerRepository = new PlayerRepository(dbContext);
            var guestInvitationRepository = new GuestInvitationRepository(dbContext); // Instantiate new repository
            var emailService = new SmtpEmailService();

            matchmakingLogic = new MatchmakingLogic(
                matchmakingRepository,
                playerRepository,
                guestInvitationRepository,
                emailService,
                activeLobbies,
                userCallbacks);

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
                return new LobbyCreationResultDto
                    { success = false, message = "Failed to establish communication channel." }; // TODO: Lang
            }

            try
            {
                return await matchmakingLogic.createLobbyAsync(hostUsername, settingsDto);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! Service Exception in createLobby for {hostUsername}: {ex.ToString()}");
                return new LobbyCreationResultDto { success = false, message = Resources.Lang.GenericServerError };
            }
        }

        public async Task joinLobby(string username, string lobbyId)
        {
            if (!ensureSessionIsRegistered(username))
            {
                try
                {
                    currentUserCallback?.lobbyCreationFailed("Failed to establish communication channel.");
                }
                catch
                {
                }

                return;
            }

            try
            {
                await matchmakingLogic.joinLobbyAsync(username, lobbyId, currentUserCallback);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! Service Exception in joinLobby for {username}: {ex.ToString()}");
                try
                {
                    currentUserCallback?.lobbyCreationFailed(Resources.Lang.GenericServerError);
                }
                catch
                {
                }

                await handleDisconnect();
            }
        }

        public async Task leaveLobby(string username, string lobbyId)
        {
            if (!ensureSessionIsRegistered(username))
            {
                return;
            }

            try
            {
                await matchmakingLogic.leaveLobbyAsync(username, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! Service Exception in leaveLobby for {username}: {ex.ToString()}");
            }
        }

        public async Task startGame(string hostUsername, string lobbyId)
        {
            if (!ensureSessionIsRegistered(hostUsername))
            {
                return;
            }

            try
            {
                await matchmakingLogic.startGameAsync(hostUsername, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! Service Exception in startGame by {hostUsername}: {ex.ToString()}");
            }
        }

        public async Task kickPlayer(string hostUsername, string playerToKickUsername, string lobbyId)
        {
            if (!ensureSessionIsRegistered(hostUsername))
            {
                return;
            }

            try
            {
                await matchmakingLogic.kickPlayerAsync(hostUsername, playerToKickUsername, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! Service Exception in kickPlayer by {hostUsername}: {ex.ToString()}");
            }
        }

        public async Task inviteToLobby(string inviterUsername, string invitedUsername, string lobbyId)
        {
            if (!ensureSessionIsRegistered(inviterUsername))
            {
                return;
            }

            try
            {
                await matchmakingLogic.inviteToLobbyAsync(inviterUsername, invitedUsername, lobbyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! Service Exception in inviteToLobby from {inviterUsername}: {ex.ToString()}");
            }
        }

        public async Task changeDifficulty(string hostUsername, string lobbyId, int newDifficultyId)
        {
            if (!ensureSessionIsRegistered(hostUsername))
            {
                return;
            }

            try
            {
                await matchmakingLogic.changeDifficultyAsync(hostUsername, lobbyId, newDifficultyId);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! Service Exception in changeDifficulty by {hostUsername}: {ex.ToString()}");
            }
        }


        public async Task inviteGuestByEmail(GuestInvitationDto invitationData)
        {
            if (matchmakingLogic == null) return;
            if (invitationData == null || string.IsNullOrWhiteSpace(invitationData.inviterUsername))
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] inviteGuestByEmail called with invalid invitation data.");
                return;
            }

            if (!ensureSessionIsRegistered(invitationData.inviterUsername))
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] inviteGuestByEmail called by user '{invitationData.inviterUsername}' but session is invalid or mismatched.");
                return;
            }

            try
            {
                await matchmakingLogic.inviteGuestByEmailAsync(invitationData);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! Service Exception in inviteGuestByEmail from {invitationData.inviterUsername} for {invitationData.guestEmail}, lobby {invitationData.lobbyCode}: {ex.ToString()}");
            }
        }

        public async Task<GuestJoinResultDto> joinLobbyAsGuest(GuestJoinRequestDto joinRequest)
        {
            if (matchmakingLogic == null)
                return new GuestJoinResultDto { success = false, message = "Service initialization failed." };

            if (isDisconnected)
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] joinLobbyAsGuest rejected: Service instance is marked as disconnected.");
                return new GuestJoinResultDto
                    { success = false, message = "Service connection closing." }; // TODO: Lang key
            }

            IMatchmakingCallback guestCallback = null;
            try
            {
                guestCallback = OperationContext.Current?.GetCallbackChannel<IMatchmakingCallback>();
                if (guestCallback == null)
                    throw new InvalidOperationException("Could not retrieve callback channel for guest.");

                GuestJoinResultDto result = await matchmakingLogic.joinLobbyAsGuestAsync(joinRequest, guestCallback);

                if (result.success && !string.IsNullOrWhiteSpace(result.assignedGuestUsername))
                {
                    currentUsername = result.assignedGuestUsername;
                    currentUserCallback = guestCallback;
                    setupCallbackEvents(guestCallback as ICommunicationObject);
                }

                return result;
            }
            catch (Exception ex)
            {
                try
                {
                    guestCallback?.lobbyCreationFailed(Resources.Lang.GenericServerError);
                }
                catch
                {

                }

                return new GuestJoinResultDto { success = false, message = Resources.Lang.GenericServerError };
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
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! CRITICAL: tryRegisterCurrentUserCallback failed, OperationContext is null for {username}.");
                return false;
            }

            try
            {
                IMatchmakingCallback callback = OperationContext.Current.GetCallbackChannel<IMatchmakingCallback>();
                if (callback == null)
                {
                    Console.WriteLine(
                        $"[{DateTime.UtcNow:O}] !!! CRITICAL: GetCallbackChannel returned null for {username}.");
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
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! CRITICAL: Exception in tryRegisterCurrentUserCallback for {username}: {ex.ToString()}");
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

                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! CRITICAL: Session previously registered for '{currentUsername}' received a call for '{username}'. Aborting and disconnecting session.");
                Task.Run(async () => await handleDisconnect());
                return false;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] !!! CRITICAL: Method called with null or empty username before session was registered.");
                return false;
            }

            return tryRegisterCurrentUserCallback(username);
        }

        private async Task handleDisconnect()
        {
            if (isDisconnected) return;
            isDisconnected = true;

            string userToDisconnect = currentUsername;

            Console.WriteLine(
                $"[{DateTime.UtcNow:O}] Disconnect triggered for session. User: '{userToDisconnect ?? "UNKNOWN"}'");

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
                    Console.WriteLine(
                        $"[{DateTime.UtcNow:O}] !!! Exception during logic layer disconnect notification for {userToDisconnect}: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine(
                    $"[{DateTime.UtcNow:O}] No username was associated with this disconnecting session, skipping logic notification.");
            }

            currentUsername = null;
            currentUserCallback = null;
        }

        private void setupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                // Remover handlers primero para evitar duplicados
                commObject.Faulted -= channel_FaultedOrClosed;
                commObject.Closed -= channel_FaultedOrClosed;
                // Añadir handlers
                commObject.Faulted += channel_FaultedOrClosed;
                commObject.Closed += channel_FaultedOrClosed;
                Debug.WriteLine(
                    $"[{DateTime.UtcNow:O}] Event handlers (Faulted/Closed) attached for user: {currentUsername ?? "UNKNOWN"} callback. Channel State: {commObject.State}");

            }
            else
            {
                Debug.WriteLine(
                    $"[{DateTime.UtcNow:O}] WARN: Attempted to setup callback events, but communication object was null for user: {currentUsername ?? "UNKNOWN"}.");
            }
        }
    }
}