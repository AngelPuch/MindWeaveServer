using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Puzzle
{
    [DataContract]
    public class PuzzlePieceDefinitionDto
    {
        [DataMember]
        public int pieceId { get; set; }

  
        [DataMember]
        public int sourceX { get; set; }

        [DataMember]
        public int sourceY { get; set; }

        [DataMember]
        public double correctX { get; set; }

        [DataMember]
        public double correctY { get; set; }

        [DataMember]
        public int width { get; set; }

        [DataMember]
        public int height { get; set; }
    }
}