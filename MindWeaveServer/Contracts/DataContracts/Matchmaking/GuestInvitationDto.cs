using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking
{
    [DataContract]
    public class GuestInvitationDto
    {
        [DataMember]
        public string inviterUsername { get; set; }

        [DataMember]
        public string guestEmail { get; set; }

        [DataMember]
        public string lobbyCode { get; set; }
    }
}