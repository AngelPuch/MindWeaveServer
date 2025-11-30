using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface ILobbyLifecycleService
    {
        Task<LobbyCreationResultDto> createLobbyAsync(string hostUsername, LobbySettingsDto settings);

        Task joinLobbyAsync(LobbyActionContext context, IMatchmakingCallback callback);

        Task leaveLobbyAsync(LobbyActionContext context);

        void handleUserDisconnect(string username);

        Task<GuestJoinResultDto> joinLobbyAsGuestAsync(GuestJoinRequestDto request, IMatchmakingCallback callback);
    }
}