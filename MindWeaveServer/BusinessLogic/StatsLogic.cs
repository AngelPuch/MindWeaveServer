/*
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class StatsLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly IPlayerRepository playerRepository;
        private readonly IMatchmakingRepository matchmakingRepository; // Para historial de partidas

        public StatsLogic(IPlayerRepository playerRepo, IMatchmakingRepository matchmakingRepo)
        {
            this.playerRepository = playerRepo ?? throw new ArgumentNullException(nameof(playerRepo));
            this.matchmakingRepository = matchmakingRepo ?? throw new ArgumentNullException(nameof(matchmakingRepo));
            logger.Info("StatsLogic instance created.");
        }

        public async Task<List<PlayerStatsDto>> getGlobalLeaderboardAsync()
        {
            logger.Info("getGlobalLeaderboardAsync logic called.");

            // Lógica: Obtener jugadores ordenados por puntuación más alta o victorias
            // Asumimos que playerRepository tiene un método para esto, o lo hacemos con LINQ sobre los DbSets si el repo lo permite.
            // Si no tienes un método específico en el repo, aquí simulo la llamada lógica.

            // Imaginemos que agregaste 'getTopPlayersAsync' a tu repositorio, 
            // o usamos una consulta directa si tu arquitectura lo permite. 
            // Por limpieza, deberías tener: playerRepository.getTopPlayersByScoreAsync(10);

            var topPlayers = await playerRepository.getTopPlayersByScoreAsync(10); // Top 10

            var leaderboard = topPlayers.Select(p => new PlayerStatsDto
            {
                // Asumiendo que p es una entidad Player con PlayerStats nav property
                Username = p.username,
                HighestScore = p.PlayerStats?.highest_score ?? 0,
                PuzzlesWon = p.PlayerStats?.puzzles_won ?? 0,
                PuzzlesCompleted = p.PlayerStats?.puzzles_completed ?? 0,
                // Puedes agregar AvatarPath si el DTO lo requiere para mostrar la foto en el ranking
            }).ToList();

            logger.Info("Leaderboard retrieved with {Count} entries.", leaderboard.Count);
            return leaderboard;
        }

        public async Task<List<MatchHistoryDto>> getPlayerMatchHistoryAsync(string username)
        {
            logger.Info("getPlayerMatchHistoryAsync called for User: {Username}", username ?? "NULL");

            if (string.IsNullOrWhiteSpace(username))
            {
                return new List<MatchHistoryDto>();
            }

            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                logger.Warn("Get match history failed: Player {Username} not found.", username);
                return new List<MatchHistoryDto>();
            }

            // Consultar historial
            var matches = await matchmakingRepository.getMatchesByPlayerIdAsync(player.idPlayer);

            var history = matches.Select(m => new MatchHistoryDto
            {
                MatchId = m.matches_id,
                PuzzleName = m.Puzzles?.image_path ?? "Unknown Puzzle", // O lógica para sacar nombre limpio
                DatePlayed = m.start_time ?? m.creation_time,
                Difficulty = m.DifficultyLevels?.name ?? "Unknown",
                IsWin = determineIfWin(m, player.idPlayer), // Lógica auxiliar
                Score = getPlayerScoreInMatch(m, player.idPlayer)
            }).OrderByDescending(h => h.DatePlayed).ToList();

            logger.Info("Found {Count} history records for User: {Username}", history.Count, username);
            return history;
        }

        // Métodos auxiliares privados para lógica de mapeo
        private bool determineIfWin(Matches match, int playerId)
        {
            // Ejemplo: Si hay tabla de ganadores o rank en MatchParticipants
            var participant = match.MatchParticipants.FirstOrDefault(p => p.player_id == playerId);
            return participant?.final_rank == 1;
        }

        private int getPlayerScoreInMatch(Matches match, int playerId)
        {
            var participant = match.MatchParticipants.FirstOrDefault(p => p.player_id == playerId);
            return participant?.score ?? 0;
        }
    }
}
*/