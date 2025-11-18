using MindWeaveServer.Contracts.DataContracts.Shared;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking
{
    [DataContract]
    public class GuestJoinResultDto : OperationResultDto
    {
        [DataMember]
        public int PlayerId { get; set; }

        [DataMember]
        public string AssignedGuestUsername { get; set; }

        [DataMember]
        public LobbyStateDto InitialLobbyState { get; set; }
    }
}