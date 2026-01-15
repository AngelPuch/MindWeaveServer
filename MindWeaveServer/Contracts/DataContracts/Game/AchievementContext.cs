using MindWeaveServer.DataAccess;

namespace MindWeaveServer.Contracts.DataContracts.Game
{
    public class AchievementContext
    {
        public PlayerStats PlayerStats { get; set; }       
        public MatchParticipants CurrentMatchStats { get; set; }
        public Matches MatchInfo { get; set; }              
        public Puzzles PuzzleInfo { get; set; }             
        public int TotalParticipants { get; set; }
    }
}
