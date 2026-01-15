using System;
using MindWeaveServer.Contracts.DataContracts.Puzzle;

namespace MindWeaveServer.BusinessLogic.Models
{
    public class GameSessionConfiguration
    {
        public string LobbyCode { get; set; }
        public int MatchId { get; set; }
        public int PuzzleId { get; set; }
        public PuzzleDefinitionDto PuzzleDefinition { get; set; }
        public Action<string> OnSessionEndedCleanup { get; set; }
    }
}