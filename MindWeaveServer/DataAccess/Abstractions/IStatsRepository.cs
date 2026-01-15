using MindWeaveServer.Contracts.DataContracts.Stats;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Abstractions
{
    public interface IStatsRepository
    {
        Task updatePlayerStatsAsync(PlayerMatchStatsDto matchStats);

        Task<PlayerStats> getPlayerStatsByIdAsync(int playerId);

        Task<List<int>> getPlayerAchievementIdsAsync(int playerId);

        Task unlockAchievementAsync(int playerId, int achievementId);

        Task<List<Achievements>> getAllAchievementsAsync();

        Task<List<int>> unlockAchievementsAsync(int playerId, List<int> potentialAchievementIds);

        Task addPlaytimeToPlayerAsync(int playerId, int minutes);
    }
}