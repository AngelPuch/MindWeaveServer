using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking
{
    [DataContract]
    public class GuestJoinRequestDto
    {
        [DataMember]
        public string lobbyCode { get; set; }

        [DataMember]
        public string guestEmail { get; set; }

        [DataMember]
        public string desiredGuestUsername { get; set; }
    }
}