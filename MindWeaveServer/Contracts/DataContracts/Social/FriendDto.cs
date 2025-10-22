using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts
{
    [DataContract]
    public class FriendDto
    {
        [DataMember]
        public string username { get; set; }
        [DataMember]
        public bool isOnline { get; set; }
        [DataMember]
        public string avatarPath { get; set; }
    }
}
