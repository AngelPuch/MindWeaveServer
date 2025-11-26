using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking
{
    [DataContract]
    public class ExpulsionDto
    {
        [DataMember]
        public int MatchId { get; set; }

        [DataMember]
        public int PlayerId { get; set; }

        [DataMember]
        public int ReasonId { get; set; }
        [DataMember]
        public int HostPlayerId { get; set; }
    }
}