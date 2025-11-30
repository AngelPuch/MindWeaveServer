using MindWeaveServer.BusinessLogic.Models;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface ILobbyInteractionService
    {
        Task invitePlayerAsync(LobbyActionContext context);

        Task kickPlayerAsync(LobbyActionContext context);

        Task startGameAsync(LobbyActionContext context);

        Task changeDifficultyAsync(LobbyActionContext context, int newDifficultyId);

        Task inviteGuestByEmailAsync(string inviterUsername, string lobbyCode, string guestEmail);

    }
}