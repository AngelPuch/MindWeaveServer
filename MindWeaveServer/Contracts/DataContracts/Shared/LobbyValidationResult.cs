using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.BusinessLogic.Models
{
    
    public class LobbyValidationResult
    {
        public bool IsSuccess { get; set; }
        public ServiceErrorType ErrorType { get; set; }
        public string ErrorMessage { get; set; } 
    }
}