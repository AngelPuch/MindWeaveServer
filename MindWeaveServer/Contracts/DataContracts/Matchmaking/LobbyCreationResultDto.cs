using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking 
{
    [DataContract]
    public class LobbyCreationResultDto : OperationResultDto 
    {
        [DataMember]
        public string lobbyCode { get; set; } 

        [DataMember]
        public LobbyStateDto initialLobbyState { get; set; } 
    }
}