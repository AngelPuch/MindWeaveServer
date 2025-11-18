using System;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Chat
{
    [DataContract]
    public class ChatMessageDto
    {
        [DataMember]
        public string SenderUsername { get; set; }
        [DataMember]
        public string Content { get; set; }
        [DataMember]
        public DateTime Timestamp { get; set; }
    }
}
