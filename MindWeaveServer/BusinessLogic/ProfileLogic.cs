using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess;
using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class ProfileLogic
    {
        public async Task<PlayerProfileViewDto> getPlayerProfileViewAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            using (var context = new MindWeaveDBEntities1())
            {
                var player = await context.Player
                    .Include(p => p.PlayerStats)
                    .Include(p => p.Achievements)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));

                if (player == null)
                {
                    return null;
                }

                var profileViewDto = new PlayerProfileViewDto
                {
                    username = player.username,
                    avatarPath = player.avatar_path,
                    stats = new PlayerStatsDto
                    {
                        puzzlesCompleted = player.PlayerStats?.puzzles_completed ?? 0,
                        puzzlesWon = player.PlayerStats?.puzzles_won ?? 0,
                        totalPlaytime = TimeSpan.FromMinutes(player.PlayerStats?.total_playtime_minutes ?? 0),
                        highestScore = player.PlayerStats?.highest_score ?? 0
                    },
                    achievements = player.Achievements.Select(ach => new AchievementDto
                    {
                        name = ach.name,
                        description = ach.description,
                        iconPath = ach.icon_path
                    }).ToList()
                };

                return profileViewDto;
            }
        }
    }
}