using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Puzzle
{
    [DataContract]
    public class PuzzleDefinitionDto
    {
        [DataMember]
        public byte[] fullImageBytes { get; set; }

        [DataMember]
        public int puzzleWidth { get; set; } 

        [DataMember]
        public int puzzleHeight { get; set; }

        [DataMember]
        public List<PuzzlePieceDefinitionDto> pieces { get; set; }
    }
}