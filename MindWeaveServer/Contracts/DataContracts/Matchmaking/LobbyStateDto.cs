using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts
{
    [DataContract]
    public class LobbyStateDto
    {
        [DataMember]
        public string lobbyId { get; set; }

        [DataMember]
        public string hostUsername { get; set; }

        [DataMember]
        public List<string> players { get; set; }



        [DataMember]
        public LobbySettingsDto currentSettingsDto { get; set; }
    }
}
