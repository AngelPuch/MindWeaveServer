using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Resources;
using MindWeaveServer.Services;
using MindWeaveServer.Utilities; 
using System;
using System.Collections.Concurrent; 
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure; 
using System.Linq;
using System.ServiceModel; 
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class MatchmakingLogic
    {
        private readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbies;
        private readonly ConcurrentDictionary<string, IMatchmakingCallback> userCallbacks;
        private const int MAX_LOBBY_CODE_GENERATION_ATTEMPTS = 10;
        private const int MAX_PLAYERS_PER_LOBBY = 4;

    public MatchmakingLogic(
        ConcurrentDictionary<string, LobbyStateDto> lobbies,
        ConcurrentDictionary<string, IMatchmakingCallback> callbacks)
    {
        this.activeLobbies = lobbies;
        this.userCallbacks = callbacks;
    }

    public async Task<LobbyCreationResultDto> createLobbyAsync(string hostUsername, LobbySettingsDto settings)
    {
        string currentLobbyCode = null;
        Matches newMatch = null;    
        LobbyStateDto initialState = null;
        bool addedToMemory = false;
        int attempts = 0;

        using (var context = new MindWeaveDBEntities1())
        {
            Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Getting host player {hostUsername}...");
            var hostPlayer = await context.Player.AsNoTracking().FirstOrDefaultAsync(p => p.username == hostUsername);
            if (hostPlayer == null)
            {
                Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: Host player {hostUsername} not found.");
                return new LobbyCreationResultDto { success = false, message = "Host player not found." }; // TODO: Lang
            }
            Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Host player found (ID: {hostPlayer.idPlayer}). Generating lobby code...");

            while (newMatch == null && attempts < MAX_LOBBY_CODE_GENERATION_ATTEMPTS)
            {
                attempts++;
                currentLobbyCode = LobbyCodeGenerator.generateUniqueCode();
                Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Attempt {attempts}, generated code: {currentLobbyCode}");

                if (activeLobbies.ContainsKey(currentLobbyCode))
                {
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Code {currentLobbyCode} exists in memory. Retrying...");
                    continue;
                }

                bool codeExistsInDb = await context.Matches.AnyAsync(m => m.lobby_code == currentLobbyCode);
                if (codeExistsInDb)
                {
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Code {currentLobbyCode} exists in DB. Retrying...");
                    continue; 
                }

                newMatch = new Matches
                {
                    creation_time = DateTime.UtcNow,
                    match_status_id = 1,
                    puzzle_id = settings.preloadedPuzzleId ?? 3, 
                    difficulty_id = 1,
                    lobby_code = currentLobbyCode
                };

                Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Match entity prepared for DB. Code={newMatch.lobby_code}, StatusID={newMatch.match_status_id}, PuzzleID={newMatch.puzzle_id}, DifficultyID={newMatch.difficulty_id}");
                context.Matches.Add(newMatch);

                try
                {
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: BEFORE SaveChangesAsync for {currentLobbyCode}");
                    await context.SaveChangesAsync();
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: AFTER SaveChangesAsync for {currentLobbyCode}. Match ID: {newMatch.matches_id}");
                    var hostParticipant = new MatchParticipants
                    {
                        match_id = newMatch.matches_id,
                        player_id = hostPlayer.idPlayer,
                        is_host = true 
                    };
                    context.MatchParticipants.Add(hostParticipant);
                    await context.SaveChangesAsync(); 
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Host participant saved for Match ID: {newMatch.matches_id}, Player ID: {hostPlayer.idPlayer}");
                    // Si llegamos aquí todo se guardo
                    }
                    catch (DbUpdateException dbEx) 
                    {
                    string errorDetails = dbEx.InnerException?.InnerException?.Message ?? dbEx.InnerException?.Message ?? dbEx.Message;
                    Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: DbUpdateException saving match {currentLobbyCode}: {errorDetails}");
                    Console.WriteLine($"--- Full DbUpdateException Trace: {dbEx.ToString()}"); 
                    if (errorDetails.Contains("UNIQUE KEY constraint") && (errorDetails.Contains("lobby_code") || errorDetails.Contains("UQ__Matches")))
                    {
                        Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Lobby code collision in DB. Detaching entity and retrying...");
                        if (context.Entry(newMatch).State != EntityState.Detached) { context.Entry(newMatch).State = EntityState.Detached; }
                        newMatch = null;
                    }
                    else 
                    {
                        if (newMatch != null && context.Entry(newMatch).State != EntityState.Detached) { context.Entry(newMatch).State = EntityState.Detached; } 
                        return new LobbyCreationResultDto { success = false, message = $"Database error: {errorDetails}", lobbyCode = null, initialLobbyState = null }; // Mensaje específico
                    }
                }
                catch (Exception ex) 
                {
                    Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: Generic Exception saving match {currentLobbyCode}: {ex.ToString()}");
                    if (newMatch != null && context.Entry(newMatch).State != EntityState.Detached) { context.Entry(newMatch).State = EntityState.Detached; }
                        return new LobbyCreationResultDto { success = false, message = $"Failed to save lobby data: {ex.Message}", lobbyCode = null, initialLobbyState = null }; // Mensaje simple
                }
            } 
            if (newMatch == null)
            {
                Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: Failed to generate and save a unique lobby code after {attempts} attempts.");
                return new LobbyCreationResultDto { success = false, message = "Failed to generate a unique lobby code.", lobbyCode = null, initialLobbyState = null }; // TODO: Lang
            }
            Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Creating initial LobbyStateDto for {currentLobbyCode}...");
            initialState = new LobbyStateDto
            {
                lobbyId = newMatch.lobby_code,
                hostUsername = hostUsername,
                players = new List<string> { hostUsername },
                currentSettingsDto = settings 
            };

            Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Adding lobby {currentLobbyCode} to activeLobbies dictionary...");
            if (activeLobbies.TryAdd(newMatch.lobby_code, initialState))
            {
                addedToMemory = true;
                Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Lobby {currentLobbyCode} successfully added to memory.");
            }
            else
            {
                Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: Failed to add lobby {currentLobbyCode} to memory dictionary (race condition?).");
                
                return new LobbyCreationResultDto { success = false, message = "Failed to register lobby in memory after DB save.", lobbyCode = null, initialLobbyState = null };
            }

        }
        if (addedToMemory && initialState != null) 
        {
            Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Returning SUCCESS result for lobby {currentLobbyCode}.");
            return new LobbyCreationResultDto
            {
                success = true,
                message = "Lobby created successfully.",
                lobbyCode = currentLobbyCode,
                initialLobbyState = initialState
            };
        }
        else
        {
            Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: Returning FAILURE (unexpected state) after DB save for {currentLobbyCode}.");
            return new LobbyCreationResultDto { success = false, message = "Unexpected error after saving lobby.", lobbyCode = null, initialLobbyState = null };
        }
    } // Fin


    public async void joinLobby(string username, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                Console.WriteLine($"JoinLobby Error: Lobby {lobbyCode} not found for user {username}.");
                sendCallbackToUser(username, cb => cb.lobbyCreationFailed($"Lobby {lobbyCode} not found or is inactive.")); // TODO: Lang Key
                return;
            }

            bool addedToMemory = false;
            int matchId = -1;
            int playerId = -1; 
            {
                if (lobbyState.players.Count >= MAX_PLAYERS_PER_LOBBY)
                {
                    Console.WriteLine($"JoinLobby Error: Lobby {lobbyCode} is full (Max: {MAX_PLAYERS_PER_LOBBY}). User: {username}");
                    sendCallbackToUser(username, cb => cb.lobbyCreationFailed($"Lobby {lobbyCode} is full.")); // TODO: Lang Key
                    return;
                }

                if (lobbyState.players.Contains(username))
                {
                    Console.WriteLine($"JoinLobby Info: User {username} already in lobby {lobbyCode}. Resending state.");
                }
                else
                {
                    lobbyState.players.Add(username);
                    addedToMemory = true;
                    Console.WriteLine($"User {username} joined lobby {lobbyCode}. Players: {string.Join(", ", lobbyState.players)}");
                }
            } 
            if (addedToMemory) 
            {
                using (var context = new MindWeaveDBEntities1())
                {
                    try
                    {
                        var match = await context.Matches
                                                .AsNoTracking() 
                                                .FirstOrDefaultAsync(m => m.lobby_code == lobbyCode);

                        // Buscar el ID del jugador
                        var player = await context.Player
                                                .AsNoTracking()
                                                .FirstOrDefaultAsync(p => p.username == username);

                        if (match != null && player != null)
                        {
                            matchId = match.matches_id;
                            playerId = player.idPlayer;

                            bool alreadyExists = await context.MatchParticipants
                                                            .AnyAsync(mp => mp.match_id == matchId && mp.player_id == playerId);

                            if (!alreadyExists)
                            {
                                var newParticipant = new MatchParticipants
                                {
                                    match_id = matchId,
                                    player_id = playerId,
                                    is_host = false 
                                };
                                context.MatchParticipants.Add(newParticipant);
                                await context.SaveChangesAsync();
                                Console.WriteLine($"JoinLobby DB: Added participant PlayerID={playerId} to MatchID={matchId}");
                            }
                            else { /* Log: Ya existía */ }
                        }
                        else { /* Log: No se encontró partida o jugador */ }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"!!! JoinLobby DB ERROR for PlayerID={playerId}, MatchID={matchId}: {ex.ToString()}");
                    }
                }
            } 
            sendLobbyUpdateToAll(lobbyState);
        }
        public async void leaveLobby(string username, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                Console.WriteLine($"LeaveLobby Error: Lobby {lobbyCode} not found for user {username}.");
                return;
            }

            bool hostLeft = false;
            bool lobbyClosed = false;
            List<string> remainingPlayers = null;
            bool removedFromMemory = false;

            lock (lobbyState)
            {
                if (!lobbyState.players.Remove(username))
                {
                    Console.WriteLine($"LeaveLobby Info: User {username} was not in lobby {lobbyCode}.");
                    return;
                }
                removedFromMemory = true; // Se quitó de la memoria

                Console.WriteLine($"User {username} left lobby {lobbyCode} in memory. Remaining: {string.Join(", ", lobbyState.players)}");

                if (username == lobbyState.hostUsername)
                {
                    hostLeft = true;
                    lobbyClosed = true;
                    remainingPlayers = lobbyState.players.ToList();
                    Console.WriteLine($"Host {username} left lobby {lobbyCode}. Lobby closing.");
                }
                else if (lobbyState.players.Count == 0)
                {
                    lobbyClosed = true;
                    Console.WriteLine($"Last player {username} left lobby {lobbyCode}. Lobby closing.");
                }
            }
            if (removedFromMemory)
            {
                using (var context = new MindWeaveDBEntities1())
                {
                    try
                    {
                        var match = await context.Matches.FirstOrDefaultAsync(m => m.lobby_code == lobbyCode);
                        var player = await context.Player.FirstOrDefaultAsync(p => p.username == username);

                        if (match != null && player != null)
                        {
                            var participantToRemove = await context.MatchParticipants
                                                                .FirstOrDefaultAsync(mp => mp.match_id == match.matches_id && mp.player_id == player.idPlayer);

                            if (participantToRemove != null)
                            {
                                context.MatchParticipants.Remove(participantToRemove);
                                await context.SaveChangesAsync();
                                Console.WriteLine($"LeaveLobby DB: Removed participant PlayerID={player.idPlayer} from MatchID={match.matches_id}");

                                if (lobbyClosed && match.match_status_id == 1) // Si estaba 'Waiting'
                                {
                                    match.match_status_id = 3; 
                                    await context.SaveChangesAsync();
                                    Console.WriteLine($"LeaveLobby DB: MatchID={match.matches_id} status updated to Canceled.");
                                }
                            }
                            else { /* Log: No se encontró el participante a eliminar */ }
                        }
                        else { /* Log: No se encontró la partida o el jugador */ }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"!!! LeaveLobby DB ERROR for user {username}, lobby {lobbyCode}: {ex.ToString()}");
  
                    }
                } 
            }

            if (!lobbyClosed)
            {
                sendLobbyUpdateToAll(lobbyState); 
            }
            else
            {
                if (hostLeft && remainingPlayers != null)
                {
                    foreach (var playerUsername in remainingPlayers)
                    {
                        sendCallbackToUser(playerUsername, cb => cb.kickedFromLobby("Host left the lobby.")); // TODO: Lang Key
                    }
                }

                if (activeLobbies.TryRemove(lobbyCode, out _))
                {
                    Console.WriteLine($"Lobby {lobbyCode} removed from active lobbies.");
                }
            }
        }

        public void handleUserDisconnect(string username)
        {
            Console.WriteLine($"Handling disconnect for user: {username}");
            List<string> lobbiesToLeave = activeLobbies
                .Where(kvp => kvp.Value.players.Contains(username)) 
                .Select(kvp => kvp.Key) 
                .ToList(); 

            foreach (var lobbyCode in lobbiesToLeave)
            {
                Console.WriteLine($"User {username} was in lobby {lobbyCode}. Forcing leave due to disconnect.");
                leaveLobby(username, lobbyCode); 
            }
            userCallbacks.TryRemove(username, out _);
            Console.WriteLine($"Removed matchmaking callback for disconnected user: {username}");
        }
        public async void startGame(string hostUsername, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                Console.WriteLine($"StartGame Error: Lobby {lobbyCode} not found.");
                sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("Lobby not found."));
                return;
            }

            List<string> currentPlayers;

            lock (lobbyState)
            {
                if (lobbyState.hostUsername != hostUsername)
                {
                    Console.WriteLine($"StartGame Error: User {hostUsername} is not host of {lobbyCode}.");
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("You are not the host."));
                    return;
                }
             
                if (lobbyState.players.Count < 1) 
                {
                    Console.WriteLine($"StartGame Error: Not enough players in lobby {lobbyCode}.");
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("Not enough players to start."));
                    return;
                }

                Console.WriteLine($"Host {hostUsername} is starting game for lobby {lobbyCode}.");
                currentPlayers = lobbyState.players.ToList();
            } 
            bool dbUpdateSuccess = false;
            using (var context = new MindWeaveDBEntities1())
            {
                try
                {
                    var matchToStart = await context.Matches.FirstOrDefaultAsync(m => m.lobby_code == lobbyCode);
                    if (matchToStart != null && matchToStart.match_status_id == 1) // Si existe y está 'Waiting'
                    {
                        matchToStart.match_status_id = 2; // Cambiar a 'InProgress' (asumiendo ID 2)
                        matchToStart.start_time = DateTime.UtcNow; // Registrar hora de inicio
                        await context.SaveChangesAsync();
                        dbUpdateSuccess = true;
                        Console.WriteLine($"StartGame DB: MatchID={matchToStart.matches_id} status updated to InProgress.");
                    }
                    else { /* Log: Partida no encontrada o ya iniciada/terminada */ }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"!!! StartGame DB ERROR for lobby {lobbyCode}: {ex.ToString()}");
       
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("Database error starting the match."));
                    return; 
                }
            } 
            if (dbUpdateSuccess)
            {
                string matchId = lobbyCode; 
                foreach (var playerUsername in currentPlayers)
                {
                    sendCallbackToUser(playerUsername, cb => cb.matchFound(matchId, currentPlayers));
                }

                if (activeLobbies.TryRemove(lobbyCode, out _))
                {
                    Console.WriteLine($"Lobby {lobbyCode} removed from active lobbies as game started.");
                }
            }
        }

        public async void kickPlayer(string hostUsername, string playerToKickUsername, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState)) return;

            bool kickedFromMemory = false;
            lock (lobbyState)
            {
                if (lobbyState.hostUsername != hostUsername) { /* No es host */ return; }
                if (hostUsername.Equals(playerToKickUsername, StringComparison.OrdinalIgnoreCase)) { /* No puede expulsarse a sí mismo */ return; }

                if (lobbyState.players.Remove(playerToKickUsername))
                {
                    kickedFromMemory = true;
                    Console.WriteLine($"Player {playerToKickUsername} kicked from {lobbyCode} in memory by {hostUsername}.");
                }
            } 
            if (kickedFromMemory)
            {
                using (var context = new MindWeaveDBEntities1())
                {
                    try
                    {
                        var match = await context.Matches.FirstOrDefaultAsync(m => m.lobby_code == lobbyCode);
                        var playerKicked = await context.Player.FirstOrDefaultAsync(p => p.username == playerToKickUsername);

                        if (match != null && playerKicked != null)
                        {
                            var participantToRemove = await context.MatchParticipants
                                                                .FirstOrDefaultAsync(mp => mp.match_id == match.matches_id && mp.player_id == playerKicked.idPlayer);
                            if (participantToRemove != null)
                            {
                                context.MatchParticipants.Remove(participantToRemove);
                                await context.SaveChangesAsync();
                                Console.WriteLine($"KickPlayer DB: Removed participant PlayerID={playerKicked.idPlayer} from MatchID={match.matches_id}");
                            }
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"!!! KickPlayer DB ERROR: {ex.ToString()}"); }
                } 
                sendCallbackToUser(playerToKickUsername, cb => cb.kickedFromLobby("Kicked by host.")); // TODO: Lang Key
                sendLobbyUpdateToAll(lobbyState);
            }
        }

        public void inviteToLobby(string inviterUsername, string invitedUsername, string lobbyCode)
        {
            // 1. Validar lobby
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] Invite Error: Lobby {lobbyCode} not found.");
                sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed($"Lobby {lobbyCode} no longer exists.")); // Notificar al invitador
                return;
            }

            // *** CAMBIO: Validar si el invitado está ONLINE usando SocialManagerService ***
            if (!SocialManagerService.ConnectedUsers.ContainsKey(invitedUsername)) // Accede al diccionario estático
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] Invite Error: User {invitedUsername} is not online.");
                // *** CAMBIO: Usar mensaje de Lang ***
                sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed($"{invitedUsername} {Lang.ErrorUserNotOnline}")); // Notificar al invitador
                return;
            }

            // 3. Validaciones adicionales (lobby lleno, ya está dentro)
            lock (lobbyState)
            {
                if (lobbyState.players.Count >= MAX_PLAYERS_PER_LOBBY)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] Invite Error: Lobby {lobbyCode} is full.");
                    sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed($"Lobby {lobbyCode} is full."));
                    return;
                }

                if (lobbyState.players.Contains(invitedUsername))
                {
                    Console.WriteLine($"[{DateTime.UtcNow:O}] Invite Info: User {invitedUsername} is already in lobby {lobbyCode}.");
                    sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed($"{invitedUsername} is already in the lobby."));
                    return;
                }
            } // Fin lock

            // *** CAMBIO: Enviar invitación usando el helper estático de SocialManagerService ***
            Console.WriteLine($"[{DateTime.UtcNow:O}] Sending lobby invite notification via Social Service to {invitedUsername} for lobby {lobbyCode} from {inviterUsername}.");
            SocialManagerService.sendNotificationToUser(invitedUsername, cb => cb.notifyLobbyInvite(inviterUsername, lobbyCode));

            // Opcional: Notificar al invitador que se envió (podrías usar lobbyCreationFailed con un mensaje de éxito)
            // sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed($"Invitation sent to {invitedUsername}.")); // Un poco hacky
        }


        private void sendLobbyUpdateToAll(LobbyStateDto lobbyState)
        {
            if (lobbyState == null) return;
            List<string> currentPlayersSnapshot;
            lock (lobbyState) 
            {
                currentPlayersSnapshot = lobbyState.players.ToList();
            }
            Console.WriteLine($"Sending lobby update for {lobbyState.lobbyId} to: {string.Join(", ", currentPlayersSnapshot)}");
            foreach (var username in currentPlayersSnapshot)
            {
                sendCallbackToUser(username, cb => cb.updateLobbyState(lobbyState));
            }
        }

        public async void changeDifficulty(string hostUsername, string lobbyId, int newDifficultyId)
        {
            if (!activeLobbies.TryGetValue(lobbyId, out LobbyStateDto lobbyState))
            {
                Console.WriteLine($"ChangeDifficulty Error: Lobby {lobbyId} not found.");
                return;
            }

            bool changed = false;
            LobbyStateDto updatedState = null;

            lock (lobbyState)
            {
                if (lobbyState.hostUsername != hostUsername)
                {
                    Console.WriteLine($"ChangeDifficulty Error: User {hostUsername} is not host of {lobbyId}.");
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("You are not the host."));
                    return;
                }

                if (newDifficultyId < 1 || newDifficultyId > 3)
                {
                    Console.WriteLine($"ChangeDifficulty Error: Invalid difficulty ID {newDifficultyId}.");
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("Invalid difficulty selected."));
                    return;
                }

                if (lobbyState.currentSettingsDto.difficultyId != newDifficultyId)
                {
                    lobbyState.currentSettingsDto.difficultyId = newDifficultyId;
                    changed = true;
                    updatedState = lobbyState; 
                    Console.WriteLine($"Difficulty for lobby {lobbyId} changed to {newDifficultyId} in memory by host {hostUsername}.");
                }
                else
                {
                    Console.WriteLine($"ChangeDifficulty Info: Difficulty for lobby {lobbyId} is already {newDifficultyId}.");
                    return; 
                }
            } // Fin lock

            if (changed && updatedState != null)
            {
              
                Task.Run(async () => { 
                    using (var context = new MindWeaveDBEntities1())
                    {
                        try
                        {
                            var matchToUpdate = await context.Matches.FirstOrDefaultAsync(m => m.lobby_code == lobbyId);
                            if (matchToUpdate != null)
                            {
                                matchToUpdate.difficulty_id = newDifficultyId;
                                await context.SaveChangesAsync();
                                Console.WriteLine($"ChangeDifficulty DB: Difficulty for MatchID={matchToUpdate.matches_id} updated to {newDifficultyId}.");
                            }
                            else { /* Log: Partida no encontrada en BD */ }
                        }
                        catch (Exception ex) { Console.WriteLine($"!!! ChangeDifficulty DB ERROR for lobby {lobbyId}: {ex.ToString()}"); }
                    }
                }); 
                sendLobbyUpdateToAll(updatedState);
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
                        Console.WriteLine($"Callback channel for {username} is not open (State: {commObject?.State}). Removing.");

                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending callback to {username}: {ex.Message}. Channel will be removed on Fault/Close.");

                }
            }
            else
            {
                Console.WriteLine($"Callback channel not found for user: {username}.");
            }
        }


    }
}