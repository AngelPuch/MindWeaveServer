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

            logger.Info("Initiating player expulsion from lobby {LobbyCode}. Reason: {Reason}",
                lobbyCode, reason ?? "Unspecified");

            _ = Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogicLazy.Value.expelPlayerAsync(lobbyCode, username, reason);
                    logger.Info("Player expelled successfully from lobby {LobbyCode}", lobbyCode);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed to expel player from lobby {LobbyCode}", lobbyCode);
                }
            });

            return Task.CompletedTask;
        }
    }
}