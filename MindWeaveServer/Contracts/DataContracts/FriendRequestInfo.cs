using System;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts
{
    [DataContract]
    public class FriendRequestInfo
    {
       
        [DataMember]
        public string requesterUsername { get; set; }

        [DataMember]
        public DateTime requestDate { get; set; }
    }
}
