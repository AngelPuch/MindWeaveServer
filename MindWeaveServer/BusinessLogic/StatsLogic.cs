using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class StatsLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly IStatsRepository statsRepository;

        public StatsLogic(IStatsRepository statsRepository)
        {
            this.statsRepository = statsRepository;
        }

        public async Task processMatchResultsAsync(PlayerMatchStatsDto matchStats)
        {
            if (matchStats == null) throw new ArgumentNullException(nameof(matchStats));

            logger.Info("processMatchResultsAsync called for PlayerID: {PlayerId}, Rank: {Rank}",
                matchStats.PlayerId, matchStats.Rank);

            await statsRepository.updatePlayerStatsAsync(matchStats);

            await statsRepository.saveChangesAsync();
        }

        public async Task<PlayerStats> getPlayerStatsAsync(int playerId)
        {
            return await statsRepository.getPlayerStatsByIdAsync(playerId);
        }

    
        public async Task<List<int>> unlockAchievementsAsync(int playerId, List<int> achievementIds)
        {
            return await statsRepository.UnlockAchievementsAsync(playerId, achievementIds);
        }
    }
}