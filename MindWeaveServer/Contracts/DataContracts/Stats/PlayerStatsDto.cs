using System;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Stats
{
    [DataContract]
    public class PlayerStatsDto
    {
        [DataMember]
        public int PuzzlesCompleted { get; set; }
        [DataMember]
        public int PuzzlesWon { get; set; }
        [DataMember]
        public TimeSpan TotalPlaytime { get; set; }
        [DataMember]
        public int HighestScore { get; set; }
    }
}
