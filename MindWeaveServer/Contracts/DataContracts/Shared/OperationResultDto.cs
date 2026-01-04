using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Shared
{
    [DataContract]
    public class OperationResultDto
    {
        [DataMember]
        public bool Success { get; set; }

        [DataMember]
        public string MessageCode { get; set; }

        [DataMember]
        public string[] MessageParams { get; set; } 
    }
}