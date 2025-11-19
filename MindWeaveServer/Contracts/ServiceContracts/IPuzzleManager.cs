using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Contracts.DataContracts.Shared;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract]
    public interface IPuzzleManager
    {
        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<List<PuzzleInfoDto>> getAvailablePuzzlesAsync();

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<UploadResultDto> uploadPuzzleImageAsync(string username, byte[] imageBytes, string fileName);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<PuzzleDefinitionDto> getPuzzleDefinitionAsync (int puzzleId, int difficultyId);
    }
}