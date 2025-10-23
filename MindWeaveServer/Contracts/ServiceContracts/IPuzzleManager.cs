﻿using MindWeaveServer.Contracts.DataContracts; 
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