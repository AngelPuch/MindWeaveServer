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
        private const int MAX_PLAYERS_PER_LOBBY = 4; // Límite de jugadores

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

            // **PUNTO DE REVISIÓN 1: Contexto de Base de Datos**
            // ¿Es seguro crear un nuevo DbContext aquí? Si el servicio WCF es Singleton,
            // podría ser mejor inyectar una *factoría* de DbContext o usar un contexto por operación.
            // Por ahora, asumimos que está bien, pero es algo a considerar.
            using (var context = new MindWeaveDBEntities1())
            {
                // **PUNTO DE REVISIÓN 2: Obtener Host Player**
                // Correcto: Busca al jugador por nombre de usuario.
                var hostPlayer = await context.Player.AsNoTracking().FirstOrDefaultAsync(p => p.username == hostUsername);
                if (hostPlayer == null)
                {
                    return new LobbyCreationResultDto { success = false, message = "Host player not found." }; // TODO: Lang
                }
                Console.WriteLine($"Host player found: {hostPlayer.username} (ID: {hostPlayer.idPlayer})"); // Log añadido

                // --- Intento de Generación y Guardado en BD ---
                while (newMatch == null && attempts < MAX_LOBBY_CODE_GENERATION_ATTEMPTS)    
                {
                    attempts++;
                    currentLobbyCode = LobbyCodeGenerator.generateUniqueCode();

                    if (activeLobbies.ContainsKey(currentLobbyCode))
                    {
                        Console.WriteLine($"Lobby code {currentLobbyCode} already exists in memory. Regenerating...");
                        continue;
                    }

                    // **PUNTO DE REVISIÓN 3: Creación de la Entidad 'Matches'**
                    // Verifica que TODAS las columnas NOT NULL y FK tengan valores válidos.
                    newMatch = new Matches
                    {
                        creation_time = DateTime.UtcNow,
                        // ¿Existe MatchStatus con status_id = 1 en tu BD?
                        match_status_id = 1,
                        // ¿Existe Puzzle con puzzle_id = 1 (o el valor de settings.preloadedPuzzleId)?
                        puzzle_id = settings.preloadedPuzzleId ?? 3,
                        // ¿Existe DifficultyLevel con difficulty_id = 1 (o el valor de settings.difficultyId)?
                        difficulty_id = settings.difficultyId > 0 ? settings.difficultyId : 1,
                        lobby_code = currentLobbyCode,
                        // ¿Existe la columna 'host_player_id' en la BD y en el modelo EDMX? ¿Es NOT NULL?
                        host_player_id = hostPlayer.idPlayer
                        // ¿Hay alguna otra columna NOT NULL en 'Matches' que falte aquí?
                    };

                    // **Log ANTES de guardar:**
                    Console.WriteLine($"Attempting to save Match: Code={newMatch.lobby_code}, HostID={newMatch.host_player_id}, StatusID={newMatch.match_status_id}, PuzzleID={newMatch.puzzle_id}, DifficultyID={newMatch.difficulty_id}");

                    context.Matches.Add(newMatch);

                    try
                    {
                        // **PUNTO DE REVISIÓN 4: SaveChangesAsync()**
                        // Aquí es donde ocurre el DbUpdateException.
                        await context.SaveChangesAsync();
                        Console.WriteLine($"Match record created in DB with lobby_code: {currentLobbyCode}, ID: {newMatch.matches_id}");
                    }
                    // **PUNTO DE REVISIÓN 5: Manejo de Excepciones**
                    // Este bloque intenta capturar el error específico.
                    catch (DbUpdateException dbEx)
                    {
                        string innerError = dbEx.InnerException?.InnerException?.Message ?? dbEx.InnerException?.Message ?? dbEx.Message;
                        Console.WriteLine($"!!! DbUpdateException saving match: {innerError}");
                        Console.WriteLine($"--- Full DbUpdateException Trace: {dbEx.ToString()}");

                        // Manejo específico para violación UNIQUE de lobby_code
                        if (innerError.Contains("UNIQUE KEY constraint") && (innerError.Contains("lobby_code") || innerError.Contains("UQ__Matches"))) // Ajusta UQ__Matches si tu constraint tiene otro nombre
                        {
                            Console.WriteLine($"Lobby code {currentLobbyCode} collision detected in database. Regenerating attempt {attempts}...");
                            if (newMatch != null) { context.Entry(newMatch).State = EntityState.Detached; }
                            newMatch = null; // Fuerza reintento
                        }
                        else // Otro error de BD (FK, NOT NULL, etc.)
                        {
                            // Retorna el mensaje de error interno, es más útil para depurar
                            return new LobbyCreationResultDto { success = false, message = $"Database error: {innerError}", lobbyCode = null, initialLobbyState = null };
                        }
                    }
                    catch (Exception ex) // Otros errores
                    {
                        Console.WriteLine($"!!! Generic Error saving match: {ex.ToString()}");
                        return new LobbyCreationResultDto { success = false, message = "Failed to save lobby data.", lobbyCode = null, initialLobbyState = null }; // TODO: Lang
                    }
                } // Fin while

                if (newMatch == null)
                {
                    Console.WriteLine("Failed to generate a unique lobby code after multiple attempts.");
                    return new LobbyCreationResultDto { success = false, message = "Failed to generate unique lobby code.", lobbyCode = null, initialLobbyState = null }; // TODO: Lang
                }

                // --- Añadir a Memoria (Sin cambios aparentes aquí) ---
                initialState = new LobbyStateDto { /* ... */ };
                activeLobbies.AddOrUpdate(newMatch.lobby_code, initialState, (key, existing) => initialState);
                addedToMemory = true;
                Console.WriteLine($"Lobby {newMatch.lobby_code} added to active lobbies memory.");

            } // Fin using context

            if (addedToMemory && initialState != null)
            {
                return new LobbyCreationResultDto { /* ... success ... */ };
            }
            else
            {
                return new LobbyCreationResultDto { success = false, message = "Failed to add lobby to memory.", lobbyCode = null, initialLobbyState = null }; // TODO: Lang
            }
        }


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