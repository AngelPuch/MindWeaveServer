using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Matchmaking
{
    [DataContract]
    public class LobbySettingsDto
    {
        [DataMember]
        public int DifficultyId { get; set; }

        [DataMember]
        public byte[] CustomPuzzleImage { get; set; }

        [DataMember]
        public int? PreloadedPuzzleId { get; set; }
    }
}
