using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.Utilities.Abstractions;
using NLog;
using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;
using Autofac;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class PuzzleManagerService : IPuzzleManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private const string OPERATION_GET_AVAILABLE_PUZZLES = "GetAvailablePuzzlesOperation";
        private const string OPERATION_GET_PUZZLE_DEFINITION = "GetPuzzleDefinitionOperation";
        private const string OPERATION_UPLOAD_PUZZLE_IMAGE = "UploadPuzzleImageOperation";

        private readonly PuzzleLogic puzzleLogic;
        private readonly IServiceExceptionHandler exceptionHandler;

        public PuzzleManagerService() : this(
            Bootstrapper.Container.Resolve<PuzzleLogic>(),
            Bootstrapper.Container.Resolve<IServiceExceptionHandler>())
        {
        }

        public PuzzleManagerService(PuzzleLogic puzzleLogic, IServiceExceptionHandler exceptionHandler)
        {
            this.puzzleLogic = puzzleLogic;
            this.exceptionHandler = exceptionHandler;
        }

        public async Task<List<PuzzleInfoDto>> getAvailablePuzzlesAsync()
        {
            logger.Info("Request received: GetAvailablePuzzlesAsync.");
            try
            {
                return await puzzleLogic.getAvailablePuzzlesAsync();
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_GET_AVAILABLE_PUZZLES);
            }
        }

        public async Task<PuzzleDefinitionDto> getPuzzleDefinitionAsync(int puzzleId, int difficultyId)
        {
            logger.Info("Request received: GetPuzzleDefinitionAsync for PuzzleId: {PuzzleId}, DifficultyId: {DifficultyId}.", puzzleId, difficultyId);
            try
            {
                return await puzzleLogic.getPuzzleDefinitionAsync(puzzleId, difficultyId);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_GET_PUZZLE_DEFINITION);
            }
        }

        public async Task<UploadResultDto> uploadPuzzleImageAsync(string username, byte[] imageBytes, string fileName)
        {
            logger.Info("Request received: UploadPuzzleImageAsync.");
            try
            {
                return await puzzleLogic.uploadPuzzleImageAsync(username, imageBytes, fileName);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_UPLOAD_PUZZLE_IMAGE);
            }
        }
    }
}