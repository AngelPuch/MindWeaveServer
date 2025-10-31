using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using MindWeaveServer.Services;
using MindWeaveServer.Utilities;
using MindWeaveServer.Utilities.Email;
using MindWeaveServer.Utilities.Email.Templates;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using NLog;

namespace MindWeaveServer.BusinessLogic
{
    public class MatchmakingLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IMatchmakingRepository matchmakingRepository;
        private readonly IPlayerRepository playerRepository;
        private readonly IGuestInvitationRepository guestInvitationRepository;
        private readonly IEmailService emailService;

        private readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbies;
        private readonly ConcurrentDictionary<string, IMatchmakingCallback> userCallbacks;
        private static readonly ConcurrentDictionary<string, HashSet<string>> guestUsernamesInLobby =
            new ConcurrentDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        private const int MAX_LOBBY_CODE_GENERATION_ATTEMPTS = 10;
        private const int MAX_PLAYERS_PER_LOBBY = 4;
        private const int MATCH_STATUS_WAITING = 1;
        private const int MATCH_STATUS_IN_PROGRESS = 3;
        private const int MATCH_STATUS_CANCELED = 4;
        private const int DEFAULT_DIFFICULTY_ID = 1;
        private const int DEFAULT_PUZZLE_ID = 4;
        private const int GUEST_INVITATION_EXPIRY_MINUTES = 10;

        public MatchmakingLogic(
            IMatchmakingRepository matchmakingRepository,
            IPlayerRepository playerRepository,
            IGuestInvitationRepository guestInvitationRepository,
            IEmailService emailService,
            ConcurrentDictionary<string, LobbyStateDto> lobbies,
            ConcurrentDictionary<string, IMatchmakingCallback> callbacks)
        {
            this.matchmakingRepository = matchmakingRepository ?? throw new ArgumentNullException(nameof(matchmakingRepository));
            this.playerRepository = playerRepository ?? throw new ArgumentNullException(nameof(playerRepository));
            this.guestInvitationRepository = guestInvitationRepository ?? throw new ArgumentNullException(nameof(guestInvitationRepository));
            this.emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            this.activeLobbies = lobbies ?? throw new ArgumentNullException(nameof(lobbies));
            this.userCallbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
            logger.Info("MatchmakingLogic instance created."); 
        }


        public async Task<LobbyCreationResultDto> createLobbyAsync(string hostUsername, LobbySettingsDto settings)
        {
            logger.Info("createLobbyAsync called by User: {Username}", hostUsername ?? "NULL");

            if (string.IsNullOrWhiteSpace(hostUsername) || settings == null)
            {
                logger.Warn("Lobby creation failed: Host username or settings are null/whitespace.");
                return new LobbyCreationResultDto { success = false, message = Lang.ErrorAllFieldsRequired };
            }

            try
            {
                logger.Debug("Fetching host player data for {Username}", hostUsername);
                var hostPlayer = await playerRepository.getPlayerByUsernameAsync(hostUsername);
                if (hostPlayer == null)
                {
                    logger.Warn("Lobby creation failed for {Username}: Player not found in DB.", hostUsername);
                    return new LobbyCreationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
                }

                logger.Debug("Attempting to create a unique lobby and match record for {Username}", hostUsername);
                Matches newMatch = await tryCreateUniqueLobbyAsync(settings, hostPlayer);

                if (newMatch == null)
                {
                    logger.Error("Lobby creation failed for {Username}: Could not create unique lobby/match record (check previous logs).", hostUsername);
                    return new LobbyCreationResultDto { success = false, message = Lang.lobbyCodeGenerationFailed }; // TODO: Lang key check
                }
                logger.Info("Match record created successfully (ID: {MatchId}, Code: {LobbyCode}) for host {Username}", newMatch.matches_id, newMatch.lobby_code, hostUsername);

                var initialState = buildInitialLobbyState(newMatch, hostUsername, settings); 

                if (activeLobbies.TryAdd(newMatch.lobby_code, initialState))
                {
                    logger.Info("Lobby {LobbyCode} successfully registered in active lobbies.", newMatch.lobby_code);
                    return new LobbyCreationResultDto
                    {
                        success = true,
                        message = Lang.lobbyCreatedSuccessfully, // TODO: Lang key check
                        lobbyCode = newMatch.lobby_code,
                        initialLobbyState = initialState
                    };
                }
                else
                {
                    logger.Error("Lobby creation failed for {Username} with code {LobbyCode}: Failed to add to activeLobbies dictionary (race condition?).", hostUsername, newMatch.lobby_code);
                    return new LobbyCreationResultDto { success = false, message = Lang.lobbyRegistrationFailed };
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during createLobbyAsync for User: {Username}", hostUsername);
                return new LobbyCreationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task joinLobbyAsync(string username, string lobbyCode, IMatchmakingCallback callback)
        {
            logger.Info("joinLobbyAsync called for User: {Username}, Lobby: {LobbyCode}", username ?? "NULL", lobbyCode ?? "NULL");

            registerCallback(username, callback);

            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                logger.Warn("Join lobby failed for {Username}: Lobby {LobbyCode} not found in active lobbies.", username, lobbyCode);
                sendCallbackToUser(username, cb => cb.lobbyCreationFailed(string.Format(Lang.lobbyNotFoundOrInactive, lobbyCode)));
                return;
            }

            var (needsDbUpdate, proceed) = tryAddPlayerToMemory(lobbyState, username, lobbyCode);

            if (!proceed)
            {
                logger.Warn("Join lobby aborted for {Username} in lobby {LobbyCode} based on memory state check (full or already joined).", username, lobbyCode);
                return;
            }

            bool dbSyncSuccess = true;
            if (needsDbUpdate)
            {
                logger.Debug("User {Username} added to memory lobby {LobbyCode}. Attempting to sync participant to DB.", username, lobbyCode);
                dbSyncSuccess = await tryAddParticipantToDatabaseAsync(username, lobbyCode, lobbyState);
            }
            else
            {
                logger.Debug("User {Username} was already in memory lobby {LobbyCode}. No DB update needed.", username, lobbyCode);
            }

            if (dbSyncSuccess)
            {
                logger.Info("Join lobby process successful for {Username} in lobby {LobbyCode}. Broadcasting update via individual callbacks.", username, lobbyCode);
                notifyLobbyStateChanged(lobbyState);
            }
            else
            {
                logger.Error("Join lobby failed for {Username} in lobby {LobbyCode} due to DB synchronization error.", username, lobbyCode);
            }
        }

        public async Task leaveLobbyAsync(string username, string lobbyCode)
        {
            logger.Info("leaveLobbyAsync called for User: {Username}, Lobby: {LobbyCode}", username ?? "NULL", lobbyCode ?? "NULL");

            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                logger.Warn("Leave lobby request ignored: Lobby {LobbyCode} not found in active lobbies.", lobbyCode);
                removeCallback(username);
                return;
            }

            bool wasGuest = false;
            
            if (guestUsernamesInLobby.TryGetValue(lobbyCode, out var currentGuests)) 
            {
                wasGuest = currentGuests.Remove(username);
                if (wasGuest)
                {
                    logger.Info("[LeaveLobby] User '{Username}' identified as guest and removed from guest tracking for lobby '{LobbyCode}'.", username, lobbyCode);
                }
                if (currentGuests.Count == 0)
                {
                    if (guestUsernamesInLobby.TryRemove(lobbyCode, out _))
                    {
                        logger.Debug("[LeaveLobby] Guest tracking list removed for empty lobby '{LobbyCode}'.", lobbyCode);
                    }
                }
            }

            var (didHostLeave, isLobbyClosed, remainingPlayers) = tryRemovePlayerFromMemory(lobbyState, username);

            if (remainingPlayers == null)
            {
                logger.Warn("User {Username} not found/removed from memory state for lobby {LobbyCode} during leave.", username, lobbyCode);
                removeCallback(username);
                return;
            }
            logger.Info("User {Username} removed from memory state for lobby {LobbyCode}. HostLeft={DidHostLeave}, LobbyClosed={IsLobbyClosed}", username, lobbyCode, didHostLeave, isLobbyClosed);

            
            if (!wasGuest)
            {
                logger.Debug("User {Username} was not a guest. Synchronizing leave with DB for lobby {LobbyCode}.", username, lobbyCode);
                await synchronizeDbOnLeaveAsync(username, lobbyCode, isLobbyClosed); 
            }
            else if (isLobbyClosed) 
            {
                logger.Debug("Guest {Username} was the last player. Synchronizing lobby closure (cancel match) with DB for lobby {LobbyCode}.", username, lobbyCode);
                await synchronizeDbOnLeaveAsync(null, lobbyCode, true); 
            }

            if (isLobbyClosed)
            {
                logger.Info("Lobby {LobbyCode} is now closed. Handling closure.", lobbyCode);
                handleLobbyClosure(lobbyCode, didHostLeave, remainingPlayers); 
            }
            else
            {
                logger.Info("Lobby {LobbyCode} remains active. Broadcasting update after user {Username} left.", lobbyCode, username);
                
                notifyLobbyStateChanged(lobbyState);
            }
            removeCallback(username); 
        }

        // TODO: Refactor (Logging añadido)
        public void handleUserDisconnect(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("handleUserDisconnect called with null or empty username.");
                return;
            }
            
            logger.Warn("[HandleDisconnect] Processing disconnect for user: '{Username}'", username);

            List<string> lobbiesToLeave = activeLobbies
                .Where(kvp => {
                    lock (kvp.Value) { return kvp.Value.players.Contains(username, StringComparer.OrdinalIgnoreCase); }
                })
                .Select(kvp => kvp.Key)
                .ToList();

            logger.Debug("[HandleDisconnect] User '{Username}' found in active lobbies: [{Lobbies}]", username, string.Join(", ", lobbiesToLeave));

            foreach (var lobbyCode in lobbiesToLeave)
            {
                logger.Debug("[HandleDisconnect] Processing disconnect actions for User '{Username}' in Lobby '{LobbyCode}'", username, lobbyCode);
                bool removedFromGuests;
                if (guestUsernamesInLobby.TryGetValue(lobbyCode, out var currentGuests)) 
                {
                    removedFromGuests = currentGuests.Remove(username);
                    if (removedFromGuests)
                    {
                        logger.Info("[HandleDisconnect] Removed '{Username}' from guest tracking for lobby '{LobbyCode}'.", username, lobbyCode);
                    }
                    if (currentGuests.Count == 0)
                    {
                        if (guestUsernamesInLobby.TryRemove(lobbyCode, out _))
                        {
                            logger.Debug("[HandleDisconnect] Guest tracking list removed for empty lobby '{LobbyCode}'.", lobbyCode);
                        }
                    }
                }

                Task.Run(async () => {
                    logger.Info("[HandleDisconnect] Initiating leaveLobbyAsync background task for '{Username}' from lobby '{LobbyCode}'.", username, lobbyCode);
                    try { await leaveLobbyAsync(username, lobbyCode); }
                    catch (Exception ex) { logger.Error(ex, "[HandleDisconnect] Exception in background leaveLobbyAsync task for User: {Username}, Lobby: {LobbyCode}", username, lobbyCode); }
                });
            }
            removeCallback(username);
        }

