// MindWeaveServer/BusinessLogic/MatchmakingLogic.cs
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts; // Para IMatchmakingCallback
using MindWeaveServer.DataAccess;
using MindWeaveServer.Utilities; // Para LobbyCodeGenerator
using System;
using System.Collections.Concurrent; // Para ConcurrentDictionary
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure; // Para DbUpdateException
using System.Linq;
using System.ServiceModel; // Para CommunicationState
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

        // *** CAMBIO: El DbContext AHORA es local a esta llamada, seguro para PerSession/PerCall ***
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

            // --- Intento de Generación y Guardado en BD ---
            while (newMatch == null && attempts < MAX_LOBBY_CODE_GENERATION_ATTEMPTS)
            {
                attempts++;
                currentLobbyCode = LobbyCodeGenerator.generateUniqueCode();
                Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Attempt {attempts}, generated code: {currentLobbyCode}");

                // Verifica si ya existe en memoria (rápido)
                if (activeLobbies.ContainsKey(currentLobbyCode))
                {
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Code {currentLobbyCode} exists in memory. Retrying...");
                    continue;
                }

                // Verifica si ya existe en BD (más lento, pero necesario por si el server reinició)
                // NOTA: Podrías quitar esta verificación si confías en el manejo de UNIQUE constraint
                bool codeExistsInDb = await context.Matches.AnyAsync(m => m.lobby_code == currentLobbyCode);
                if (codeExistsInDb)
                {
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Code {currentLobbyCode} exists in DB. Retrying...");
                    continue; // Intenta generar otro código
                }

                // Crea la entidad Match
                newMatch = new Matches
                {
                    creation_time = DateTime.UtcNow,
                    match_status_id = 1, // Asume 1 = 'Waiting'/'Pending'
                    puzzle_id = settings.preloadedPuzzleId ?? 3, // Default a 3 si es null
                    difficulty_id = 1, // Default a 1 si es 0 o menos
                    lobby_code = currentLobbyCode
                };

                Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Match entity prepared for DB. Code={newMatch.lobby_code}, StatusID={newMatch.match_status_id}, PuzzleID={newMatch.puzzle_id}, DifficultyID={newMatch.difficulty_id}");
                context.Matches.Add(newMatch);

                try
                {
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: BEFORE SaveChangesAsync for {currentLobbyCode}");
                    await context.SaveChangesAsync();
                    // Si llegamos aquí, ¡se guardó!
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: AFTER SaveChangesAsync for {currentLobbyCode}. Match ID: {newMatch.matches_id}");
                    // *** NUEVO: Crear y guardar MatchParticipants para el host ***
                    var hostParticipant = new MatchParticipants
                    {
                        match_id = newMatch.matches_id, // Usar el ID de la partida recién creada
                        player_id = hostPlayer.idPlayer,
                        is_host = true // Marcar como host
                        // score, pieces_placed, final_rank son nullables, no se asignan al crear
                    };
                    context.MatchParticipants.Add(hostParticipant);
                    await context.SaveChangesAsync(); // Guardar el participante host
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Host participant saved for Match ID: {newMatch.matches_id}, Player ID: {hostPlayer.idPlayer}");
                    // Si llegamos aquí todo se guardo
                    }
                    catch (DbUpdateException dbEx) // Error al guardar (FK, UNIQUE, etc.)
                    {
                    string errorDetails = dbEx.InnerException?.InnerException?.Message ?? dbEx.InnerException?.Message ?? dbEx.Message;
                    Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: DbUpdateException saving match {currentLobbyCode}: {errorDetails}");
                    Console.WriteLine($"--- Full DbUpdateException Trace: {dbEx.ToString()}"); // Log completo

                    // Si es error de UNIQUE en lobby_code, reintenta
                    if (errorDetails.Contains("UNIQUE KEY constraint") && (errorDetails.Contains("lobby_code") || errorDetails.Contains("UQ__Matches")))
                    {
                        Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Lobby code collision in DB. Detaching entity and retrying...");
                        if (context.Entry(newMatch).State != EntityState.Detached) { context.Entry(newMatch).State = EntityState.Detached; }
                        newMatch = null; // Fuerza el reintento del while
                    }
                    else // Otro error de BD, no reintentable
                    {
                        if (newMatch != null && context.Entry(newMatch).State != EntityState.Detached) { context.Entry(newMatch).State = EntityState.Detached; } 
                        return new LobbyCreationResultDto { success = false, message = $"Database error: {errorDetails}", lobbyCode = null, initialLobbyState = null }; // Mensaje específico
                    }
                }
                catch (Exception ex) // Otros errores durante el guardado
                {
                    Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: Generic Exception saving match {currentLobbyCode}: {ex.ToString()}");
                    if (newMatch != null && context.Entry(newMatch).State != EntityState.Detached) { context.Entry(newMatch).State = EntityState.Detached; }
                        return new LobbyCreationResultDto { success = false, message = $"Failed to save lobby data: {ex.Message}", lobbyCode = null, initialLobbyState = null }; // Mensaje simple
                }
            } // Fin while

            // Si salimos del while porque se agotaron los intentos
            if (newMatch == null)
            {
                Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: Failed to generate and save a unique lobby code after {attempts} attempts.");
                return new LobbyCreationResultDto { success = false, message = "Failed to generate a unique lobby code.", lobbyCode = null, initialLobbyState = null }; // TODO: Lang
            }

            // --- Guardado en BD exitoso, ahora añadir a memoria ---
            Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Creating initial LobbyStateDto for {currentLobbyCode}...");
            initialState = new LobbyStateDto
            {
                lobbyId = newMatch.lobby_code,
                hostUsername = hostUsername,
                players = new List<string> { hostUsername }, // Inicia solo con el host
                currentSettingsDto = settings // Guarda las settings iniciales
            };

            Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Adding lobby {currentLobbyCode} to activeLobbies dictionary...");
            // Intenta añadir al diccionario concurrente
            if (activeLobbies.TryAdd(newMatch.lobby_code, initialState))
            {
                addedToMemory = true;
                Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Lobby {currentLobbyCode} successfully added to memory.");
            }
            else
            {
                // Esto sería muy raro si la verificación inicial funcionó, pero es una salvaguarda
                Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: Failed to add lobby {currentLobbyCode} to memory dictionary (race condition?).");
                // Considera eliminar el registro de la BD si falla aquí? O marcarlo como inválido?
                // Por ahora, retornamos error.
                return new LobbyCreationResultDto { success = false, message = "Failed to register lobby in memory after DB save.", lobbyCode = null, initialLobbyState = null };
            }

        } // Fin using context

        // --- Retorno final ---
        if (addedToMemory && initialState != null) // Debería cumplirse si TryAdd tuvo éxito
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
            // Este caso ahora es menos probable, pero se mantiene por seguridad
            Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: Returning FAILURE (unexpected state) after DB save for {currentLobbyCode}.");
            return new LobbyCreationResultDto { success = false, message = "Unexpected error after saving lobby.", lobbyCode = null, initialLobbyState = null };
        }
    } // Fin


    public async void joinLobby(string username, string lobbyCode)
        {
            // Intenta encontrar el lobby en el diccionario de lobbies activos.
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                Console.WriteLine($"JoinLobby Error: Lobby {lobbyCode} not found for user {username}.");
                sendCallbackToUser(username, cb => cb.lobbyCreationFailed($"Lobby {lobbyCode} not found or is inactive.")); // TODO: Lang Key
                return;
            }

            bool addedToMemory = false;
            int matchId = -1; // Para buscar la partida en la BD
            int playerId = -1; // Para el nuevo participante
                               // .
            lock (lobbyState) // Bloquea el estado de ESTE lobby para modificarlo de forma segura.
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
                    // No se añade, pero se reenviará el estado actual fuera del lock.a
                }
                else
                {
                    lobbyState.players.Add(username);
                    addedToMemory = true;
                    Console.WriteLine($"User {username} joined lobby {lobbyCode}. Players: {string.Join(", ", lobbyState.players)}");
                }
            } // Fin lock

            // --- Lógica de Base de Datos (fuera del lock de memoria) ---
            if (addedToMemory) // Solo si se añadió a la memoria, intentamos añadirlo a la BD
            {
                using (var context = new MindWeaveDBEntities1())
                {
                    try
                    {
                        // Buscar el ID de la partida (Match) usando el lobbyCode
                        var match = await context.Matches
                                                .AsNoTracking() // No necesitamos rastrear la partida
                                                .FirstOrDefaultAsync(m => m.lobby_code == lobbyCode);

                        // Buscar el ID del jugador
                        var player = await context.Player
                                                .AsNoTracking() // No necesitamos rastrear al jugador
                                                .FirstOrDefaultAsync(p => p.username == username);

                        if (match != null && player != null)
                        {
                            matchId = match.matches_id;
                            playerId = player.idPlayer;

                            // Verificar si ya existe por alguna razón (doble join rápido?)
                            bool alreadyExists = await context.MatchParticipants
                                                            .AnyAsync(mp => mp.match_id == matchId && mp.player_id == playerId);

                            if (!alreadyExists)
                            {
                                // Crear el nuevo participante (NO es host)
                                var newParticipant = new MatchParticipants
                                {
                                    match_id = matchId,
                                    player_id = playerId,
                                    is_host = false // El que se une nunca es el host
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
                        // ¿Qué hacemos si falla la BD? ¿Quitarlo de la memoria? ¿Notificar?
                        // Por ahora, solo logueamos. El estado en memoria ya lo incluye.
                    }
                } // Fin using context
            } // Fin if(addedToMemory)

            // Enviar actualización a todos fuera del lock
            sendLobbyUpdateToAll(lobbyState);
        }

        // Método leaveLobby: Ahora también debe eliminar al participante de MatchParticipants
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
                    return; // No estaba en el lobby
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
            } // Fin lock

            // --- Lógica de Base de Datos (fuera del lock) ---
            if (removedFromMemory)
            {
                using (var context = new MindWeaveDBEntities1())
                {
                    try
                    {
                        // Buscamos la partida y el jugador para obtener IDs
                        var match = await context.Matches.FirstOrDefaultAsync(m => m.lobby_code == lobbyCode);
                        var player = await context.Player.FirstOrDefaultAsync(p => p.username == username);

                        if (match != null && player != null)
                        {
                            // Buscamos la entrada del participante
                            var participantToRemove = await context.MatchParticipants
                                                                .FirstOrDefaultAsync(mp => mp.match_id == match.matches_id && mp.player_id == player.idPlayer);

                            if (participantToRemove != null)
                            {
                                context.MatchParticipants.Remove(participantToRemove);
                                await context.SaveChangesAsync();
                                Console.WriteLine($"LeaveLobby DB: Removed participant PlayerID={player.idPlayer} from MatchID={match.matches_id}");

                                // Si el lobby se cierra (host se fue o último jugador), actualizamos estado de la partida
                                if (lobbyClosed && match.match_status_id == 1) // Si estaba 'Waiting'
                                {
                                    match.match_status_id = 3; // Cambiar a 'Canceled' (asumiendo ID 3)
                                                               // Opcional: Poner end_time?
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
                        // Loguear el error, la salida de memoria ya ocurrió.
                    }
                } // Fin using context
            } // Fin if(removedFromMemory)

            // --- Lógica de Callbacks y Limpieza de Memoria ---
            if (!lobbyClosed)
            {
                sendLobbyUpdateToAll(lobbyState); // Notificar a los restantes
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
                .Where(kvp => kvp.Value.players.Contains(username)) // Encontrar lobbies donde estaba el usuario
                .Select(kvp => kvp.Key) // Obtener los códigos de lobby
                .ToList(); // Materializar la lista antes de iterar

            foreach (var lobbyCode in lobbiesToLeave)
            {
                Console.WriteLine($"User {username} was in lobby {lobbyCode}. Forcing leave due to disconnect.");
                leaveLobby(username, lobbyCode); // Llama a la lógica de salida normal
            }
        }

        // Método startGame: Ya no necesita buscar el host_player_id en Matches.
        // La validación de si el que inicia es el host se hace con lobbyState.hostUsername.
        // Podríamos añadir una consulta a MatchParticipants para doble verificación si fuera necesario.
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
                // Validación principal: ¿El usuario que llama es el host en memoria?
                if (lobbyState.hostUsername != hostUsername)
                {
                    Console.WriteLine($"StartGame Error: User {hostUsername} is not host of {lobbyCode}.");
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("You are not the host."));
                    return;
                }
                // Opcional: Validación mínima de jugadores
                if (lobbyState.players.Count < 1) // O el mínimo que definas
                {
                    Console.WriteLine($"StartGame Error: Not enough players in lobby {lobbyCode}.");
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("Not enough players to start."));
                    return;
                }

                Console.WriteLine($"Host {hostUsername} is starting game for lobby {lobbyCode}.");
                currentPlayers = lobbyState.players.ToList();
            } // Fin lock

            // --- Lógica de Base de Datos (fuera del lock) ---
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
                    // Notificar al host del error de BD?
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("Database error starting the match."));
                    return; // No continuar si falla la BD
                }
            } // Fin using context

            // --- Lógica de Callbacks y Limpieza de Memoria (solo si la BD se actualizó) ---
            if (dbUpdateSuccess)
            {
                string matchId = lobbyCode; // Usando lobbyCode como Id temporalmente
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

        // Método kickPlayer: Ya no necesita host_player_id. Usa lobbyState.hostUsername.
        // Debe eliminar al jugador de MatchParticipants.
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
            } // Fin lock

            // --- Lógica de Base de Datos y Callbacks (si se eliminó de memoria) ---
            if (kickedFromMemory)
            {
                // Eliminar de BD (similar a leaveLobby)
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
                } // Fin using context

                // Notificar al jugador expulsado
                sendCallbackToUser(playerToKickUsername, cb => cb.kickedFromLobby("Kicked by host.")); // TODO: Lang Key
                // Notificar a los restantes
                sendLobbyUpdateToAll(lobbyState);
            }
        }

        public void inviteToLobby(string inviterUsername, string invitedUsername, string lobbyCode)
        {
            if (!activeLobbies.ContainsKey(lobbyCode))
            {
                Console.WriteLine($"Invite Error: Lobby {lobbyCode} does not exist.");
                // Podríamos enviar un mensaje al invitador
                sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed($"Lobby {lobbyCode} no longer exists.")); // Reutilizamos callback
                return;
            }
            // Verificar si el invitado está online (tiene callback registrado)
            if (!userCallbacks.ContainsKey(invitedUsername))
            {
                Console.WriteLine($"Invite Error: User {invitedUsername} is not online/connected.");
                sendCallbackToUser(inviterUsername, cb => cb.lobbyCreationFailed($"{invitedUsername} is not online.")); // Reutilizamos callback
                return;
            }

            Console.WriteLine($"Sending invite to {invitedUsername} for lobby {lobbyCode} from {inviterUsername}.");
            sendCallbackToUser(invitedUsername, cb => cb.receiveLobbyInvite(inviterUsername, lobbyCode));
        }


        // --- Métodos Auxiliares ---

        private void sendLobbyUpdateToAll(LobbyStateDto lobbyState)
        {
            if (lobbyState == null) return;
            List<string> currentPlayers = lobbyState.players.ToList(); // Copia segura fuera de lock
            Console.WriteLine($"Sending lobby update for {lobbyState.lobbyId} to: {string.Join(", ", currentPlayers)}");
            foreach (var username in currentPlayers)
            {
                sendCallbackToUser(username, cb => cb.updateLobbyState(lobbyState));
            }
        }

        // Método changeDifficulty: La validación del host se hace con lobbyState.hostUsername.
        // El resto sigue igual, actualiza el ID en la BD.
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
                // Validación principal: ¿Es el host?
                if (lobbyState.hostUsername != hostUsername)
                {
                    Console.WriteLine($"ChangeDifficulty Error: User {hostUsername} is not host of {lobbyId}.");
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("You are not the host."));
                    return;
                }

                // Validación simple de ID (1, 2, 3)
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
                    updatedState = lobbyState; // Guardar referencia para enviar fuera del lock
                    Console.WriteLine($"Difficulty for lobby {lobbyId} changed to {newDifficultyId} in memory by host {hostUsername}.");
                }
                else
                {
                    Console.WriteLine($"ChangeDifficulty Info: Difficulty for lobby {lobbyId} is already {newDifficultyId}.");
                    return; // No cambió, no hacer nada más
                }
            } // Fin lock

            if (changed && updatedState != null)
            {
                // --- Lógica de Base de Datos (fuera del lock) ---
                Task.Run(async () => { // Ejecutar en segundo plano
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
                }); // Fin Task.Run

                // Notificar a todos los jugadores en el lobby (fuera del lock)
                sendLobbyUpdateToAll(updatedState);
            }
        }

        public void sendCallbackToUser(string username, Action<IMatchmakingCallback> callbackAction) // Hice público para reusar en Service
        {
            if (userCallbacks.TryGetValue(username, out IMatchmakingCallback callbackChannel))
            {
                try
                {
                    ICommunicationObject commObject = callbackChannel as ICommunicationObject;
                    if (commObject != null && commObject.State == CommunicationState.Opened)
                    {
                        callbackAction(callbackChannel);
                        // Console.WriteLine($"Callback sent successfully to {username}."); // Log (opcional, puede ser ruidoso)
                    }
                    else
                    {
                        Console.WriteLine($"Callback channel for {username} is not open (State: {commObject?.State}). Removing.");
                        // Llamar a removeCallbackChannel del SERVICIO para que maneje la limpieza completa
                        // (No tenemos referencia directa al servicio aquí, así que el servicio debe llamarlo)
                        // Esto se maneja ahora con los eventos Faulted/Closed en el servicio.
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending callback to {username}: {ex.Message}. Channel will be removed on Fault/Close.");
                    // No removemos el canal aquí directamente, esperamos al evento Faulted/Closed
                }
            }
            else
            {
                Console.WriteLine($"Callback channel not found for user: {username}.");
            }
        }


    }
}