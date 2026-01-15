namespace MindWeaveServer.BusinessLogic.Models
{
    public class PieceMovementContext
    {
        public string LobbyCode { get; set; }
        public int PlayerId { get; set; }
        public int PieceId { get; set; }
        public double NewX { get; set; }
        public double NewY { get; set; }
    }
}
