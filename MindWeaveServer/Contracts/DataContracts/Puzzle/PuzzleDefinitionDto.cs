using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Puzzle
{
    [DataContract]
    public class PuzzleDefinitionDto
    {
        [DataMember]
        public byte[] FullImageBytes { get; set; }

        [DataMember]
        public int PuzzleWidth { get; set; } 

        [DataMember]
        public int PuzzleHeight { get; set; }

        [DataMember]
        public List<PuzzlePieceDefinitionDto> Pieces { get; set; }
    }
}