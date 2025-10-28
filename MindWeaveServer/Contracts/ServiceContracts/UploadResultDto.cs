using System.Runtime.Serialization;
using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [DataContract]
    public class UploadResultDto : OperationResultDto
    {
        [DataMember]
        public int newPuzzleId { get; set; }
    }
}