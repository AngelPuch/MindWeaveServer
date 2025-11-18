using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking
{
    [DataContract]
    public class GuestInvitationDto
    {
        [DataMember]
        public string InviterUsername { get; set; }

        [DataMember]
        public string GuestEmail { get; set; }

        [DataMember]
        public string LobbyCode { get; set; }
    }
}