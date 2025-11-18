using System.Runtime.Serialization;
using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.Contracts.DataContracts.Puzzle
{
    [DataContract]
    public class UploadResultDto : OperationResultDto
    {
        [DataMember]
        public int NewPuzzleId { get; set; }
    }
}