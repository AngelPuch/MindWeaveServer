using System;

namespace MindWeaveServer.BusinessLogic.Models
{
    public class PuzzlePieceState
    {
        public int PieceId { get; set; }
        public double FinalX { get; set; }
        public double FinalY { get; set; }
        public double CurrentX { get; set; }
        public double CurrentY { get; set; }
        public bool IsPlaced { get; set; }
        public int? HeldByPlayerId { get; set; }
        public DateTime? GrabTime { get; set; }
    }
}