using System;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Stats
{
    [DataContract]
    public class MatchHistoryDto
    {
        [DataMember]
        public string MatchId { get; set; }
        [DataMember]
        public bool WasWinner { get; set; }
        [DataMember]
        public int Score { get; set; }
        [DataMember]
        public DateTime MatchDate { get; set; }
    }
}
