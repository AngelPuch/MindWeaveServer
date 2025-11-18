using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Resources;
using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
using NLog;

namespace MindWeaveServer.Services
{
    
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class PuzzleManagerService : IPuzzleManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly PuzzleLogic puzzleLogic;

        public PuzzleManagerService()
        {
            var dbContext = new MindWeaveDBEntities1();
            var playerRepository = new PlayerRepository(dbContext);
            var puzzleRepository = new PuzzleRepository(dbContext);

            puzzleLogic = new PuzzleLogic(puzzleRepository, playerRepository);
        }

        public async Task<List<PuzzleInfoDto>> getAvailablePuzzlesAsync()
        {
            logger.Info("getAvailablePuzzlesAsync request received.");
            try
            {
                return await puzzleLogic.getAvailablePuzzlesAsync();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in getAvailablePuzzlesAsync service call.");
                return new List<PuzzleInfoDto>();
            }
        }

        public async Task<PuzzleDefinitionDto> getPuzzleDefinitionAsync(int puzzleId, int difficultyId)
        {
            logger.Info("GetPuzzleDefinitionAsync request received for puzzleId: {PuzzleId}, difficultyId: {DifficultyId}", puzzleId, difficultyId);
            try
            {
                return await puzzleLogic.getPuzzleDefinitionAsync(puzzleId, difficultyId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in GetPuzzleDefinitionAsync service call for puzzleId: {PuzzleId}", puzzleId);
                return null;
            }
        }

        public async Task<UploadResultDto> uploadPuzzleImageAsync(string username, byte[] imageBytes, string fileName)
        {
            logger.Info("uploadPuzzleImageAsync service attempt by user: {Username}, fileName: {FileName}", username ?? "NULL", fileName ?? "NULL");
            try
            {
                return await puzzleLogic.uploadPuzzleImageAsync(username, imageBytes, fileName);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Generic Error in uploadPuzzleImageAsync service call for {Username}", username ?? "NULL");
                return new UploadResultDto { Success = false, Message = Lang.GenericServerError };
            }
        }
    }
}