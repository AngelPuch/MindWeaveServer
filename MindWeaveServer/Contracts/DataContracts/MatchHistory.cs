using System;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts
{
    [DataContract]
    public class MatchHistory
    {
        [DataMember]
        public string matchId { get; set; }
        [DataMember]
        public bool wasWinner { get; set; }
        [DataMember]
        public int score { get; set; }
        [DataMember]
        public DateTime matchDate { get; set; }
    }
}
