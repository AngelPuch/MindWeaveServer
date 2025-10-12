using System;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts
{
    [DataContract]
    public class ChatMessageDto
    {
        [DataMember]
        public string senderUsername { get; set; }
        [DataMember]
        public string content { get; set; }
        [DataMember]
        public DateTime timestamp { get; set; }
    }
}
