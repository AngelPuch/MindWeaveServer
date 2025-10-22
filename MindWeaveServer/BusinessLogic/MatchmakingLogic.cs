// MindWeaveServer/BusinessLogic/MatchmakingLogic.cs
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.DataAccess; // Para acceso a DBContext y Player
using MindWeaveServer.Utilities; // Para LobbyCodeGenerator
using System;
using System.Collections.Generic;
using System.Data.Entity; // Para FirstOrDefaultAsync
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class MatchmakingLogic
    {
        // Referencia al diccionario de lobbies activos (pasado desde el servicio)
        private readonly Dictionary<string, LobbyStateDto> activeLobbies;

        // TODO: Inyectar repositorios si los usas (IPlayerRepository, IMatchRepository)
        // public MatchmakingLogic(Dictionary<string, LobbyStateDto> lobbies, IPlayerRepository playerRepo, IMatchRepository matchRepo)
        public MatchmakingLogic(Dictionary<string, LobbyStateDto> lobbies)
        {
            this.activeLobbies = lobbies;
            // this.playerRepository = playerRepo;
            // this.matchRepository = matchRepo;
        }

        public async Task<LobbyCreationResultDto> createLobbyAsync(string hostUsername, LobbySettingsDto settings)
        {
            using (var context = new MindWeaveDBEntities1()) // Usamos el contexto directamente por ahora
            {
                // 1. Buscar al jugador host
                var hostPlayer = await context.Player
                                        .AsNoTracking() // No necesitamos rastrear cambios aquí
                                        .FirstOrDefaultAsync(p => p.username == hostUsername);

                if (hostPlayer == null)
                {
                    return new LobbyCreationResultDto { success = false, message = "Host player not found." }; // TODO: Lang
                }

                // 2. Generar un código único (simplificado)
                string newLobbyCode;
                lock (activeLobbies) // Bloquea el diccionario mientras generamos/verificamos
                {
                    // Intenta generar hasta encontrar uno no existente (baja probabilidad de colisión con 6 chars)
                    int attempts = 0;
                    const int maxAttempts = 10;
                    do
                    {
                        newLobbyCode = LobbyCodeGenerator.generateUniqueCode();
                        attempts++;
                        if (attempts > maxAttempts)
                        {
                            // Muy improbable, pero por si acaso
                            throw new Exception("Failed to generate a unique lobby code after multiple attempts.");
                        }
                    } while (activeLobbies.ContainsKey(newLobbyCode)); // Verifica si ya existe en memoria
                    // NOTA: Aún falta verificar en la BD si el código ya fue usado y la partida terminó.
                    //       Para una mejor robustez, el código debería tener una FK a Matches o viceversa.
                }


                // 3. Crear la entidad Matches para la BD
                var newMatch = new Matches
                {
                    // Asignamos el host_player_id (Necesitarás añadir esta columna a tu tabla Matches si no existe)
                    // host_player_id = hostPlayer.idPlayer, // DESCOMENTAR SI AÑADES LA COLUMNA
                    creation_time = DateTime.UtcNow,
                    match_status_id = 1, // Asumiendo que 1 = 'Waiting' o 'En Espera' en tu tabla MatchStatus
                    puzzle_id = settings.preloadedPuzzleId ?? 1, // TODO: Manejar puzzle personalizado o default
                    difficulty_id = settings.difficultyId,
                    lobby_code = newLobbyCode // Guardamos el código generado
                };

                // 4. Guardar en la BD
                context.Matches.Add(newMatch);
                await context.SaveChangesAsync(); // Obtenemos el matches_id generado

                // 5. Crear el estado inicial del Lobby en memoria
                var initialState = new LobbyStateDto
                {
                    // lobbyId podría ser el código o el ID de la BD. Usemos el código por ahora.
                    lobbyId = newLobbyCode,
                    hostUsername = hostUsername,
                    players = new List<string> { hostUsername }, // El host es el primer jugador
                    currentSettingsDto = settings // Guarda la configuración inicial
                };

                // 6. Añadir al diccionario de lobbies activos (asegurar thread safety)
                lock (activeLobbies)
                {
                    activeLobbies.Add(newLobbyCode, initialState);
                }

                Console.WriteLine($"Lobby created with code: {newLobbyCode} by host: {hostUsername}"); // Log

                // 7. Devolver resultado exitoso
                return new LobbyCreationResultDto
                {
                    success = true,
                    message = "Lobby created successfully.", // TODO: Lang
                    lobbyCode = newLobbyCode,
                    initialLobbyState = initialState
                };
            }
        }

        // --- Otros métodos de lógica (joinLobbyAsync, leaveLobbyAsync, etc.) irían aquí ---
    }
}