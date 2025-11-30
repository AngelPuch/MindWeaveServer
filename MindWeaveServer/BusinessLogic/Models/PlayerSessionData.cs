using System;
using System.Collections.Generic;
using MindWeaveServer.Contracts.ServiceContracts;

namespace MindWeaveServer.BusinessLogic.Models
{
    public class PlayerSessionData
    {
        public int PlayerId { get; set; }
        public string Username { get; set; }
        public IMatchmakingCallback Callback { get; set; }
        public int Score { get; set; }
        public int PiecesPlaced { get; set; }
        public int CurrentStreak { get; set; }
        public int NegativeStreak { get; set; }
        public List<DateTime> RecentPlacementTimestamps { get; set; } = new List<DateTime>();
    }
}