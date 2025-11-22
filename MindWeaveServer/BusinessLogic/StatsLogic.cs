using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess.Abstractions;
using NLog;
using System;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class StatsLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly IStatsRepository statsRepository;

        private const int ACHIEVEMENT_FIRST_WIN = 1;
        private const int ACHIEVEMENT_VETERAN = 2; 
        private const int ACHIEVEMENT_HIGH_SCORE = 3; 

        public StatsLogic(IStatsRepository statsRepository)
        {
            this.statsRepository = statsRepository ?? throw new ArgumentNullException(nameof(statsRepository));
        }

        public async Task processMatchResultsAsync(PlayerMatchStatsDto matchStats)
        {
            if (matchStats == null) throw new ArgumentNullException(nameof(matchStats));

            logger.Info("processMatchResultsAsync called for PlayerID: {PlayerId}, Rank: {Rank}",
                matchStats.PlayerId, matchStats.Rank);

            await statsRepository.updatePlayerStatsAsync(matchStats);

            await checkAchievementsAsync(matchStats);

            await statsRepository.saveChangesAsync();
        }

        private async Task checkAchievementsAsync(PlayerMatchStatsDto stats)
        {
            var currentAchievements = await statsRepository.getPlayerAchievementIdsAsync(stats.PlayerId);

            
            if (stats.IsWin && !currentAchievements.Contains(ACHIEVEMENT_FIRST_WIN))
            {
                await statsRepository.unlockAchievementAsync(stats.PlayerId, ACHIEVEMENT_FIRST_WIN);
            }

            
            if (stats.Score >= 1000 && !currentAchievements.Contains(ACHIEVEMENT_HIGH_SCORE))
            {
                await statsRepository.unlockAchievementAsync(stats.PlayerId, ACHIEVEMENT_HIGH_SCORE);
            }
        }
    }
}