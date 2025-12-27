using System;
using System.Threading.Tasks;
using MindWeaveServer.BusinessLogic.Abstractions;
using NLog;

namespace MindWeaveServer.BusinessLogic.Services
{
    public class PlayerExpulsionService : IPlayerExpulsionService
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly Lazy<MatchmakingLogic> matchmakingLogicLazy;

        public PlayerExpulsionService(Lazy<MatchmakingLogic> matchmakingLogicLazy)
        {
            this.matchmakingLogicLazy = matchmakingLogicLazy
                                        ?? throw new ArgumentNullException(nameof(matchmakingLogicLazy));
        }

        public Task expelPlayerAsync(string lobbyCode, string username, string reason)
        {
            if (string.IsNullOrWhiteSpace(lobbyCode))
            {
                throw new ArgumentNullException(nameof(lobbyCode));
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentNullException(nameof(username));
            }

            _ = executeExpulsionAsync(lobbyCode, username, reason);


            return Task.CompletedTask;
        }

        private async Task executeExpulsionAsync(string lobbyCode, string username, string reason)
        {
            try
            {
                await matchmakingLogicLazy.Value.expelPlayerAsync(lobbyCode, username, reason);
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "Invalid operation expelling player from lobby {0}", lobbyCode);
            }
        }

    }
}