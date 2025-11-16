using MindWeaveServer.Contracts.DataContracts.Shared;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking
{
    [DataContract]
    public class GuestJoinResultDto : OperationResultDto
    {
        [DataMember]
        public int playerId { get; set; }

        [DataMember]
        public string assignedGuestUsername { get; set; }

        [DataMember]
        public LobbyStateDto initialLobbyState { get; set; }
    }
}