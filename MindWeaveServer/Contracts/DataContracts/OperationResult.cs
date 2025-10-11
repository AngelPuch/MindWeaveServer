using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts
{
    [DataContract]
    public class OperationResult
    {
        [DataMember]
        public bool success { get; set; }
        [DataMember]
        public string message { get; set; }
    }
}
