using MindWeaveServer.BusinessLogic.Models;

namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface IScoreCalculator
    {
        ScoreResult calculatePointsForPlacement(ScoreCalculationContext context);

        int calculatePenaltyPoints(int negativeStreak);
    }

    public class ScoreCalculationContext
    {
        public PlayerSessionData Player { get; set; }
        public int PieceId { get; set; }
        public bool IsEdgePiece { get; set; }
        public bool IsFirstBloodAvailable { get; set; }
        public bool IsPuzzleComplete { get; set; }
    }

    public class ScoreResult
    {
        public int Points { get; set; }
        public string BonusType { get; set; }
        public bool ClaimedFirstBlood { get; set; }
    }
}