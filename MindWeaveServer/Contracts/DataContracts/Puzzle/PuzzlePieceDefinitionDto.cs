using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Puzzle
{
    [DataContract]
    public class PuzzlePieceDefinitionDto
    {
        [DataMember]
        public int PieceId { get; set; }
        [DataMember]
        public int SourceX { get; set; }
        [DataMember]
        public int SourceY { get; set; }
        [DataMember]
        public double CorrectX { get; set; }
        [DataMember]
        public double CorrectY { get; set; }
        [DataMember]
        public int Width { get; set; }
        [DataMember]
        public int Height { get; set; }
        [DataMember]
        public double InitialX { get; set; }
        [DataMember]
        public double InitialY { get; set; }
        [DataMember]
        public int? TopNeighborId { get; set; }
        [DataMember]
        public int? BottomNeighborId { get; set; }
        [DataMember]
        public int? LeftNeighborId { get; set; }
        [DataMember]
        public int? RightNeighborId { get; set; }
        [DataMember]
        public byte[] PieceImageBytes { get; set; }
        [DataMember]
        public int RenderWidth { get; set; }
        [DataMember]
        public int RenderHeight { get; set; }
        [DataMember]
        public int OffsetX { get; set; }
        [DataMember]
        public int OffsetY { get; set; }
    }
}