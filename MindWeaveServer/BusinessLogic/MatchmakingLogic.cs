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
                    difficulty_id = settings.difficultyId > 0 ? settings.difficultyId : 1, // Default a 1 si es 0 o menos
                    lobby_code = currentLobbyCode,
                    host_player_id = hostPlayer.idPlayer
                };

                Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: Match entity prepared for DB. Code={newMatch.lobby_code}, HostID={newMatch.host_player_id}, StatusID={newMatch.match_status_id}, PuzzleID={newMatch.puzzle_id}, DifficultyID={newMatch.difficulty_id}");
                context.Matches.Add(newMatch);

                try
                {
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: BEFORE SaveChangesAsync for {currentLobbyCode}");
                    await context.SaveChangesAsync();
                    // Si llegamos aquí, ¡se guardó!
                    Console.WriteLine($"{DateTime.UtcNow:O} --- Logic: AFTER SaveChangesAsync for {currentLobbyCode}. Match ID: {newMatch.matches_id}");
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
                        return new LobbyCreationResultDto { success = false, message = $"Database error: {errorDetails}", lobbyCode = null, initialLobbyState = null }; // Mensaje específico
                    }
                }
                catch (Exception ex) // Otros errores durante el guardado
                {
                    Console.WriteLine($"{DateTime.UtcNow:O} !!! Logic: Generic Exception saving match {currentLobbyCode}: {ex.ToString()}");
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


    public void joinLobby(string username, string lobbyCode)
        {
            // Intenta encontrar el lobby en el diccionario de lobbies activos.
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                Console.WriteLine($"JoinLobby Error: Lobby {lobbyCode} not found for user {username}.");
                sendCallbackToUser(username, cb => cb.lobbyCreationFailed($"Lobby {lobbyCode} not found or is inactive.")); // TODO: Lang Key
                return;
            }

            bool added = false; // Bandera para saber si el jugador fue añadido.
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
                    added = true;
                    Console.WriteLine($"User {username} joined lobby {lobbyCode}. Players: {string.Join(", ", lobbyState.players)}");
                }
            } // Fin lock

            // Enviar actualización a todos fuera del lock
            sendLobbyUpdateToAll(lobbyState);
        }

        public void leaveLobby(string username, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                Console.WriteLine($"LeaveLobby Error: Lobby {lobbyCode} not found for user {username}.");
                return;
            }

            bool hostLeft = false;
            bool lobbyClosed = false;
            List<string> remainingPlayers = null; // Para notificar si host se va

            lock (lobbyState)
            {
                if (!lobbyState.players.Remove(username))
                {
                    Console.WriteLine($"LeaveLobby Info: User {username} was not in lobby {lobbyCode}.");
                    return; // No estaba en el lobby
                }

                Console.WriteLine($"User {username} left lobby {lobbyCode}. Remaining: {string.Join(", ", lobbyState.players)}");

                if (username == lobbyState.hostUsername)
                {
                    hostLeft = true;
                    lobbyClosed = true;
                    remainingPlayers = lobbyState.players.ToList(); // Copia ANTES de limpiar
                    Console.WriteLine($"Host {username} left lobby {lobbyCode}. Lobby closing.");
                }
                else if (lobbyState.players.Count == 0)
                {
                    lobbyClosed = true;
                    Console.WriteLine($"Last player {username} left lobby {lobbyCode}. Lobby closing.");
                }
            } // Fin lock

            if (!lobbyClosed)
            {
                sendLobbyUpdateToAll(lobbyState);
            }
            else
            {
                if (hostLeft && remainingPlayers != null)
                {
                    foreach (var player in remainingPlayers)
                    {
                        sendCallbackToUser(player, cb => cb.kickedFromLobby("Host left the lobby.")); // TODO: Lang Key
                    }
                }

                if (activeLobbies.TryRemove(lobbyCode, out _))
                {
                    Console.WriteLine($"Lobby {lobbyCode} removed from active lobbies.");
                    // TODO: Actualizar estado en DB a 'Canceled'?
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

        public void startGame(string hostUsername, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState))
            {
                Console.WriteLine($"StartGame Error: Lobby {lobbyCode} not found.");
                sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("Lobby not found.")); // TODO: Lang
                return;
            }

            List<string> currentPlayers; // Para enviar en el callback
            lock (lobbyState)
            {
                if (lobbyState.hostUsername != hostUsername)
                {
                    Console.WriteLine($"StartGame Error: User {hostUsername} is not host of {lobbyCode}.");
                    sendCallbackToUser(hostUsername, cb => cb.lobbyCreationFailed("You are not the host.")); // TODO: Lang
                    return;
                }
                // if (lobbyState.players.Count < MIN_PLAYERS) { ... } // Validación opcional

                Console.WriteLine($"Host {hostUsername} is starting game for lobby {lobbyCode}.");
                // TODO: Cambiar estado del Match en la BD a 'InProgress'
                // using (var context = new MindWeaveDBEntities1()) { ... context.SaveChangesAsync(); ... }
                currentPlayers = lobbyState.players.ToList(); // Copiar lista dentro del lock
            } // Fin lock

            // Notificar a todos fuera del lock
            string matchId = lobbyCode; // Usando lobbyCode como Id de partida temporalmente
            foreach (var player in currentPlayers)
            {
                sendCallbackToUser(player, cb => cb.matchFound(matchId, currentPlayers));
            }

            // Remover el lobby de la lista activa
            if (activeLobbies.TryRemove(lobbyCode, out _))
            {
                Console.WriteLine($"Lobby {lobbyCode} removed as game started.");
            }
        }

        public void kickPlayer(string hostUsername, string playerToKickUsername, string lobbyCode)
        {
            if (!activeLobbies.TryGetValue(lobbyCode, out LobbyStateDto lobbyState)) return;

            bool kicked = false;
            lock (lobbyState)
            {
                if (lobbyState.hostUsername != hostUsername) { /* ... log error, return ... */ return; }
                if (hostUsername == playerToKickUsername) { /* ... log error, return ... */ return; }

                if (lobbyState.players.Remove(playerToKickUsername)) // Intentar remover
                {
                    kicked = true;
                    Console.WriteLine($"Player {playerToKickUsername} kicked from {lobbyCode} by {hostUsername}.");
                }
            } // Fin lock

            if (kicked)
            {
                sendCallbackToUser(playerToKickUsername, cb => cb.kickedFromLobby("Kicked by host.")); // TODO: Lang Key
                sendLobbyUpdateToAll(lobbyState); // Notificar a los restantes
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