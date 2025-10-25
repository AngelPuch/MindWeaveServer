using System;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Stats
{
    [DataContract]
    public class PlayerStatsDto
    {
        [DataMember]
        public int puzzlesCompleted { get; set; }
        [DataMember]
        public int puzzlesWon { get; set; }
        [DataMember]
        public TimeSpan totalPlaytime { get; set; }
        [DataMember]
        public int highestScore { get; set; }
    }
}
