using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(IMatchmakingCallback))]
    public interface IMatchmakingManager
    {
        [OperationContract]
        Task<LobbyCreationResultDto> createLobby(string hostUsername, LobbySettingsDto settingsDto);

        [OperationContract(IsOneWay = true)]
        void inviteToLobby(string inviterUsername, string invitedUsername, string lobbyId);

        [OperationContract(IsOneWay = true)]
        void joinLobby(string username, string lobbyId);

        [OperationContract(IsOneWay = true)]
        void leaveLobby(string username, string lobbyId);

        [OperationContract(IsOneWay = true)]
        void startGame(string hostUsername, string lobbyId);

        [OperationContract(IsOneWay = true)]
        void kickPlayer(string hostUsername, string playerToKickUsername, string lobbyId);
    }

    [ServiceContract]
    public interface IMatchmakingCallback
    {
        [OperationContract(IsOneWay = true)]
        void receiveLobbyInvite(string fromUsername, string lobbyId);

        [OperationContract(IsOneWay = true)]
        void updateLobbyState(LobbyStateDto lobbyStateDto);

        [OperationContract(IsOneWay = true)]
        void matchFound(string matchId, List<string> players);

        [OperationContract(IsOneWay = true)]
        void lobbyCreationFailed(string reason);

        [OperationContract(IsOneWay = true)]
        void kickedFromLobby(string reason);
    }
}