using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Resources;
using MindWeaveServer.Services;
using MindWeaveServer.Utilities; 
using System;
using System.Collections.Concurrent; 
using System.Collections.Generic;
using System.Data.Entity.Infrastructure; 
using System.Linq;
using System.ServiceModel; 
using System.Threading.Tasks;
using MindWeaveServer.DataAccess.Abstractions;

namespace MindWeaveServer.BusinessLogic
{
    public class MatchmakingLogic
    {
        private readonly IMatchmakingRepository matchmakingRepository;
        private readonly IPlayerRepository playerRepository;
        private readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbies;
        private readonly ConcurrentDictionary<string, IMatchmakingCallback> userCallbacks;
        private const int MAX_LOBBY_CODE_GENERATION_ATTEMPTS = 10;
        private const int MAX_PLAYERS_PER_LOBBY = 4;
        private const int MATCH_STATUS_WAITING = 1;
        private const int MATCH_STATUS_IN_PROGRESS = 3;
        private const int MATCH_STATUS_CANCELED = 4;
        private const int DEFAULT_DIFFICULTY_ID = 1;
        private const int DEFAULT_PUZZLE_ID = 4;

        public MatchmakingLogic(
            IMatchmakingRepository matchmakingRepository,
            IPlayerRepository playerRepository,
            ConcurrentDictionary<string, LobbyStateDto> lobbies,
            ConcurrentDictionary<string, IMatchmakingCallback> callbacks)
        {
            this.matchmakingRepository = matchmakingRepository ?? throw new ArgumentNullException(nameof(matchmakingRepository));
            this.playerRepository = playerRepository ?? throw new ArgumentNullException(nameof(playerRepository));
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
                return;
            }

            var (didHostLeave, isLobbyClosed, remainingPlayers) = tryRemovePlayerFromMemory(lobbyState, username);

            if (remainingPlayers == null)
            {
                return;
            }
            
            await synchronizeDbOnLeaveAsync(username, lobbyCode, isLobbyClosed);

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
        
        public void handleUserDisconnect(string username)
        {
            List<string> lobbiesToLeave = activeLobbies
                .Where(kvp => kvp.Value.players.Contains(username)) 
                .Select(kvp => kvp.Key) 
                .ToList(); 

            foreach (var lobbyCode in lobbiesToLeave)
            {
                Task.Run(async () => await leaveLobbyAsync(username, lobbyCode));
            }
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

        public async Task kickPlayerAsync(string hostUsername, string playerToKickUsername, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                return;
            }

            bool kickedFromMemory = tryKickPlayerFromMemory(lobbyState, hostUsername, playerToKickUsername);

            if (kickedFromMemory)
            {
                await synchronizeDbOnKickAsync(playerToKickUsername, lobbyCode);
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