using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Game
{
    [DataContract]
    public class MatchEndResultDto
    {
        [DataMember]
        public int MatchId { get; set; }

        [DataMember]
        public string Reason { get; set; }

        [DataMember]
        public double TotalTimeElapsedSeconds { get; set; }

        [DataMember]
        public List<PlayerResultDto> PlayerResults { get; set; }
    }
}