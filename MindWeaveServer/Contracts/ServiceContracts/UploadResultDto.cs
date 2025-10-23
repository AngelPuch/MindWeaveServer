using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts 
{
    [DataContract]
    public class UploadResultDto : OperationResultDto 
    {
        [DataMember]
        public int newPuzzleId { get; set; } 
    }
}