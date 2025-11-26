using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess.Abstractions;
using NLog;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Repositories
{
    public class StatsRepository : IStatsRepository
    {
        private readonly MindWeaveDBEntities1 context;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public StatsRepository(MindWeaveDBEntities1 context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task updatePlayerStatsAsync(PlayerMatchStatsDto matchStats)
        {
            if (matchStats == null) throw new ArgumentNullException(nameof(matchStats));

            var stats = await context.PlayerStats.FirstOrDefaultAsync(s => s.player_id == matchStats.PlayerId);

            if (stats == null)
            {
                stats = new PlayerStats
                {
                    player_id = matchStats.PlayerId,
                    puzzles_completed = 0,
                    puzzles_won = 0,
                    total_playtime_minutes = 0,
                    highest_score = 0
                };
                context.PlayerStats.Add(stats);
            }

            stats.puzzles_completed = (stats.puzzles_completed ?? 0) + 1;
            stats.total_playtime_minutes = (stats.total_playtime_minutes ?? 0) + matchStats.PlaytimeMinutes;

            if (matchStats.IsWin)
            {
                stats.puzzles_won = (stats.puzzles_won ?? 0) + 1;
            }

            if (matchStats.Score > (stats.highest_score ?? 0))
            {
                stats.highest_score = matchStats.Score;
            }
        }

        public async Task<PlayerStats> getPlayerStatsByIdAsync(int playerId)
        {
            return await context.PlayerStats.AsNoTracking().FirstOrDefaultAsync(s => s.player_id == playerId);
        }

        public async Task<List<int>> getPlayerAchievementIdsAsync(int playerId)
        {
            return await context.Player
                .Where(p => p.idPlayer == playerId)
                .SelectMany(p => p.Achievements)
                .Select(a => a.achievements_id)
                .ToListAsync();
        }

        public async Task unlockAchievementAsync(int playerId, int achievementId)
        {
            var player = await context.Player.FindAsync(playerId);
            var achievement = await context.Achievements.FindAsync(achievementId);

            if (player != null && achievement != null)
            {
                if (!player.Achievements.Contains(achievement))
                {
                    player.Achievements.Add(achievement);
                    logger.Info($"Achievement {achievementId} unlocked for Player {playerId}");
                }
            }
        }

        public async Task<int> saveChangesAsync()
        {
            return await context.SaveChangesAsync();
        }

        public async Task<List<int>> UnlockAchievementsAsync(int playerId, List<int> potentialAchievementIds)
        {
            List<int> newlyUnlocked = new List<int>();

            if (potentialAchievementIds == null || !potentialAchievementIds.Any())
            {
                return newlyUnlocked;
            }

            var player = await context.Player
                .Include("Achievements")
                .FirstOrDefaultAsync(p => p.idPlayer == playerId);

            if (player == null) return newlyUnlocked;

            var existingIds = player.Achievements.Select(a => a.achievements_id).ToList();
            var idsToAdd = potentialAchievementIds.Except(existingIds).ToList();

            if (idsToAdd.Any())
            {
                var achievementsToAddEntities = await context.Achievements
                    .Where(a => idsToAdd.Contains(a.achievements_id))
                    .ToListAsync();

                foreach (var achievement in achievementsToAddEntities)
                {
                    player.Achievements.Add(achievement);
                    newlyUnlocked.Add(achievement.achievements_id);
                }

                await context.SaveChangesAsync();
            }

            return newlyUnlocked;
        }
    }
}