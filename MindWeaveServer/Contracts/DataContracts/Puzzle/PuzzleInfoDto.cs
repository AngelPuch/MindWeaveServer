using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Puzzle 
{
    [DataContract]
    public class PuzzleInfoDto
    {
        [DataMember]
        public int PuzzleId { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string ImagePath { get; set; }
        [DataMember]
        public bool IsUploaded { get; set; }

        [DataMember]
        public byte[] ImageBytes { get; set; }
    }
}