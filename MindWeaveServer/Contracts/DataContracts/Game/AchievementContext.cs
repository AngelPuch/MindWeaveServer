using MindWeaveServer.DataAccess;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
