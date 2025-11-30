using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;

namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface ILobbyValidationService
    {
        ValidationResult canCreateLobby(string hostUsername);

        ValidationResult canJoinLobby(LobbyStateDto lobby, string username, string inputCode);

        ValidationResult canInvitePlayer(LobbyStateDto lobby, string targetUsername);

        ValidationResult canStartGame(LobbyStateDto lobby, string requestUsername);

        ValidationResult canKickPlayer(LobbyStateDto lobby, string hostUsername, string targetUsername);
    }
}