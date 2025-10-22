using System;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts
{
    [DataContract]
    public class FriendRequestInfoDto
    {
       
        [DataMember]
        public string requesterUsername { get; set; }

        [DataMember]
        public DateTime requestDate { get; set; }
        [DataMember]
        public string avatarPath { get; set; }
    }
}
