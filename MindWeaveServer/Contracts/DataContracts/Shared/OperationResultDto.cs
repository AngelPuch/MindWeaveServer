using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Shared
{
    [DataContract]
    public class OperationResultDto
    {
        [DataMember]
        public bool success { get; set; }
        [DataMember]
        public string message { get; set; }
    }
}
