using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess; // Necesario para PlayerStats entity
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

        Task<int> saveChangesAsync();

        Task<List<int>> UnlockAchievementsAsync(int playerId, List<int> potentialAchievementIds);
    }
}