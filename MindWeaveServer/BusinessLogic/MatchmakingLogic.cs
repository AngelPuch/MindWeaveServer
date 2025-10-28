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

namespace MindWeaveServer.BusinessLogic
{
    public class MatchmakingLogic
    {
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
        }


        public async Task<LobbyCreationResultDto> createLobbyAsync(string hostUsername, LobbySettingsDto settings)
        {
            if (string.IsNullOrWhiteSpace(hostUsername) || settings == null)
            {
                return new LobbyCreationResultDto { success = false, message = Lang.ErrorAllFieldsRequired };
            }

            var hostPlayer = await playerRepository.getPlayerByUsernameAsync(hostUsername);
            if (hostPlayer == null)
            {
                return new LobbyCreationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
            }

            Matches newMatch = await tryCreateUniqueLobbyAsync(settings, hostPlayer);

            if (newMatch == null)
            {
                return new LobbyCreationResultDto
                    { success = false, message = Lang.lobbyCodeGenerationFailed };
            }


            var initialState = buildInitialLobbyState(newMatch, hostUsername, settings);

            if (activeLobbies.TryAdd(newMatch.lobby_code, initialState))
            {
                return new LobbyCreationResultDto
                {
                    success = true, message = Lang.lobbyCreatedSuccessfully,
                    lobbyCode = newMatch.lobby_code, initialLobbyState = initialState
                };
            }
            return new LobbyCreationResultDto { success = false, message = Lang.lobbyRegistrationFailed };
        }

        public async Task joinLobbyAsync(string username, string lobbyCode, IMatchmakingCallback callback)
        {
            registerCallback(username, callback);
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                sendCallbackToUser(username, cb => cb.lobbyCreationFailed(string.Format(Lang.lobbyNotFoundOrInactive, lobbyCode)));
                return;
            }

            var (needsDbUpdate, proceed) = tryAddPlayerToMemory(lobbyState, username, lobbyCode);

            if (!proceed)
            {
                return; 
            }

            bool dbSyncSuccess = true;

            if (needsDbUpdate)
            {
                dbSyncSuccess = await tryAddParticipantToDatabaseAsync(username, lobbyCode, lobbyState);
            }

