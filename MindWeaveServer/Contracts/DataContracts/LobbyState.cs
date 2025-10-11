using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts
{
    [DataContract]
    public class LobbyState
    {
        [DataMember]
        public string lobbyId { get; set; }

        [DataMember]
        public string hostUsername { get; set; }

        [DataMember]
        public List<string> players { get; set; }

        [DataMember]
        public LobbySettings currentSettings { get; set; }
    }
}
