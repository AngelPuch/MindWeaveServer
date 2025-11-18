using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking
{
    [DataContract]
    public class GuestJoinRequestDto
    {
        [DataMember]
        public string LobbyCode { get; set; }

        [DataMember]
        public string GuestEmail { get; set; }

        [DataMember]
        public string DesiredGuestUsername { get; set; }
    }
}