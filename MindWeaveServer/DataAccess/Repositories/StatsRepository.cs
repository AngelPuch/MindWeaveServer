using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess.Abstractions;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Repositories
{
    public class StatsRepository : IStatsRepository
    {
        private readonly Func<MindWeaveDBEntities1> contextFactory;

        private const int INITIAL_STAT_VALUE = 0;
        private const int STAT_INCREMENT = 1;
        private const string NAVIGATION_ACHIEVEMENTS = "Achievements";

        public StatsRepository(Func<MindWeaveDBEntities1> contextFactory)
        {
            this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task updatePlayerStatsAsync(PlayerMatchStatsDto matchStats)
        {
            if (matchStats == null)
            {
                throw new ArgumentNullException(nameof(matchStats));
            }

            using (var context = contextFactory())
            {
                var stats = await context.PlayerStats.FirstOrDefaultAsync(s => s.player_id == matchStats.PlayerId);

                if (stats == null)
                {
                    stats = new PlayerStats
                    {
                        player_id = matchStats.PlayerId,
                        puzzles_completed = INITIAL_STAT_VALUE,
                        puzzles_won = INITIAL_STAT_VALUE,
                        total_playtime_minutes = INITIAL_STAT_VALUE,
                        highest_score = INITIAL_STAT_VALUE
                    };
                    context.PlayerStats.Add(stats);
                }

                stats.puzzles_completed = (stats.puzzles_completed ?? INITIAL_STAT_VALUE) + STAT_INCREMENT;
                stats.total_playtime_minutes = (stats.total_playtime_minutes ?? INITIAL_STAT_VALUE) + matchStats.PlaytimeMinutes;

                if (matchStats.IsWin)
                {
                    stats.puzzles_won = (stats.puzzles_won ?? INITIAL_STAT_VALUE) + STAT_INCREMENT;
                }

                if (matchStats.Score > (stats.highest_score ?? INITIAL_STAT_VALUE))
                {
                    stats.highest_score = matchStats.Score;
                }

                await context.SaveChangesAsync();
            }
        }

        public async Task<PlayerStats> getPlayerStatsByIdAsync(int playerId)
        {
            using (var context = contextFactory())
            {
                return await context.PlayerStats.AsNoTracking().FirstOrDefaultAsync(s => s.player_id == playerId);
            }
        }

        public async Task<List<int>> getPlayerAchievementIdsAsync(int playerId)
        {
            using (var context = contextFactory())
            {
                var player = await context.Player
                    .Include(NAVIGATION_ACHIEVEMENTS)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.idPlayer == playerId);

                if (player != null && player.Achievements != null)
                {
                    return player.Achievements.Select(a => a.achievements_id).ToList();
                }

                return new List<int>();
            }
        }

        public async Task unlockAchievementAsync(int playerId, int achievementId)
        {
            using (var context = contextFactory())
            {
                var player = await context.Player.Include(NAVIGATION_ACHIEVEMENTS).FirstOrDefaultAsync(p => p.idPlayer == playerId);
                var achievement = await context.Achievements.FindAsync(achievementId);

                if (player != null && achievement != null &&player.Achievements.All(a => a.achievements_id != achievementId))
                {
                    player.Achievements.Add(achievement);
                    await context.SaveChangesAsync();
                    
                }
            }
        }

        public async Task<List<Achievements>> getAllAchievementsAsync()
        {
            using (var context = contextFactory())
            {
                return await context.Achievements.AsNoTracking().ToListAsync();
            }
        }

        public async Task<List<int>> unlockAchievementsAsync(int playerId, List<int> potentialAchievementIds)
        {
            List<int> newlyUnlocked = new List<int>();

            if (potentialAchievementIds == null || !potentialAchievementIds.Any())
            {
                return newlyUnlocked;
            }

            using (var context = contextFactory())
            {
                var player = await context.Player
                    .Include(NAVIGATION_ACHIEVEMENTS)
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

        public async Task addPlaytimeToPlayerAsync(int playerId, int minutes)
        {
            using (var context = contextFactory())
            {
                var stats = await context.PlayerStats.FirstOrDefaultAsync(s => s.player_id == playerId);

                if (stats != null)
                {
                    stats.total_playtime_minutes = (stats.total_playtime_minutes ?? 0) + minutes;
                    await context.SaveChangesAsync();
                }
            }
        }

        public async Task<int> saveChangesAsync()
        {
            return await Task.FromResult(0);
        }
    }
}