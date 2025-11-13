using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Puzzle 
{
    [DataContract]
    public class PuzzleInfoDto
    {
        [DataMember]
        public int puzzleId { get; set; }

        [DataMember]
        public string name { get; set; }

        [DataMember]
        public string imagePath { get; set; }
        [DataMember]
        public bool isUploaded { get; set; }

        [DataMember]
        public byte[] imageBytes { get; set; }
    }
}