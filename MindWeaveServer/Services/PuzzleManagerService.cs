using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Resources;
using NLog;
using System;
using System.Collections.Generic;
using System.Data.Entity.Core; 
using System.IO;
using System.ServiceModel;
using System.Threading.Tasks;

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
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.DatabaseError, Lang.ErrorMsgServerOffline, "Database");
                logger.Fatal(ex, "Puzzle Service DB Error in getAvailablePuzzlesAsync");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.GenericServerError, "Server");
                logger.Fatal(ex, "Puzzle Service Critical Error in getAvailablePuzzlesAsync");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public async Task<PuzzleDefinitionDto> getPuzzleDefinitionAsync(int puzzleId, int difficultyId)
        {
            logger.Info("GetPuzzleDefinitionAsync request received for puzzleId: {PuzzleId}, difficultyId: {DifficultyId}", puzzleId, difficultyId);
            try
            {
                return await puzzleLogic.getPuzzleDefinitionAsync(puzzleId, difficultyId);
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.DatabaseError, Lang.ErrorMsgServerOffline, "Database");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (FileNotFoundException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.NotFound, Lang.ErrorPuzzleFileNotFound, "FileSystem");
                logger.Error(ex, "Puzzle Service File Error: Image missing for puzzleId {PuzzleId}", puzzleId);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Resource Missing"));
            }
            catch (IOException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.ErrorReadingPuzzleFile, "FileSystem");
                logger.Error(ex, "Puzzle Service IO Error in getPuzzleDefinitionAsync");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("File System Error"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.GenericServerError, "Server");
                logger.Fatal(ex, "Puzzle Service Critical Error in getPuzzleDefinitionAsync");
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public async Task<UploadResultDto> uploadPuzzleImageAsync(string username, byte[] imageBytes, string fileName)
        {
            logger.Info("uploadPuzzleImageAsync service attempt by user: {Username}, fileName: {FileName}", username ?? "NULL", fileName ?? "NULL");
            try
            {
                return await puzzleLogic.uploadPuzzleImageAsync(username, imageBytes, fileName);
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.DatabaseError, Lang.ErrorMsgServerOffline, "Database");
                logger.Fatal(ex, "Puzzle Service DB Error during upload for {Username}", username);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (IOException ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.ErrorPuzzleUploadFailed, "FileSystem");
                logger.Error(ex, "Puzzle Service IO Error during upload for {Username}", username);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Storage Error"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(ServiceErrorType.Unknown, Lang.GenericServerError, "Server");
                logger.Fatal(ex, "Puzzle Service Critical Error during upload for {Username}", username);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }
    }
}