using MindWeaveServer.Contracts.DataContracts.Stats;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Abstractions
{
    public interface IStatsRepository
    {
        Task updatePlayerStatsAsync(PlayerMatchStatsDto matchStats);

        Task<List<int>> getPlayerAchievementIdsAsync(int playerId);

        Task unlockAchievementAsync(int playerId, int achievementId);

        Task<int> saveChangesAsync();
    }
}