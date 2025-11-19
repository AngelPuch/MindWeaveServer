/*
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Resources;
using System;
using System.Collections.Generic;
using System.Data.Entity.Core;
using System.ServiceModel;
using System.Threading.Tasks;
using NLog;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class StatsManagerService : IStatsManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly StatsLogic statsLogic;

        public StatsManagerService()
        {
            var dbContext = new MindWeaveDBEntities1();
            var playerRepo = new PlayerRepository(dbContext);
            var matchmakingRepo = new MatchmakingRepository(dbContext);

            this.statsLogic = new StatsLogic(playerRepo, matchmakingRepo);
            logger.Info("StatsManagerService instance created.");
        }

        public async Task<List<PlayerStatsDto>> getGlobalLeaderboard()
        {
            logger.Info("getGlobalLeaderboard request received.");
            try
            {
                return await statsLogic.getGlobalLeaderboardAsync();
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.DatabaseError, Lang.ErrorMsgServerOffline, "Database");
                logger.Fatal(ex, "Stats Service DB Error in getGlobalLeaderboard");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.GenericServerError, "Server");
                logger.Fatal(ex, "Stats Service Critical Error in getGlobalLeaderboard");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public async Task<List<MatchHistoryDto>> getPlayerMatchHistory(string username)
        {
            logger.Info("getPlayerMatchHistory request received for User: {Username}", username);
            try
            {
                return await statsLogic.getPlayerMatchHistoryAsync(username);
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.DatabaseError, Lang.ErrorMsgServerOffline, "Database");
                logger.Fatal(ex, "Stats Service DB Error in getPlayerMatchHistory for {Username}", username);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.GenericServerError, "Server");
                logger.Fatal(ex, "Stats Service Critical Error in getPlayerMatchHistory for {Username}", username);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }
    }
}
*/