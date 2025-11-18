using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Social
{
    [DataContract]
    public class FriendDto
    {
        [DataMember]
        public string Username { get; set; }
        [DataMember]
        public bool IsOnline { get; set; }
        [DataMember]
        public string AvatarPath { get; set; }
    }
}
