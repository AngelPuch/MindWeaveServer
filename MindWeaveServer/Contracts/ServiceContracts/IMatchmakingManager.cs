using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
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

        [OperationContract(IsOneWay = true)] 
        void changeDifficulty(string hostUsername, string lobbyId, int newDifficultyId);

        [OperationContract(IsOneWay = true)]
        void inviteGuestByEmail(GuestInvitationDto invitationData);

        [OperationContract]
        Task<GuestJoinResultDto> joinLobbyAsGuest(GuestJoinRequestDto joinRequest);

        [OperationContract]
        Task sendPiecePlacedAsync(int pieceId);

    }

    [ServiceContract]
    public interface IMatchmakingCallback
    {
        [OperationContract(IsOneWay = true)]
        void updateLobbyState(LobbyStateDto lobbyStateDto);

        [OperationContract(IsOneWay = true)]
        void matchFound(string lobbyCode, List<string> players, LobbySettingsDto settings, string puzzleImagePath);

        [OperationContract(IsOneWay = true)]
        void lobbyCreationFailed(string reason);

        [OperationContract(IsOneWay = true)]
        void kickedFromLobby(string reason);

        [OperationContract(IsOneWay = true)]
        void onGameStarted(PuzzleDefinitionDto puzzleDefinition);


    }
}