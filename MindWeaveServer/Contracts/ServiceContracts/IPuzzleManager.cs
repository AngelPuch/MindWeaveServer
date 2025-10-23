// MindWeaveServer/Contracts/ServiceContracts/IPuzzleManager.cs
using MindWeaveServer.Contracts.DataContracts; // Para los DTOs base y nuevos
// Si creaste subcarpeta 'Puzzle' para DTOs: using MindWeaveServer.Contracts.DataContracts.Puzzle;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract]
    public interface IPuzzleManager
    {
        [OperationContract]
        Task<List<PuzzleInfoDto>> getAvailablePuzzlesAsync();

        [OperationContract]
        Task<UploadResultDto> uploadPuzzleImageAsync(string username, byte[] imageBytes, string fileName);
    }
}