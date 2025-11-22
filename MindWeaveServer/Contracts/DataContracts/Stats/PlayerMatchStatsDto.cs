using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Stats
{
    [DataContract]
    public class PlayerMatchStatsDto
    {
        [DataMember]
        public int PlayerId { get; set; }

        [DataMember]
        public int Score { get; set; }

        [DataMember]
        public bool IsWin { get; set; }

        [DataMember]
        public int PlaytimeMinutes { get; set; }

        [DataMember]
        public int Rank { get; set; }

    }
}