using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class StatsLogic
    {
        private readonly IStatsRepository statsRepository;
        private readonly IPlayerRepository playerRepository;

        public StatsLogic(IStatsRepository statsRepository, IPlayerRepository playerRepository)
        {
            this.statsRepository = statsRepository;
            this.playerRepository = playerRepository;
        }

        public async Task processMatchResultsAsync(PlayerMatchStatsDto matchStats)
        {
            if (matchStats == null)
            {
                return;
            }

            await statsRepository.updatePlayerStatsAsync(matchStats);
        }

        public async Task<PlayerStats> getPlayerStatsAsync(int playerId)
        {
            return await statsRepository.getPlayerStatsByIdAsync(playerId);
        }

        public async Task<List<int>> unlockAchievementsAsync(int playerId, List<int> achievementIds)
        {
            return await statsRepository.unlockAchievementsAsync(playerId, achievementIds);
        }

        public async Task updatePlaytimeOnly(string username, int minutesPlayed)
        {
            var player = await playerRepository.getPlayerByUsernameAsync(username);

            if (player != null)
            {
                await statsRepository.addPlaytimeToPlayerAsync(player.idPlayer, minutesPlayed);
            }
        }
    }
}