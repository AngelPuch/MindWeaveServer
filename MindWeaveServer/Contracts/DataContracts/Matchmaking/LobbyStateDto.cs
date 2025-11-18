using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking
{
    [DataContract]
    public class LobbyStateDto
    {
        [DataMember]
        public string LobbyId { get; set; }

        [DataMember]
        public string HostUsername { get; set; }

        [DataMember]
        public List<string> Players { get; set; }

        [DataMember]
        public string PuzzleImagePath { get; set; }

        [DataMember]
        public LobbySettingsDto CurrentSettingsDto { get; set; }
    }
}
