using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Heartbeat
{
    [DataContract]
    public class HeartbeatRegistrationResult
    {
        [DataMember]
        public bool Success { get; set; }

        [DataMember]
        public string MessageCode { get; set; }

        [DataMember]
        public int HeartbeatIntervalMs { get; set; }

        [DataMember]
        public int TimeoutMs { get; set; }
    }
}