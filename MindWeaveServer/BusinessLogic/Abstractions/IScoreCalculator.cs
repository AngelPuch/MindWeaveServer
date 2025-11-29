namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface IScoreCalculator
    {
        /// <summary>
        /// Calculates points awarded for placing a piece correctly.
        /// </summary>
        /// <param name="context">Context containing all information needed for calculation.</param>
        /// <returns>Points earned and any bonus types applied.</returns>
        ScoreResult calculatePointsForPlacement(ScoreCalculationContext context);

        /// <summary>
        /// Calculates penalty points for incorrect placement.
        /// </summary>
        /// <param name="negativeStreak">Current negative streak count.</param>
        /// <returns>Penalty points to subtract.</returns>
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