        public async Task startGameAsync(string hostUsername, string lobbyCode)
        {
            logger.Info("startGameAsync called by Host: {Username}, Lobby: {LobbyCode}", hostUsername ?? "NULL", lobbyCode ?? "NULL");

            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                logger.Warn("Start game failed: Lobby {LobbyCode} not found in active lobbies.", lobbyCode);
                sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.lobbyNotFoundOrInactive, lobbyCode)));
                return;
            }

            var (isValid, playersSnapshot) = validateLobbyStateAndGetSnapshot(lobbyState, hostUsername); 
            if (!isValid)
            {
                logger.Warn("Start game validation failed for Lobby {LobbyCode}, Host {Username}.", lobbyCode, hostUsername);
                return;
            }
            logger.Debug("Lobby state validation successful for starting game. Lobby: {LobbyCode}, Player count: {Count}", lobbyCode, playersSnapshot.Count);

            bool dbUpdateSuccess = await tryStartMatchInDatabaseAsync(lobbyCode, hostUsername); 

            if (dbUpdateSuccess)
            {
                logger.Info("Match {LobbyCode} successfully marked as started in DB. Notifying players and cleaning up lobby.", lobbyCode);
                notifyAllAndCleanupLobby(lobbyCode, playersSnapshot); 
            }
            else
            {
                logger.Error("Start game failed for Lobby {LobbyCode} due to DB update error.", lobbyCode);
            }
        }

        public async Task kickPlayerAsync(string hostUsername, string playerToKickUsername, string lobbyCode)
        {
            logger.Info("kickPlayerAsync called by Host: {HostUsername}, Target: {TargetUsername}, Lobby: {LobbyCode}", hostUsername ?? "NULL", playerToKickUsername ?? "NULL", lobbyCode ?? "NULL");

            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                logger.Warn("Kick player failed: Lobby {LobbyCode} not found.", lobbyCode);
                return;
            }

            bool isGuest;
            
            isGuest = guestUsernamesInLobby.TryGetValue(lobbyCode, out var currentGuests) && currentGuests.Contains(playerToKickUsername);
            if (isGuest) logger.Debug("[KickPlayer] Player '{TargetUsername}' is identified as a guest in lobby {LobbyCode}.", playerToKickUsername, lobbyCode);


            bool kickedFromMemory = tryKickPlayerFromMemory(lobbyState, hostUsername, playerToKickUsername); 

            if (kickedFromMemory)
            {
                logger.Info("Player {TargetUsername} successfully kicked from memory state of lobby {LobbyCode} by {HostUsername}.", playerToKickUsername, lobbyCode, hostUsername);

                if (isGuest)
                {
                    if (guestUsernamesInLobby.TryGetValue(lobbyCode, out var currentGuestsAfterKick))
                    {
                        if (currentGuestsAfterKick.Remove(playerToKickUsername))
                            logger.Debug("Removed kicked guest {TargetUsername} from guest tracking for lobby {LobbyCode}.", playerToKickUsername, lobbyCode);

                        if (currentGuestsAfterKick.Count == 0)
                        {
                            if (guestUsernamesInLobby.TryRemove(lobbyCode, out _))
                                logger.Debug("Guest tracking list removed for lobby {LobbyCode} after kicking last guest.", lobbyCode);
                        }
                    }
                }
                else
                {
                    logger.Debug("Kicked player {TargetUsername} was not a guest. Synchronizing kick with DB for lobby {LobbyCode}.", playerToKickUsername, lobbyCode);
                    await synchronizeDbOnKickAsync(playerToKickUsername, lobbyCode); 
                }
                notifyAllOnKick(lobbyState, playerToKickUsername); 
            }
            else
            {
                logger.Warn("Kick player failed for {TargetUsername} in lobby {LobbyCode}. See previous logs for reason.", playerToKickUsername, lobbyCode);
            }
        }

        public Task inviteToLobbyAsync(string inviterUsername, string invitedUsername, string lobbyCode)
        {
            logger.Info("inviteToLobbyAsync called by Inviter: {InviterUsername}, Target: {TargetUsername}, Lobby: {LobbyCode}", inviterUsername ?? "NULL", invitedUsername ?? "NULL", lobbyCode ?? "NULL");

            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                logger.Warn("Invite failed: Lobby {LobbyCode} not found.", lobbyCode);
                sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed(String.Format(Lang.LobbyNoLongerAvailable, lobbyCode)));
                return Task.CompletedTask;
            }

            if (invitedUsername != null && !SocialManagerService.connectedUsers.ContainsKey(invitedUsername))
            {
                logger.Warn("Invite failed: Target user {TargetUsername} is not online.", invitedUsername);
                sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed($"{invitedUsername} {Lang.ErrorUserNotOnline}"));
                return Task.CompletedTask;
            }

            lock (lobbyState)
            {
                if (lobbyState.players.Count >= MAX_PLAYERS_PER_LOBBY)
                {
                    logger.Warn("Invite failed: Lobby {LobbyCode} is full ({Count}/{Max}).", lobbyCode, lobbyState.players.Count, MAX_PLAYERS_PER_LOBBY);
                    sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.LobbyIsFull, lobbyCode)));
                    return Task.CompletedTask;
                }
                if (lobbyState.players.Contains(invitedUsername, StringComparer.OrdinalIgnoreCase))
                {
                    logger.Warn("Invite failed: Target user {TargetUsername} is already in lobby {LobbyCode}.", invitedUsername, lobbyCode);
                    sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.PlayerAlreadyInLobby, invitedUsername)));
                    return Task.CompletedTask;
                }
            } 

            logger.Info("Sending lobby invite notification to {TargetUsername} for lobby {LobbyCode} from {InviterUsername}.", invitedUsername, lobbyCode, inviterUsername);
            SocialManagerService.sendNotificationToUser(invitedUsername, cb => cb.notifyLobbyInvite(inviterUsername, lobbyCode)); 
            return Task.CompletedTask;
        }

        private void notifyLobbyStateChanged(LobbyStateDto lobbyState)
        {
            if (lobbyState == null)
            {
                logger.Warn("notifyLobbyStateChanged called with null lobbyState.");
                return;
            }
            List<string> currentPlayersSnapshot;
            lock (lobbyState) 
            {
                currentPlayersSnapshot = lobbyState.players.ToList();
            }
            logger.Debug("Broadcasting lobby state update for Lobby {LobbyId} to {Count} players.", lobbyState.lobbyId, currentPlayersSnapshot.Count);
            foreach (var username in currentPlayersSnapshot)
            {
                sendCallbackToUser(username, cb => cb.updateLobbyState(lobbyState));
            }
        }

        public async Task changeDifficultyAsync(string hostUsername, string lobbyId, int newDifficultyId)
        {
            logger.Info("changeDifficultyAsync called by Host: {Username}, Lobby: {LobbyId}, New Difficulty: {DifficultyId}", hostUsername ?? "NULL", lobbyId ?? "NULL", newDifficultyId);

            if (newDifficultyId < 1 || newDifficultyId > 3) 
            {
                logger.Warn("Change difficulty failed: Invalid difficulty ID ({DifficultyId}) requested.", newDifficultyId);
                sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("Invalid difficulty selected.")); // TODO: Lang key
                return;
            }

            if (!activeLobbies.TryGetValue(lobbyId, out LobbyStateDto lobbyState))
            {
                logger.Warn("Change difficulty failed: Lobby {LobbyId} not found.", lobbyId);
                return;
            }

            bool changedInMemory = tryChangeDifficultyInMemory(lobbyState, hostUsername, newDifficultyId);

            if (changedInMemory)
            {
                logger.Info("Difficulty changed in memory for lobby {LobbyId} to {DifficultyId}. Synchronizing with DB.", lobbyId, newDifficultyId);
                bool dbSyncSuccess = await trySynchronizeDifficultyToDbAsync(lobbyId, newDifficultyId, hostUsername);

                if (!dbSyncSuccess)
                {
                    logger.Error("Failed to synchronize difficulty change to DB for lobby {LobbyId}.", lobbyId);
                    return;
                }
                logger.Info("Difficulty change synchronized to DB for lobby {LobbyId}. Broadcasting update.", lobbyId);
                notifyLobbyStateChanged(lobbyState);
            }
            else
            {
                logger.Debug("Difficulty change not applied in memory for lobby {LobbyId}. See previous logs.", lobbyId);
            }
        }

        // TODO: Refactor (Logging añadido)
        public async Task inviteGuestByEmailAsync(GuestInvitationDto invitationData)
        {
            if (invitationData == null || string.IsNullOrWhiteSpace(invitationData.inviterUsername)
               || string.IsNullOrWhiteSpace(invitationData.guestEmail) || string.IsNullOrWhiteSpace(invitationData.lobbyCode))
            {
                logger.Warn("inviteGuestByEmail failed: Invalid invitation data provided.");
                sendCallbackToUser(invitationData?.inviterUsername, cb => cb.lobbyCreationFailed(Lang.ErrorInvalidInvitationData));
                return;
            }
            string inviterUsername = invitationData.inviterUsername;
            string guestEmail = invitationData.guestEmail;
            string lobbyCode = invitationData.lobbyCode;
            logger.Info("inviteGuestByEmailAsync called by Inviter: {InviterUsername}, Target Email: {GuestEmail}, Lobby: {LobbyCode}", inviterUsername, guestEmail, lobbyCode);

            try
            {
                logger.Debug("Fetching inviter player data: {InviterUsername}", inviterUsername);
                var inviterPlayer = await playerRepository.getPlayerByUsernameAsync(inviterUsername);
                if (inviterPlayer == null)
                {
                    logger.Warn("Invite guest failed: Inviter player {InviterUsername} not found.", inviterUsername);
                    sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed(Lang.ErrorPlayerNotFound));
                    return;
                }

                logger.Debug("Fetching match data for lobby code: {LobbyCode}", lobbyCode);
                Matches match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
                if (match == null || match.match_status_id != MATCH_STATUS_WAITING)
                {
                    logger.Warn("Invite guest failed: Match for lobby {LobbyCode} not found or not in waiting state (Status: {StatusId}).", lobbyCode, match?.match_status_id);
                    sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.lobbyNotFoundOrInactive, lobbyCode)));
                    return;
                }

                if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
                {
                    logger.Error("Invite guest inconsistency: Match {MatchId} exists for lobby {LobbyCode}, but lobby state not found in activeLobbies.", match.matches_id, lobbyCode);
                    sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.lobbyNotFoundOrInactive, lobbyCode)));
                    return;
                }
                lock (lobbyState)
                {
                    if (lobbyState.players.Count >= MAX_PLAYERS_PER_LOBBY)
                    {
                        logger.Warn("Invite guest failed: Lobby {LobbyCode} is full ({Count}/{Max}).", lobbyCode, lobbyState.players.Count, MAX_PLAYERS_PER_LOBBY);
                        sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.LobbyIsFull, lobbyCode)));
                        return;
                    }
                }

                var invitation = new GuestInvitations
                {
                    match_id = match.matches_id,
                    guest_email = guestEmail.Trim().ToLowerInvariant(),
                    inviter_player_id = inviterPlayer.idPlayer,
                    sent_timestamp = DateTime.UtcNow,
                    expiry_timestamp = DateTime.UtcNow.AddMinutes(GUEST_INVITATION_EXPIRY_MINUTES),
                    used_timestamp = null
                };

                logger.Debug("Attempting to save guest invitation to DB for Email: {GuestEmail}, Match: {MatchId}", invitation.guest_email, invitation.match_id);
                await guestInvitationRepository.addInvitationAsync(invitation);
                await guestInvitationRepository.saveChangesAsync();
                logger.Info("Guest invitation (ID: {InvitationId}) saved successfully.", invitation.invitation_id);

                var emailTemplate = new GuestInviteEmailTemplate(inviterUsername, lobbyCode);
                await emailService.sendEmailAsync(invitation.guest_email, invitation.guest_email, emailTemplate);

                logger.Info("[InviteGuest SUCCESS] Invitation sent to {GuestEmail} for lobby {LobbyCode} (MatchID: {MatchId}) by {InviterUsername}.", invitation.guest_email, lobbyCode, invitation.match_id, inviterUsername);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[InviteGuest FAILED] Error saving invitation or sending email for Lobby: {LobbyCode}, Inviter: {InviterUsername}", lobbyCode, inviterUsername);
                sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed(Lang.ErrorSendingGuestInvitation));
            }
        }

        // TODO: Refactor (Logging añadido)
        public async Task<GuestJoinResultDto> joinLobbyAsGuestAsync(GuestJoinRequestDto joinRequest, IMatchmakingCallback callback)
        {
            if (joinRequest == null || string.IsNullOrWhiteSpace(joinRequest.lobbyCode)
               || string.IsNullOrWhiteSpace(joinRequest.guestEmail) || string.IsNullOrWhiteSpace(joinRequest.desiredGuestUsername))
            {
                logger.Warn("joinLobbyAsGuest failed: Invalid join request data (null or missing fields).");
                return new GuestJoinResultDto { success = false, message = Lang.ErrorAllFieldsRequired };
            }
            string lobbyCode = joinRequest.lobbyCode;
            string guestEmailLower = joinRequest.guestEmail.Trim().ToLowerInvariant();
            string desiredUsername = joinRequest.desiredGuestUsername;
            logger.Info("joinLobbyAsGuestAsync called for Email: {GuestEmail}, Lobby: {LobbyCode}, Desired Username: {DesiredUsername}", guestEmailLower, lobbyCode, desiredUsername);

            try
            {
                logger.Debug("Fetching match data for lobby code: {LobbyCode}", lobbyCode);
                Matches match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
                if (match == null || match.match_status_id != MATCH_STATUS_WAITING)
                {
                    logger.Warn("Join guest failed: Match for lobby {LobbyCode} not found or not in waiting state (Status: {StatusId}).", lobbyCode, match?.match_status_id);
                    return new GuestJoinResultDto { success = false, message = string.Format(Lang.lobbyNotFoundOrInactive, lobbyCode) };
                }
                logger.Debug("Match record found (ID: {MatchId}) and is in waiting state.", match.matches_id);

                logger.Debug("Searching for valid guest invitation for Match: {MatchId}, Email: {GuestEmail}", match.matches_id, guestEmailLower);
                GuestInvitations validInvitation = await guestInvitationRepository.findValidInvitationAsync(match.matches_id, guestEmailLower);
                if (validInvitation == null)
                {
                    logger.Warn("Join guest failed: No valid (unused, non-expired) invitation found for Match: {MatchId}, Email: {GuestEmail}", match.matches_id, guestEmailLower);
                    return new GuestJoinResultDto { success = false, message = Lang.ErrorInvalidOrExpiredGuestInvite };
                }
                logger.Info("Valid guest invitation (ID: {InvitationId}) found.", validInvitation.invitation_id);

                if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
                {
                    logger.Error("Join guest inconsistency: Match {MatchId} and Invitation {InvitationId} exist, but lobby state {LobbyCode} not found in activeLobbies.", match.matches_id, validInvitation.invitation_id, lobbyCode);
                    
                    return new GuestJoinResultDto { success = false, message = string.Format(Lang.lobbyNotFoundOrInactive, lobbyCode) };
                }
                logger.Debug("Active lobby state found for {LobbyCode}.", lobbyCode);

                string finalGuestUsername;

                lock (lobbyState)
                {
                    if (lobbyState.players.Count >= MAX_PLAYERS_PER_LOBBY)
                    {
                        logger.Warn("Join guest failed: Lobby {LobbyCode} is full ({Count}/{Max}).", lobbyCode, lobbyState.players.Count, MAX_PLAYERS_PER_LOBBY);
                        return new GuestJoinResultDto { success = false, message = string.Format(Lang.LobbyIsFull, lobbyCode) };
                    }

                    finalGuestUsername = findAvailableGuestUsername(lobbyCode, desiredUsername); 
                    if (finalGuestUsername == null)
                    {
                        logger.Error("Join guest failed: Could not generate a unique guest username for Lobby {LobbyCode}, Desired: {DesiredUsername}", lobbyCode, desiredUsername);
                        return new GuestJoinResultDto { success = false, message = Lang.ErrorGuestUsernameGenerationFailed };
                    }

                    if (lobbyState.players.Any(p => p.Equals(finalGuestUsername, StringComparison.OrdinalIgnoreCase)))
                    {
                        logger.Error("Join guest race condition: Generated username '{FinalGuestUsername}' became occupied in lobby '{LobbyCode}' before adding.", finalGuestUsername, lobbyCode);
                        return new GuestJoinResultDto { success = false, message = string.Format(Lang.ErrorGuestUsernameTaken, finalGuestUsername) };
                    }

                    lobbyState.players.Add(finalGuestUsername);
                    registerCallback(finalGuestUsername, callback); 

                    var guestsInThisLobby = guestUsernamesInLobby.GetOrAdd(lobbyCode, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    guestsInThisLobby.Add(finalGuestUsername);
                    logger.Info("[JoinGuest MEMORY] Guest '{FinalGuestUsername}' added to lobby '{LobbyCode}' state and guest tracking.", finalGuestUsername, lobbyCode);
                }

                try
                {
                    logger.Debug("Attempting to mark invitation {InvitationId} as used in DB.", validInvitation.invitation_id);
                    await guestInvitationRepository.markInvitationAsUsedAsync(validInvitation);
                    await guestInvitationRepository.saveChangesAsync();
                    logger.Info("[JoinGuest DB] Invitation ID {InvitationId} marked as used.", validInvitation.invitation_id);
                }
                catch (Exception dbEx)
                {
                    logger.Warn(dbEx, "[JoinGuest WARN] Failed to mark invitation {InvitationId} as used. User {FinalGuestUsername} is already in memory lobby.", validInvitation.invitation_id, finalGuestUsername);
                }

                logger.Info("Guest {FinalGuestUsername} successfully joined lobby {LobbyCode}. Broadcasting update.", finalGuestUsername, lobbyCode);
                notifyLobbyStateChanged(lobbyState);

                return new GuestJoinResultDto
                {
                    success = true,
                    message = Lang.SuccessGuestJoinedLobby,
                    assignedGuestUsername = finalGuestUsername,
                    initialLobbyState = lobbyState
                };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during joinLobbyAsGuestAsync for Email: {GuestEmail}, Lobby: {LobbyCode}", guestEmailLower, lobbyCode);
                return new GuestJoinResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        // TODO: Refactor (Logging añadido)
        private string findAvailableGuestUsername(string lobbyCode, string desiredUsername)
        {
            string baseUsername = desiredUsername.Trim();
            logger.Debug("Finding available guest username for Lobby: {LobbyCode}, Desired: {DesiredUsername}", lobbyCode, baseUsername);
            if (baseUsername.Length > 16) baseUsername = baseUsername.Substring(0, 16);

            string finalUsername = baseUsername;
            int counter = 1;

            var guestsInThisLobby = guestUsernamesInLobby.GetOrAdd(lobbyCode, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            List<string> registeredPlayersInLobby = new List<string>();
            if (activeLobbies.TryGetValue(lobbyCode, out var state))
            {
                lock (state) { registeredPlayersInLobby = state.players.ToList(); }
            }

            while (guestsInThisLobby.Contains(finalUsername) || registeredPlayersInLobby.Any(p => p.Equals(finalUsername, StringComparison.OrdinalIgnoreCase)))
            {
                logger.Debug("Username '{FinalUsername}' is already taken in lobby {LobbyCode}. Generating next.", finalUsername, lobbyCode);
                string counterStr = counter.ToString();
                int availableLength = 16 - counterStr.Length;
                if (availableLength < 1)
                {
                    logger.Error("[FindGuestName ERROR] Cannot generate unique name for base '{BaseUsername}' in lobby '{LobbyCode}' within length limit (counter too high: {Counter}).", baseUsername, lobbyCode, counter);
                    return null;
                }
                string currentBase = baseUsername.Length > availableLength ? baseUsername.Substring(0, availableLength) : baseUsername;
                finalUsername = $"{currentBase}{counterStr}";

                counter++;
                if (counter > 99)
                {
                    logger.Error("[FindGuestName ERROR] Reached counter limit ({Counter}) trying to generate unique name for base '{BaseUsername}' in lobby '{LobbyCode}'.", counter - 1, baseUsername, lobbyCode);
                    return null;
                }
            }
            logger.Info("[FindGuestName] Assigned username '{FinalUsername}' for desired '{DesiredUsername}' in lobby '{LobbyCode}'.", finalUsername, desiredUsername, lobbyCode);
            return finalUsername;
        }

        public void sendCallbackToUser(string username, Action<IMatchmakingCallback> callbackAction)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("sendCallbackToUser ignored: Username is null or whitespace.");
                return;
            }
            if (userCallbacks.TryGetValue(username, out IMatchmakingCallback callbackChannel))
            {
                try
                {
                    ICommunicationObject commObject = callbackChannel as ICommunicationObject;
                    if (commObject != null && commObject.State == CommunicationState.Opened)
                    {
                        logger.Debug("Sending callback to User: {Username}", username);
                        callbackAction(callbackChannel);
                    }
                    else
                    {
                        logger.Warn("Callback channel for User: {Username} is not open (State: {State}). Triggering disconnect.", username, commObject?.State);
                        Task.Run(() => handleUserDisconnect(username));
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Exception sending callback to User: {Username}. Triggering disconnect.", username);
                    Task.Run(() => handleUserDisconnect(username));
                }
            }
            else
            {
                logger.Debug("No callback channel found for User: {Username}. Cannot send callback.", username);
            }
        }
        public void removeCallback(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Debug("removeCallback ignored: Username is null or whitespace.");
                return;
            }
            if (userCallbacks.TryRemove(username, out _))
            {
                logger.Info("Removed matchmaking callback for User: {Username}.", username);
            }
            else
            {
                logger.Debug("Callback for User: {Username} was not found or already removed.", username);
            }
        }

        public void registerCallback(string username, IMatchmakingCallback callback)
        {
            if (string.IsNullOrWhiteSpace(username) || callback == null)
            {
                logger.Warn("registerCallback ignored: Username or callback is null/whitespace.");
                return;
            }
            userCallbacks.AddOrUpdate(username, callback, (key, existingVal) =>
            {
                var existingComm = existingVal as ICommunicationObject;
                if (existingComm == null || existingComm.State != CommunicationState.Opened)
                {
                    logger.Debug("Registering new/replacing closed callback for User: {Username}", key);
                    return callback;
                }
                if (existingVal != callback)
                {
                    logger.Debug("Updating existing open callback with new instance for User: {Username}", key);
                    return callback;
                }
                logger.Debug("Re-registering same callback instance for User: {Username}", key);
                return existingVal;
            });
            logger.Info("Callback registered/updated for User: {Username}", username);
        }

        private async Task<Matches> tryCreateUniqueLobbyAsync(LobbySettingsDto settings, Player hostPlayer)
        {
            logger.Debug("Attempting to create unique lobby/match record...");
            Matches newMatch = null;
            int attempts = 0;
            while (newMatch == null && attempts < MAX_LOBBY_CODE_GENERATION_ATTEMPTS)
            {
                attempts++;
                logger.Debug("Attempt {Attempt}/{MaxAttempts} to generate unique lobby code.", attempts, MAX_LOBBY_CODE_GENERATION_ATTEMPTS);
                string lobbyCode = LobbyCodeGenerator.generateUniqueCode();

                if (activeLobbies.ContainsKey(lobbyCode))
                {
                    logger.Debug("Generated code {LobbyCode} collision in memory.", lobbyCode);
                    continue;
                }
                if (await matchmakingRepository.doesLobbyCodeExistAsync(lobbyCode))
                {
                    logger.Debug("Generated code {LobbyCode} collision in DB.", lobbyCode);
                    continue;
                }
                logger.Debug("Generated code {LobbyCode} appears unique. Attempting to create match record.", lobbyCode);

                Matches matchToCreate = buildNewMatch(settings, lobbyCode);
                try
                {
                    newMatch = await matchmakingRepository.createMatchAsync(matchToCreate);
                    await addHostParticipantAsync(newMatch, hostPlayer);
                    logger.Info("Successfully created Match (ID: {MatchId}) and added Host (PlayerID: {PlayerId}) for Lobby: {LobbyCode}", newMatch.matches_id, hostPlayer.idPlayer, lobbyCode);
                }
                catch (DbUpdateException dbEx) when (dbEx.InnerException?.InnerException?.Message.Contains("UNIQUE KEY constraint") ?? false)
                {
                    logger.Warn("DB unique key constraint violation for lobby code {LobbyCode} on attempt {Attempt}. Retrying.", lobbyCode, attempts);
                    newMatch = null;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed to create match record or add host participant for lobby code {LobbyCode} on attempt {Attempt}", lobbyCode, attempts);
                    return null;
                }
            }
            if (newMatch == null) { logger.Error("Exceeded max attempts ({MaxAttempts}) to generate a unique lobby code.", MAX_LOBBY_CODE_GENERATION_ATTEMPTS); }
            return newMatch;
        }

        private Matches buildNewMatch(LobbySettingsDto settings, string code)
        {
            return new Matches
            {
                creation_time = DateTime.UtcNow,
                match_status_id = MATCH_STATUS_WAITING,
                puzzle_id = settings.preloadedPuzzleId ?? DEFAULT_PUZZLE_ID,
                difficulty_id = settings.difficultyId > 0 ? settings.difficultyId : DEFAULT_DIFFICULTY_ID,
                lobby_code = code
            };
        }

        private async Task addHostParticipantAsync(Matches match, Player hostPlayer)
        {
            var hostParticipant = new MatchParticipants
            {
                match_id = match.matches_id,
                player_id = hostPlayer.idPlayer,
                is_host = true
            };
            await matchmakingRepository.addParticipantAsync(hostParticipant);
        }

        private LobbyStateDto buildInitialLobbyState(Matches match, string hostUsername, LobbySettingsDto settings)
        {
            return new LobbyStateDto
            {
                lobbyId = match.lobby_code,
                hostUsername = hostUsername,
                players = new List<string> { hostUsername },
                currentSettingsDto = settings
            };
        }

        private (bool needsDbUpdate, bool proceed) tryAddPlayerToMemory(LobbyStateDto lobbyState, string username, string lobbyCode)
        {
            lock (lobbyState)
            {
                if (lobbyState.players.Count >= MAX_PLAYERS_PER_LOBBY)
                {
                    logger.Warn("Cannot add User {Username} to Lobby {LobbyCode}: Lobby is full ({Count}/{Max}).", username, lobbyCode, lobbyState.players.Count, MAX_PLAYERS_PER_LOBBY);
                    sendCallbackToUser(username, cb => cb.lobbyCreationFailed(string.Format(Lang.LobbyIsFull, lobbyCode)));
                    return (needsDbUpdate: false, proceed: false);
                }
                if (lobbyState.players.Contains(username, StringComparer.OrdinalIgnoreCase))
                {
                    logger.Debug("User {Username} is already in the memory list for lobby {LobbyCode}. No add needed.", username, lobbyCode);
                    return (needsDbUpdate: false, proceed: true);
                }
                lobbyState.players.Add(username);
                logger.Debug("User {Username} added to memory list for lobby {LobbyCode}. Needs DB update.", username, lobbyCode);
                return (needsDbUpdate: true, proceed: true);
            }
        }

        private async Task<bool> tryAddParticipantToDatabaseAsync(string username, string lobbyCode, LobbyStateDto lobbyState)
        {
            try
            {
                logger.Debug("Attempting to add participant {Username} to DB for match corresponding to lobby {LobbyCode}", username, lobbyCode);
                Player player = await playerRepository.getPlayerByUsernameAsync(username);
                Matches match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);

                if (player != null && match != null && match.match_status_id == MATCH_STATUS_WAITING)
                {
                    logger.Debug("Player (ID: {PlayerId}) and Match (ID: {MatchId}) found and match is waiting. Adding participant if not exists.", player.idPlayer, match.matches_id);
                    await addParticipantIfNotExistsAsync(match, player); 
                    return true;
                }
                else if (match?.match_status_id != MATCH_STATUS_WAITING)
                {
                    logger.Warn("Cannot add participant {Username} to DB: Match {MatchId} (Lobby {LobbyCode}) is no longer in waiting state (Status: {StatusId}). Removing from memory.", username, match?.matches_id, lobbyCode, match?.match_status_id);
                    lock (lobbyState) { lobbyState.players.RemoveAll(p => p.Equals(username, StringComparison.OrdinalIgnoreCase)); }
                    sendCallbackToUser(username, cb => cb.lobbyCreationFailed(string.Format(Lang.LobbyNoLongerAvailable, lobbyCode)));
                    bool isEmpty; lock (lobbyState) { isEmpty = lobbyState.players.Count == 0; }
                    if (isEmpty) { logger.Info("Lobby {LobbyCode} became empty after failed DB sync. Removing from active lobbies.", lobbyCode); activeLobbies.TryRemove(lobbyCode, out _); }
                    return false;
                }
                else
                {
                    logger.Error("Cannot add participant {Username} to DB: Player or Match not found (PlayerFound={PlayerFound}, MatchFound={MatchFound}). Lobby: {LobbyCode}", username, player != null, true, lobbyCode);
                    lock (lobbyState) { lobbyState.players.RemoveAll(p => p.Equals(username, StringComparison.OrdinalIgnoreCase)); }
                    throw new Exception(string.Format(Lang.PlayerOrMatchNotFoundInDb, username, lobbyCode)); // TODO: Lang key
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception trying to add participant {Username} to DB for lobby {LobbyCode}. Removing from memory.", username, lobbyCode);
                lock (lobbyState) { lobbyState.players.RemoveAll(p => p.Equals(username, StringComparison.OrdinalIgnoreCase)); }
                sendCallbackToUser(username, cb => cb.lobbyCreationFailed(Lang.ErrorJoiningLobbyData));
                return false;
            }
        }

        private async Task addParticipantIfNotExistsAsync(Matches match, Player player)
        {
            logger.Debug("Checking if participant PlayerID: {PlayerId} already exists for MatchID: {MatchId}", player.idPlayer, match.matches_id);
            var existingParticipant = await matchmakingRepository.getParticipantAsync(match.matches_id, player.idPlayer);
            if (existingParticipant == null)
            {
                logger.Debug("Participant does not exist. Adding PlayerID: {PlayerId} to MatchID: {MatchId}", player.idPlayer, match.matches_id);
                var newParticipant = new MatchParticipants { match_id = match.matches_id, player_id = player.idPlayer, is_host = false };
                await matchmakingRepository.addParticipantAsync(newParticipant);
                logger.Info("Successfully added participant PlayerID: {PlayerId} to MatchID: {MatchId}", player.idPlayer, match.matches_id);
            }
            else { logger.Debug("Participant PlayerID: {PlayerId} already exists for MatchID: {MatchId}. No action needed.", player.idPlayer, match.matches_id); }
        }

        private (bool didHostLeave, bool isLobbyClosed, List<string> remainingPlayers) tryRemovePlayerFromMemory(LobbyStateDto lobbyState, string username)
        {
            bool didHostLeave; bool isLobbyClosed; List<string> remainingPlayers; bool removed;
            lock (lobbyState)
            {
                int initialCount = lobbyState.players.Count;
                lobbyState.players.RemoveAll(p => p.Equals(username, StringComparison.OrdinalIgnoreCase));
                removed = lobbyState.players.Count < initialCount;
                if (!removed) { logger.Debug("User {Username} not found in lobby {LobbyId} memory list during removal.", username, lobbyState.lobbyId); return (false, false, null); }
                logger.Debug("User {Username} removed from lobby {LobbyId} memory list.", username, lobbyState.lobbyId);
                didHostLeave = username.Equals(lobbyState.hostUsername, StringComparison.OrdinalIgnoreCase);
                isLobbyClosed = didHostLeave || lobbyState.players.Count == 0;
                remainingPlayers = lobbyState.players.ToList();
                logger.Debug("State after removal: HostLeft={DidHostLeave}, LobbyClosed={IsLobbyClosed}, RemainingCount={Count}", didHostLeave, isLobbyClosed, remainingPlayers.Count);
            }
            return (didHostLeave, isLobbyClosed, remainingPlayers);
        }

        private async Task synchronizeDbOnLeaveAsync(string username, string lobbyCode, bool isLobbyClosed)
        {
            if (username == null && !isLobbyClosed) return;
            try
            {
                logger.Debug("Synchronizing DB on leave/closure. User: {Username}, Lobby: {LobbyCode}, IsLobbyClosed: {IsLobbyClosed}", username ?? "N/A (Guest?)", lobbyCode, isLobbyClosed);
                var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
                if (match == null) { logger.Warn("Cannot synchronize DB on leave: Match not found for lobby {LobbyCode}.", lobbyCode); return; }
                if (username != null)
                {
                    var player = await playerRepository.getPlayerByUsernameAsync(username);
                    if (player == null) { logger.Warn("Cannot remove participant from DB: Player {Username} not found.", username); }
                    else
                    {
                        var participant = await matchmakingRepository.getParticipantAsync(match.matches_id, player.idPlayer);
                        if (participant != null)
                        {
                            logger.Debug("Removing participant PlayerID: {PlayerId} from MatchID: {MatchId}", player.idPlayer, match.matches_id);
                            await matchmakingRepository.removeParticipantAsync(participant);
                            logger.Info("Successfully removed participant PlayerID: {PlayerId} from MatchID: {MatchId}", player.idPlayer, match.matches_id);
                        }
                        else { logger.Warn("Participant PlayerID: {PlayerId} not found for MatchID: {MatchId} during DB sync on leave.", player.idPlayer, match.matches_id); }
                    }
                }
                if (isLobbyClosed && match.match_status_id == MATCH_STATUS_WAITING)
                {
                    logger.Info("Lobby {LobbyCode} closed while match was waiting. Updating match status to Canceled (ID: {MatchId}).", lobbyCode, match.matches_id);
                    await matchmakingRepository.updateMatchStatusAsync(match, MATCH_STATUS_CANCELED);
                    logger.Info("Match status updated to Canceled for MatchID: {MatchId}", match.matches_id);
                }
            }
            catch (Exception ex) { logger.Error(ex, "Exception during synchronizeDbOnLeaveAsync for User: {Username}, Lobby: {LobbyCode}", username ?? "N/A", lobbyCode); }
        }

        private void handleLobbyClosure(string lobbyCode, bool didHostLeave, List<string> remainingPlayers)
        {
            logger.Info("Handling closure of lobby {LobbyCode}. HostLeft={DidHostLeave}, RemainingPlayers={Count}", lobbyCode, didHostLeave, remainingPlayers?.Count ?? 0);
            if (didHostLeave && remainingPlayers != null && remainingPlayers.Count > 0)
            {
                logger.Info("Host left lobby {LobbyCode}. Notifying {Count} remaining players they were kicked.", lobbyCode, remainingPlayers.Count);
                foreach (var playerUsername in remainingPlayers) { sendCallbackToUser(playerUsername, cb => cb.kickedFromLobby(Lang.HostLeftLobby)); removeCallback(playerUsername); }
            }
            if (activeLobbies.TryRemove(lobbyCode, out _)) { logger.Info("Lobby {LobbyCode} successfully removed from active lobbies dictionary.", lobbyCode); }
            else { logger.Warn("Lobby {LobbyCode} was already removed from active lobbies dictionary.", lobbyCode); }
            if (guestUsernamesInLobby.TryRemove(lobbyCode, out _)) { logger.Debug("Guest tracking list removed for closed lobby {LobbyCode}.", lobbyCode); }
        }

        private (bool isValid, List<string> playersSnapshot) validateLobbyStateAndGetSnapshot(LobbyStateDto lobbyState, string hostUsername)
        {
            lock (lobbyState)
            {
                if (!lobbyState.hostUsername.Equals(hostUsername, StringComparison.OrdinalIgnoreCase))
                {
                    logger.Warn("Start game validation failed: User {Username} is not the host of lobby {LobbyId} (Host is {Host}).", hostUsername, lobbyState.lobbyId, lobbyState.hostUsername);
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.notHost));
                    return (false, null);
                }
                if (lobbyState.players.Count != MAX_PLAYERS_PER_LOBBY)
                {
                    logger.Warn("Start game validation failed: Lobby {LobbyId} does not have exactly {RequiredCount} players (Current: {CurrentCount}).", lobbyState.lobbyId, MAX_PLAYERS_PER_LOBBY, lobbyState.players.Count);
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.NotEnoughPlayersToStart));
                    return (false, null);
                }
                logger.Debug("Start game validation successful for lobby {LobbyId}.", lobbyState.lobbyId);
                return (true, lobbyState.players.ToList());
            }
        }

        private async Task<bool> tryStartMatchInDatabaseAsync(string lobbyCode, string hostUsername)
        {
            try
            {
                logger.Debug("Attempting to mark match as started in DB for lobby {LobbyCode}", lobbyCode);
                Matches match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
                if (match == null) { logger.Error("Cannot start match in DB: Match not found for lobby {LobbyCode}.", lobbyCode); sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.LobbyDataNotFound)); return false; }
                if (match.match_status_id != MATCH_STATUS_WAITING) { logger.Warn("Cannot start match in DB: Match {MatchId} (Lobby {LobbyCode}) is not in waiting state (Status: {StatusId}).", match.matches_id, lobbyCode, match.match_status_id); sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.LobbyNotInWaitingState)); return false; }
                logger.Debug("Updating match status to InProgress and setting start time for MatchID: {MatchId}", match.matches_id);
                Task statusUpdateTask = matchmakingRepository.updateMatchStatusAsync(match, MATCH_STATUS_IN_PROGRESS);
                Task timeUpdateTask = matchmakingRepository.updateMatchStartTimeAsync(match);
                await Task.WhenAll(statusUpdateTask, timeUpdateTask);
                logger.Info("Successfully updated match status and start time for MatchID: {MatchId}", match.matches_id);
                return true;
            }
            catch (Exception ex) { logger.Error(ex, "Exception trying to start match in DB for lobby {LobbyCode}", lobbyCode); sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.DatabaseErrorStartingMatch)); return false; }
        }

        private void notifyAllAndCleanupLobby(string lobbyCode, List<string> playersSnapshot)
        {
            logger.Info("Notifying {Count} players in lobby {LobbyCode} that the match has started.", playersSnapshot.Count, lobbyCode);
            foreach (var playerUsername in playersSnapshot) { sendCallbackToUser(playerUsername, cb => cb.matchFound(lobbyCode, playersSnapshot)); /* TODO: removeCallback(playerUsername); */ }
            if (activeLobbies.TryRemove(lobbyCode, out _)) { logger.Info("Lobby {LobbyCode} removed from active lobbies as game started.", lobbyCode); }
            else { logger.Warn("Lobby {LobbyCode} was already removed when trying to clean up after game start.", lobbyCode); }
            if (guestUsernamesInLobby.TryRemove(lobbyCode, out _)) { logger.Debug("Guest tracking list removed for started lobby {LobbyCode}.", lobbyCode); }
        }

        private bool tryKickPlayerFromMemory(LobbyStateDto lobbyState, string hostUsername, string playerToKickUsername)
        {
            lock (lobbyState)
            {
                if (!lobbyState.hostUsername.Equals(hostUsername, StringComparison.OrdinalIgnoreCase)) { logger.Warn("Kick player failed: User {Username} is not the host of lobby {LobbyId}.", hostUsername, lobbyState.lobbyId); return false; }
                if (hostUsername.Equals(playerToKickUsername, StringComparison.OrdinalIgnoreCase)) { logger.Warn("Kick player failed: Host {Username} cannot kick themselves from lobby {LobbyId}.", hostUsername, lobbyState.lobbyId); return false; }
                bool removed = lobbyState.players.RemoveAll(p => p.Equals(playerToKickUsername, StringComparison.OrdinalIgnoreCase)) > 0;
                if (!removed) { logger.Warn("Kick player failed: User {TargetUsername} not found in memory list for lobby {LobbyId}.", playerToKickUsername, lobbyState.lobbyId); }
                return removed;
            }
        }

        private async Task synchronizeDbOnKickAsync(string playerToKickUsername, string lobbyCode)
        {
            try
            {
                logger.Debug("Synchronizing kick with DB: Removing participant {TargetUsername} from match for lobby {LobbyCode}", playerToKickUsername, lobbyCode);
                var playerKicked = await playerRepository.getPlayerByUsernameAsync(playerToKickUsername);
                var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
                if (playerKicked != null && match != null)
                {
                    var participant = await matchmakingRepository.getParticipantAsync(match.matches_id, playerKicked.idPlayer);
                    if (participant != null)
                    {
                        logger.Debug("Found participant record for PlayerID: {PlayerId}, MatchID: {MatchId}. Removing...", playerKicked.idPlayer, match.matches_id);
                        await matchmakingRepository.removeParticipantAsync(participant);
                        logger.Info("Successfully removed participant PlayerID: {PlayerId} from MatchID: {MatchId} due to kick.", playerKicked.idPlayer, match.matches_id);
                    }
                    else { logger.Warn("Participant record not found for kicked PlayerID: {PlayerId}, MatchID: {MatchId}.", playerKicked.idPlayer, match.matches_id); }
                }
                else { logger.Warn("Cannot synchronize kick to DB: Player {TargetUsername} (Found={PlayerFound}) or Match for lobby {LobbyCode} (Found={MatchFound}) not found.", playerToKickUsername, playerKicked != null, lobbyCode, match != null); }
            }
            catch (Exception ex) { logger.Error(ex, "Exception during synchronizeDbOnKickAsync for User: {TargetUsername}, Lobby: {LobbyCode}", playerToKickUsername, lobbyCode); }
        }
        private void notifyAllOnKick(LobbyStateDto lobbyState, string playerToKickUsername)
        {
            logger.Info("Notifying players about kick of {TargetUsername} in lobby {LobbyId}", playerToKickUsername, lobbyState.lobbyId);
            sendCallbackToUser(playerToKickUsername, cb => cb.kickedFromLobby(Lang.KickedByHost));
            removeCallback(playerToKickUsername);
            notifyLobbyStateChanged(lobbyState);
        }

        private bool tryChangeDifficultyInMemory(LobbyStateDto lobbyState, string hostUsername, int newDifficultyId)
        {
            lock (lobbyState)
            {
                if (!lobbyState.hostUsername.Equals(hostUsername, StringComparison.OrdinalIgnoreCase)) { logger.Warn("Change difficulty failed: User {Username} is not the host of lobby {LobbyId}.", hostUsername, lobbyState.lobbyId); sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.notHost)); return false; }
                if (lobbyState.currentSettingsDto.difficultyId == newDifficultyId) { logger.Debug("Change difficulty ignored: Difficulty for lobby {LobbyId} is already {DifficultyId}.", lobbyState.lobbyId, newDifficultyId); return false; }
                lobbyState.currentSettingsDto.difficultyId = newDifficultyId;
                logger.Debug("Difficulty updated in memory for lobby {LobbyId} to {DifficultyId}.", lobbyState.lobbyId, newDifficultyId);
                return true;
            }
        }

        private async Task<bool> trySynchronizeDifficultyToDbAsync(string lobbyId, int newDifficultyId, string hostUsername)
        {
            try
            {
                logger.Debug("Attempting to synchronize difficulty change to DB for lobby {LobbyId}", lobbyId);
                var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyId);
                if (match != null && match.match_status_id == MATCH_STATUS_WAITING)
                {
                    logger.Debug("Match {MatchId} found and is waiting. Updating difficulty to {DifficultyId}", match.matches_id, newDifficultyId);
                    await matchmakingRepository.updateMatchDifficultyAsync(match, newDifficultyId);
                    logger.Info("Successfully updated difficulty in DB for MatchID: {MatchId}", match.matches_id);
                }
                else if (match == null) { logger.Warn("Cannot sync difficulty to DB: Match not found for lobby {LobbyId}.", lobbyId); }
                else { logger.Warn("Cannot sync difficulty to DB: Match {MatchId} (Lobby {LobbyId}) is no longer in waiting state (Status: {StatusId}).", match.matches_id, lobbyId, match.match_status_id); }
                return true;
            }
            catch (Exception ex) { logger.Error(ex, "Exception trying to synchronize difficulty change to DB for lobby {LobbyId}", lobbyId); sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.ErrorSavingDifficultyChange)); return false; }
        }
    }
}