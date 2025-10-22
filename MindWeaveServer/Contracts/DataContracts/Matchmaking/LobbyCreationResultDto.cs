// MindWeaveServer/Contracts/DataContracts/Matchmaking/LobbyCreationResultDto.cs
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking // O donde prefieras ponerlo
{
    [DataContract]
    public class LobbyCreationResultDto : OperationResultDto // Hereda de OperationResultDto
    {
        [DataMember]
        public string lobbyCode { get; set; } // El código generado

        [DataMember]
        public LobbyStateDto initialLobbyState { get; set; } // El estado inicial del lobby
    }
}