            if (dbSyncSuccess)
            {
                sendLobbyUpdateToAll(lobbyState);
            }
        }

        public async Task leaveLobbyAsync(string username, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                removeCallback(username);
                return;
            }

            bool wasGuest = false;
            if (guestUsernamesInLobby.TryGetValue(lobbyCode, out var guests))
            {
                wasGuest = guests.Remove(username);
                if (wasGuest) Console.WriteLine($"[LeaveLobby] User '{username}' identified as guest and removed from guest tracking for lobby '{lobbyCode}'.");
                if (guests.Count == 0)
                {
                    guestUsernamesInLobby.TryRemove(lobbyCode, out _);
                    Console.WriteLine($"[LeaveLobby] Guest tracking list removed for empty lobby '{lobbyCode}'.");
                }
            }


            var (didHostLeave, isLobbyClosed, remainingPlayers) = tryRemovePlayerFromMemory(lobbyState, username);

            if (remainingPlayers == null)
            {
                removeCallback(username);
                return;
            }

            if (!wasGuest)
            {
                await synchronizeDbOnLeaveAsync(username, lobbyCode, isLobbyClosed);
            }
            else if (isLobbyClosed)
            {
                await synchronizeDbOnLeaveAsync(null, lobbyCode, true);
            }


            if (isLobbyClosed)
            {
                handleLobbyClosure(lobbyCode, didHostLeave, remainingPlayers);
            }
            else
            {
                sendLobbyUpdateToAll(lobbyState);
            }
            removeCallback(username);
        }

        //TODO: Refactor
        public void handleUserDisconnect(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return;

            Console.WriteLine($"[HandleDisconnect] Processing disconnect for user: '{username}'");

            List<string> lobbiesToLeave = activeLobbies
                .Where(kvp => {
                    lock (kvp.Value) // Lock each lobby state while checking players list
                    {
                        return kvp.Value.players.Contains(username, StringComparer.OrdinalIgnoreCase);
                    }
                })
                .Select(kvp => kvp.Key)
                .ToList();

            Console.WriteLine($"[HandleDisconnect] User '{username}' found in lobbies: [{string.Join(", ", lobbiesToLeave)}]");

            foreach (var lobbyCode in lobbiesToLeave)
            {
                // Eliminar de la lista de invitados si estaba allí
                bool removedFromGuests = false;
                if (guestUsernamesInLobby.TryGetValue(lobbyCode, out var guests))
                {
                    removedFromGuests = guests.Remove(username);
                    if (removedFromGuests) Console.WriteLine($"[HandleDisconnect] Removed '{username}' from guest tracking for lobby '{lobbyCode}'.");
                    if (guests.Count == 0) // Limpiar si ya no hay invitados
                    {
                        guestUsernamesInLobby.TryRemove(lobbyCode, out _);
                        Console.WriteLine($"[HandleDisconnect] Guest tracking list removed for empty lobby '{lobbyCode}'.");
                    }
                }
                // Ejecutar leaveLobbyAsync en segundo plano para no bloquear
                Task.Run(async () => {
                    Console.WriteLine($"[HandleDisconnect] Initiating leaveLobbyAsync for '{username}' from lobby '{lobbyCode}'.");
                    await leaveLobbyAsync(username, lobbyCode);
                });
            }
            // Eliminar callback SIEMPRE al final, después de procesar lobbies
            removeCallback(username);
        }

        public async Task startGameAsync(string hostUsername, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.lobbyNotFoundOrInactive, lobbyCode)));
                return;
            }

            var (isValid, playersSnapshot) = validateLobbyStateAndGetSnapshot(lobbyState, hostUsername);
            if (!isValid)
            {
                return;
            }

            bool dbUpdateSuccess = await tryStartMatchInDatabaseAsync(lobbyCode, hostUsername);
            
            if (dbUpdateSuccess)
            {
                notifyAllAndCleanupLobby(lobbyCode, playersSnapshot);
            }
        }

        //TODO: Refactor
        public async Task kickPlayerAsync(string hostUsername, string playerToKickUsername, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                return;
            }

            bool isGuest = guestUsernamesInLobby.TryGetValue(lobbyCode, out var guests) && guests.Contains(playerToKickUsername);
            if (isGuest) Console.WriteLine($"[KickPlayer] Player '{playerToKickUsername}' is identified as a guest.");

            bool kickedFromMemory = tryKickPlayerFromMemory(lobbyState, hostUsername, playerToKickUsername);

            if (kickedFromMemory)
            {
                if (isGuest)
                {
                    if (guests != null)
                    {
                        guests.Remove(playerToKickUsername);
                        if (guests.Count == 0)
                        {
                            guestUsernamesInLobby.TryRemove(lobbyCode, out _);
                        }
                    }
                }
                else
                {
                    await synchronizeDbOnKickAsync(playerToKickUsername, lobbyCode);
                }

                notifyAllOnKick(lobbyState, playerToKickUsername);
            }
        }

        public Task inviteToLobbyAsync(string inviterUsername, string invitedUsername, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed(String.Format(Lang.LobbyNoLongerAvailable, lobbyCode)));
                return Task.CompletedTask;
            }

            if (!SocialManagerService.ConnectedUsers.ContainsKey(invitedUsername))
            {
                sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed($"{invitedUsername} {Lang.ErrorUserNotOnline}")); 
                return Task.CompletedTask;
            }
            
            lock (lobbyState)
            {
                if (lobbyState.players.Count >= MAX_PLAYERS_PER_LOBBY)
                {
                    sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.LobbyIsFull, lobbyCode)));
                    return Task.CompletedTask;
                }

                if (lobbyState.players.Contains(invitedUsername))
                {
                    sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.PlayerAlreadyInLobby, invitedUsername)));
                    return Task.CompletedTask; 
                }
            } 
            SocialManagerService.sendNotificationToUser(invitedUsername, cb => cb.notifyLobbyInvite(inviterUsername, lobbyCode));
            return Task.CompletedTask;
        }


        private void sendLobbyUpdateToAll(LobbyStateDto lobbyState)
        {
            if (lobbyState == null) return;
            List<string> currentPlayersSnapshot;
            lock (lobbyState) 
            {
                currentPlayersSnapshot = lobbyState.players.ToList();
            }
            foreach (var username in currentPlayersSnapshot)
            {
                sendCallbackToUser(username, cb => cb.updateLobbyState(lobbyState));
            }
        }

        public async Task changeDifficultyAsync(string hostUsername, string lobbyId, int newDifficultyId)
        {
            if (newDifficultyId < 1 || newDifficultyId > 3)
            {
                sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("Invalid difficulty selected."));
                return;
            }

            if (!activeLobbies.TryGetValue(lobbyId, out LobbyStateDto lobbyState))
            {
                return;
            }

            bool changedInMemory = tryChangeDifficultyInMemory(lobbyState, hostUsername, newDifficultyId);

            if (changedInMemory)
            {
                bool dbSyncSuccess = await trySynchronizeDifficultyToDbAsync(lobbyId, newDifficultyId, hostUsername);

                if (!dbSyncSuccess)
                {
                    return;
                }
                sendLobbyUpdateToAll(lobbyState);
            }
        }

        //TODO: REFACTOR
        public async Task inviteGuestByEmailAsync(GuestInvitationDto invitationData)
        {
            if (invitationData == null || string.IsNullOrWhiteSpace(invitationData.inviterUsername)
                || string.IsNullOrWhiteSpace(invitationData.guestEmail) || string.IsNullOrWhiteSpace(invitationData.lobbyCode))
            {
                sendCallbackToUser(invitationData?.inviterUsername, cb => cb.lobbyCreationFailed(Lang.ErrorInvalidInvitationData));
                return;
            }

            var inviterPlayer = await playerRepository.getPlayerByUsernameAsync(invitationData.inviterUsername);
            if (inviterPlayer == null)
            {
                sendCallbackToUser(invitationData.inviterUsername, cb => cb.lobbyCreationFailed(Lang.ErrorPlayerNotFound));
                return;
            }

            Matches match = await matchmakingRepository.getMatchByLobbyCodeAsync(invitationData.lobbyCode);
            if (match == null || match.match_status_id != MATCH_STATUS_WAITING) 
            {
                sendCallbackToUser(invitationData.inviterUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.lobbyNotFoundOrInactive, invitationData.lobbyCode)));
                Console.WriteLine($"[InviteGuest FAILED] Match for lobby '{invitationData.lobbyCode}' not found or not in waiting state.");
                return;
            }

            if (!activeLobbies.TryGetValue(invitationData.lobbyCode, out LobbyStateDto lobbyState))
            {
                sendCallbackToUser(invitationData.inviterUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.lobbyNotFoundOrInactive, invitationData.lobbyCode)));
                Console.WriteLine($"[InviteGuest FAILED] Lobby '{invitationData.lobbyCode}' not found in active lobbies despite match existing.");
                return;
            }
            lock (lobbyState)
            {
                if (lobbyState.players.Count >= MAX_PLAYERS_PER_LOBBY)
                {
                    sendCallbackToUser(invitationData.inviterUsername, cb => cb.lobbyCreationFailed(string.Format(Lang.LobbyIsFull, invitationData.lobbyCode)));
                    Console.WriteLine($"[InviteGuest FAILED] Lobby '{invitationData.lobbyCode}' is full.");
                    return;
                }
            }

            var invitation = new GuestInvitations
            {
                match_id = match.matches_id,
                guest_email = invitationData.guestEmail.Trim().ToLowerInvariant(),
                inviter_player_id = inviterPlayer.idPlayer,
                sent_timestamp = DateTime.UtcNow,
                expiry_timestamp = DateTime.UtcNow.AddMinutes(GUEST_INVITATION_EXPIRY_MINUTES),
                used_timestamp = null
            };

            try
            {
                await guestInvitationRepository.addInvitationAsync(invitation);
                await guestInvitationRepository.saveChangesAsync();

                var emailTemplate = new GuestInviteEmailTemplate(invitationData.inviterUsername, invitationData.lobbyCode);
                await emailService.sendEmailAsync(invitation.guest_email, invitation.guest_email, emailTemplate);

                Console.WriteLine($"[InviteGuest SUCCESS] Invitation sent to {invitation.guest_email} for lobby {invitationData.lobbyCode} (MatchID: {invitation.match_id}) by {invitationData.inviterUsername}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[InviteGuest FAILED] Error saving invitation or sending email: {ex.ToString()}");
                sendCallbackToUser(invitationData.inviterUsername, cb => cb.lobbyCreationFailed(Lang.ErrorSendingGuestInvitation)); 
            }
        }

        //TODO: REFACTOR
        public async Task<GuestJoinResultDto> joinLobbyAsGuestAsync(GuestJoinRequestDto joinRequest, IMatchmakingCallback callback)
        {
            if (joinRequest == null || string.IsNullOrWhiteSpace(joinRequest.lobbyCode)
                || string.IsNullOrWhiteSpace(joinRequest.guestEmail) || string.IsNullOrWhiteSpace(joinRequest.desiredGuestUsername))
            {
                return new GuestJoinResultDto { success = false, message = Lang.ErrorAllFieldsRequired };
            }

            string guestEmailLower = joinRequest.guestEmail.Trim().ToLowerInvariant();

            // *** CHANGE: Get Match ID first ***
            Matches match = await matchmakingRepository.getMatchByLobbyCodeAsync(joinRequest.lobbyCode);
            if (match == null || match.match_status_id != MATCH_STATUS_WAITING)
            {
                return new GuestJoinResultDto { success = false, message = string.Format(Lang.lobbyNotFoundOrInactive, joinRequest.lobbyCode) };
            }

            // *** CHANGE: Find invitation using match_id ***
            GuestInvitations validInvitation = await guestInvitationRepository.findValidInvitationAsync(match.matches_id, guestEmailLower);
            if (validInvitation == null)
            {
                return new GuestJoinResultDto { success = false, message = Lang.ErrorInvalidOrExpiredGuestInvite }; // Requires new Lang key
            }

            // Get lobby state from memory (should exist if match exists and is waiting)
            if (!activeLobbies.TryGetValue(joinRequest.lobbyCode, out LobbyStateDto lobbyState))
            {
                // Data inconsistency - log warning, return error
                Console.WriteLine($"[JoinGuest WARN] Match found for lobby '{joinRequest.lobbyCode}', but lobby state not found in activeLobbies.");
                return new GuestJoinResultDto { success = false, message = string.Format(Lang.lobbyNotFoundOrInactive, joinRequest.lobbyCode) };
            }

            string finalGuestUsername;
            bool addedToMemory = false;

            lock (lobbyState)
            {
                if (lobbyState.players.Count >= MAX_PLAYERS_PER_LOBBY)
                {
                    return new GuestJoinResultDto { success = false, message = string.Format(Lang.LobbyIsFull, joinRequest.lobbyCode) };
                }

                finalGuestUsername = findAvailableGuestUsername(joinRequest.lobbyCode, joinRequest.desiredGuestUsername);
                if (finalGuestUsername == null)
                {
                    return new GuestJoinResultDto { success = false, message = Lang.ErrorGuestUsernameGenerationFailed }; // Requires new Lang key
                }

                // Double check just in case findAvailableGuestUsername had stale data
                if (lobbyState.players.Any(p => p.Equals(finalGuestUsername, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"[JoinGuest WARN] Race condition? Username '{finalGuestUsername}' became occupied in lobby '{joinRequest.lobbyCode}'.");
                    // Optionally retry findAvailableGuestUsername or return error
                    return new GuestJoinResultDto { success = false, message = string.Format(Lang.ErrorGuestUsernameTaken, finalGuestUsername) }; // Requires new Lang key
                }

                lobbyState.players.Add(finalGuestUsername);
                addedToMemory = true;
                registerCallback(finalGuestUsername, callback);

                var guestsInThisLobby = guestUsernamesInLobby.GetOrAdd(joinRequest.lobbyCode, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                guestsInThisLobby.Add(finalGuestUsername);
                Console.WriteLine($"[JoinGuest MEMORY] Guest '{finalGuestUsername}' added to lobby '{joinRequest.lobbyCode}' state and guest tracking.");
            } // End lock

            try
            {
                await guestInvitationRepository.markInvitationAsUsedAsync(validInvitation);
                await guestInvitationRepository.saveChangesAsync();
                Console.WriteLine($"[JoinGuest DB] Invitation ID {validInvitation.invitation_id} marked as used.");
            }
            catch (Exception dbEx)
            {
                Console.WriteLine($"[JoinGuest WARN] Failed to mark invitation {validInvitation.invitation_id} as used: {dbEx.Message}");
                // Log and continue, user is already in memory lobby
            }

            sendLobbyUpdateToAll(lobbyState);

            return new GuestJoinResultDto
            {
                success = true,
                message = Lang.SuccessGuestJoinedLobby, // Requires new Lang key
                assignedGuestUsername = finalGuestUsername,
                initialLobbyState = lobbyState
            };
        }

        //TODO: Refactor
        private string findAvailableGuestUsername(string lobbyCode, string desiredUsername)
        {
            string baseUsername = desiredUsername.Trim();
            // Limitar longitud y quitar caracteres inválidos si es necesario
            if (baseUsername.Length > 16) baseUsername = baseUsername.Substring(0, 16);
            // Podrías añadir validación de caracteres aquí si quieres ser más estricto

            string finalUsername = baseUsername;
            int counter = 1;

            var guestsInThisLobby = guestUsernamesInLobby.GetOrAdd(lobbyCode, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            // Get current players from active lobby state for accurate check
            List<string> registeredPlayersInLobby = new List<string>();
            if (activeLobbies.TryGetValue(lobbyCode, out var state))
            {
                lock (state) // Ensure thread-safe read of players list
                {
                    registeredPlayersInLobby = state.players.ToList();
                }
            }


            // Comprobar contra invitados Y jugadores registrados en el lobby actual
            while (guestsInThisLobby.Contains(finalUsername) || registeredPlayersInLobby.Any(p => p.Equals(finalUsername, StringComparison.OrdinalIgnoreCase)))
            {
                // También podríamos comprobar contra la BD de jugadores registrados globalmente, pero
                // para invitados, la unicidad por lobby suele ser suficiente.
                // if (await playerRepository.getPlayerByUsernameAsync(finalUsername) != null) { ... }

                finalUsername = $"{baseUsername}{counter}";
                if (finalUsername.Length > 16)
                {
                    // Si añadir el número excede el límite, truncar base y añadir número
                    int availableLength = 16 - counter.ToString().Length;
                    if (availableLength < 1)
                    {
                        Console.WriteLine($"[FindGuestName ERROR] Cannot generate unique name for base '{desiredUsername}' in lobby '{lobbyCode}' within length limit.");
                        return null; // Imposible generar nombre único corto
                    }
                    baseUsername = baseUsername.Substring(0, availableLength);
                    finalUsername = $"{baseUsername}{counter}";
                }
                counter++;
                if (counter > 99)
                {
                    Console.WriteLine($"[FindGuestName ERROR] Reached counter limit trying to generate unique name for base '{desiredUsername}' in lobby '{lobbyCode}'.");
                    return null; // Evitar bucle infinito
                }
            }
            Console.WriteLine($"[FindGuestName] Assigned username '{finalUsername}' for desired '{desiredUsername}' in lobby '{lobbyCode}'.");
            return finalUsername;
        }

        public void sendCallbackToUser(string username, Action<IMatchmakingCallback> callbackAction)
        {
            if (userCallbacks.TryGetValue(username, out IMatchmakingCallback callbackChannel))
            {
                try
                {
                    ICommunicationObject commObject = callbackChannel as ICommunicationObject;
                    if (commObject != null && commObject.State == CommunicationState.Opened)
                    {
                        callbackAction(callbackChannel);
                    }
                    else
                    {
                        Task.Run(() => handleUserDisconnect(username));
                    }
                }
                catch (Exception ex)
                {
                    Task.Run(() => handleUserDisconnect(username));
                }
            }
        }
        public void removeCallback(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return;

            if (userCallbacks.TryRemove(username, out _))
            {
                Console.WriteLine($"Removed matchmaking callback for {username}.");
            }
        }

        public void registerCallback(string username, IMatchmakingCallback callback)
        {
            if (string.IsNullOrWhiteSpace(username) || callback == null) return;

            userCallbacks.AddOrUpdate(username, callback, (key, existingVal) =>
            {
                var existingComm = existingVal as ICommunicationObject;
                if (existingComm == null || existingComm.State != CommunicationState.Opened)
                {
                    return callback;
                }
                if (existingVal == callback) return existingVal;
                return callback; 
            });
        }

        private async Task<Matches> tryCreateUniqueLobbyAsync(LobbySettingsDto settings, Player hostPlayer)
        {
            Matches newMatch = null;
            int attempts = 0;
            while (newMatch == null && attempts < MAX_LOBBY_CODE_GENERATION_ATTEMPTS)
            {
                attempts++;
                string lobbyCode = LobbyCodeGenerator.generateUniqueCode();
                if (activeLobbies.ContainsKey(lobbyCode) || await matchmakingRepository.doesLobbyCodeExistAsync(lobbyCode))
                {
                    continue;
                }

                newMatch = buildNewMatch(settings, lobbyCode);
                try
                {
                    newMatch = await matchmakingRepository.createMatchAsync(newMatch);
                    await addHostParticipantAsync(newMatch, hostPlayer);
                }
                catch (DbUpdateException dbEx) when (dbEx.InnerException?.InnerException?.Message.Contains("UNIQUE KEY constraint") ?? false)
                {
                    newMatch = null;
                }
                catch (Exception ex)
                {
                    return null;
                }
            }
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
                    sendCallbackToUser(username, cb => cb.lobbyCreationFailed(string.Format(Lang.LobbyIsFull, lobbyCode)));
                    return (needsDbUpdate: false, proceed: false);
                }

                if (lobbyState.players.Contains(username))
                {
                    return (needsDbUpdate: false, proceed: true);
                }

                lobbyState.players.Add(username);
                return (needsDbUpdate: true, proceed: true);
            }
        }

        private async Task<bool> tryAddParticipantToDatabaseAsync(string username, string lobbyCode, LobbyStateDto lobbyState)
        {
            try
            {
                Player player = await playerRepository.getPlayerByUsernameAsync(username);
                Matches match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);

                if (player != null && match != null && match.match_status_id == MATCH_STATUS_WAITING)
                {
                    await addParticipantIfNotExistsAsync(match, player);
                    return true;
                } 
                else if (match?.match_status_id != MATCH_STATUS_WAITING)
                {
                    lock (lobbyState) { lobbyState.players.Remove(username); }
                    sendCallbackToUser(username, cb => cb.lobbyCreationFailed(string.Format(Lang.LobbyNoLongerAvailable, lobbyCode)));
                    
                    activeLobbies.TryRemove(lobbyCode, out _);
                    return false;
                }

                throw new Exception(string.Format(Lang.PlayerOrMatchNotFoundInDb, username, lobbyCode));
            }
            catch (Exception e)
            {
                lock (lobbyState) { lobbyState.players.Remove(username); }
                sendCallbackToUser(username, cb => cb.lobbyCreationFailed(Lang.ErrorJoiningLobbyData));
                return false;
            }
        }

        private async Task addParticipantIfNotExistsAsync(Matches match, Player player)
        {
            var existingParticipant = await matchmakingRepository.getParticipantAsync(match.matches_id, player.idPlayer);

            if (existingParticipant == null)
            {
                var newParticipant = new MatchParticipants
                {
                    match_id = match.matches_id,
                    player_id = player.idPlayer,
                    is_host = false
                };
                await matchmakingRepository.addParticipantAsync(newParticipant);
            }
        }

        private (bool didHostLeave, bool isLobbyClosed, List<string> remainingPlayers) tryRemovePlayerFromMemory(LobbyStateDto lobbyState, string username)
        {
            bool didHostLeave = false;
            bool isLobbyClosed = false;
            List<string> remainingPlayers = null;

            lock (lobbyState)
            {
                int initialCount = lobbyState.players.Count;
                lobbyState.players.RemoveAll(p => p.Equals(username, StringComparison.OrdinalIgnoreCase));
                bool removed = lobbyState.players.Count < initialCount;

                if (!removed)
                {
                    return (false, false, null);
                }

                // Check if the host left
                if (username.Equals(lobbyState.hostUsername, StringComparison.OrdinalIgnoreCase))
                {
                    didHostLeave = true;
                    isLobbyClosed = true;
                    remainingPlayers = lobbyState.players.ToList();
                }
                else if (lobbyState.players.Count == 0)
                {
                    isLobbyClosed = true;
                    remainingPlayers = new List<string>();
                }
                else
                {
                    remainingPlayers = lobbyState.players.ToList();
                }
            }

            return (didHostLeave, isLobbyClosed, remainingPlayers);
        }

        private async Task synchronizeDbOnLeaveAsync(string username, string lobbyCode, bool isLobbyClosed)
        {
            try
            {
                var player = await playerRepository.getPlayerByUsernameAsync(username);
                var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
                if (player == null || match == null)
                    return;

                var participant = await matchmakingRepository.getParticipantAsync(match.matches_id, player.idPlayer);
                if (participant != null)
                    await matchmakingRepository.removeParticipantAsync(participant);

                if (isLobbyClosed && match.match_status_id == MATCH_STATUS_WAITING)
                    await matchmakingRepository.updateMatchStatusAsync(match, MATCH_STATUS_CANCELED);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LeaveLobby DB ERROR for {username}, lobby {lobbyCode}: {ex}");
            }
        }

        private void handleLobbyClosure(string lobbyCode, bool didHostLeave, List<string> remainingPlayers)
        {
            if (didHostLeave && remainingPlayers != null)
            {
                foreach (var playerUsername in remainingPlayers)
                {
                    sendCallbackToUser(playerUsername, cb => cb.kickedFromLobby(Lang.HostLeftLobby));
                }
            }

            if (activeLobbies.TryRemove(lobbyCode, out _))
            {
                Console.WriteLine($"Lobby {lobbyCode} removed from active lobbies.");
            }
        }

        private (bool isValid, List<string> playersSnapshot) validateLobbyStateAndGetSnapshot(LobbyStateDto lobbyState, string hostUsername)
        {
            lock (lobbyState)
            {
                if (!lobbyState.hostUsername.Equals(hostUsername, StringComparison.OrdinalIgnoreCase))
                {
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.notHost));
                    return (false, null);
                }

                if (lobbyState.players.Count != MAX_PLAYERS_PER_LOBBY)
                {
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.NotEnoughPlayersToStart));
                    return (false, null);
                }
                return (true, lobbyState.players.ToList());
            }
        }

        private async Task<bool> tryStartMatchInDatabaseAsync(string lobbyCode, string hostUsername)
        {
            try
            {
                Matches match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);

                if (match == null)
                {
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.LobbyDataNotFound));
                    return false;
                }

                if (match.match_status_id != MATCH_STATUS_WAITING)
                {
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.LobbyNotInWaitingState));
                    return false;
                }
                Task statusUpdateTask = matchmakingRepository.updateMatchStatusAsync(match, MATCH_STATUS_IN_PROGRESS);
                Task timeUpdateTask = matchmakingRepository.updateMatchStartTimeAsync(match);
                await Task.WhenAll(statusUpdateTask, timeUpdateTask);

                return true;
            }
            catch (Exception ex)
            {
                sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.DatabaseErrorStartingMatch));
                return false;
            }
        }

        private void notifyAllAndCleanupLobby(string lobbyCode, List<string> playersSnapshot)
        {
            foreach (var playerUsername in playersSnapshot)
            {
                sendCallbackToUser(playerUsername, cb => cb.matchFound(lobbyCode, playersSnapshot));
            }

            if (activeLobbies.TryRemove(lobbyCode, out _))
            {
                Console.WriteLine($"Lobby {lobbyCode} removed from active lobbies as game started.");
            }
        }

        private bool tryKickPlayerFromMemory(LobbyStateDto lobbyState, string hostUsername, string playerToKickUsername)
        {
            lock (lobbyState)
            {
                if (!lobbyState.hostUsername.Equals(hostUsername, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (hostUsername.Equals(playerToKickUsername, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return lobbyState.players.Remove(playerToKickUsername);
            }
        }

        private async Task synchronizeDbOnKickAsync(string playerToKickUsername, string lobbyCode)
        {
            try
            {
                var playerKicked = await playerRepository.getPlayerByUsernameAsync(playerToKickUsername);
                var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);

                if (playerKicked != null && match != null)
                {
                    var participant = await matchmakingRepository.getParticipantAsync(match.matches_id, playerKicked.idPlayer);
                    if (participant != null)
                    {
                        await matchmakingRepository.removeParticipantAsync(participant);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"KickPlayer DB ERROR for {playerToKickUsername}, lobby {lobbyCode}: {ex.ToString()}");
                // Continue with notifications even if DB fails
            }
            
        }
        private void notifyAllOnKick(LobbyStateDto lobbyState, string playerToKickUsername)
        {
            sendCallbackToUser(playerToKickUsername, cb => cb.kickedFromLobby(Lang.KickedByHost));
            removeCallback(playerToKickUsername);
            sendLobbyUpdateToAll(lobbyState);
        }

        private bool tryChangeDifficultyInMemory(LobbyStateDto lobbyState, string hostUsername, int newDifficultyId)
        {
            lock (lobbyState)
            {
                if (!lobbyState.hostUsername.Equals(hostUsername, StringComparison.OrdinalIgnoreCase))
                {
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.notHost));
                    return false;
                }

                if (lobbyState.currentSettingsDto.difficultyId == newDifficultyId)
                {
                    return false;
                }

                lobbyState.currentSettingsDto.difficultyId = newDifficultyId;
                return true;
            }
        }

        private async Task<bool> trySynchronizeDifficultyToDbAsync(string lobbyId, int newDifficultyId, string hostUsername)
        {
            try
            {
                var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyId);

                if (match != null && match.match_status_id == MATCH_STATUS_WAITING)
                {
                    await matchmakingRepository.updateMatchDifficultyAsync(match, newDifficultyId);
                }

                return true;
            }
            catch (Exception ex)
            {
                sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed(Lang.ErrorSavingDifficultyChange));
                return false; 
            }
        }

    }
}