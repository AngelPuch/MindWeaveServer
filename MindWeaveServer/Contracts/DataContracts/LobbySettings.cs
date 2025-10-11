using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts
{
    [DataContract]
    public class LobbySettings
    {
        [DataMember]
        public int difficultyId { get; set; }

        [DataMember]
        public byte[] customPuzzleImage { get; set; }

        [DataMember]
        public int? preloadedPuzzleId { get; set; }
    }
}
