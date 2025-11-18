using System;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Social
{
    [DataContract]
    public class FriendRequestInfoDto
    {
       
        [DataMember]
        public string RequesterUsername { get; set; }

        [DataMember]
        public DateTime RequestDate { get; set; }
        [DataMember]
        public string AvatarPath { get; set; }
    }
}
