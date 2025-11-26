using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking
{
    [DataContract]
    public class MatchParticipantStatsUpdateDto
    {
        [DataMember]
        public int MatchId { get; set; }

        [DataMember]
        public int PlayerId { get; set; }

        [DataMember]
        public int Score { get; set; }

        [DataMember]
        public int PiecesPlaced { get; set; }

        [DataMember]
        public int Rank { get; set; }
    }
}