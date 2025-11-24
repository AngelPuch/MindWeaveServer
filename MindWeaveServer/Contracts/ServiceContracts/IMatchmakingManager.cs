using MindWeaveServer.Contracts.DataContracts.Game;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Contracts.DataContracts.Shared;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(IMatchmakingCallback))]

    public interface IMatchmakingManager
    {
        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
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
        [FaultContract(typeof(ServiceFaultDto))]
        Task<GuestJoinResultDto> joinLobbyAsGuest(GuestJoinRequestDto joinRequest);

        [OperationContract(IsOneWay = true)]
        void requestPieceDrag(string lobbyCode, int pieceId);

        [OperationContract(IsOneWay = true)]
        void requestPieceMove(string lobbyCode, int pieceId, double newX, double newY);

        [OperationContract(IsOneWay = true)]
        void requestPieceDrop(string lobbyCode, int pieceId, double newX, double newY);

        [OperationContract(IsOneWay = true)]
        void requestPieceRelease(string lobbyCode, int pieceId);

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
        void onGameStarted(PuzzleDefinitionDto puzzleDefinition, int matchDurationSeconds);

        
        [OperationContract(IsOneWay = true)]
        void onPieceDragStarted(int pieceId, string username);

        [OperationContract(IsOneWay = true)]
        void onPiecePlaced(int pieceId, double correctX, double correctY, string username, int newScore, string bonusType);

      
        [OperationContract(IsOneWay = true)]
        void onPieceMoved(int pieceId, double newX, double newY, string username);

      
        [OperationContract(IsOneWay = true)]
        void onPieceDragReleased(int pieceId, string username);

        [OperationContract(IsOneWay = true)]
        void onGameEnded(MatchEndResultDto result);

        [OperationContract(IsOneWay = true)]
        void onPlayerPenalty(string username, int pointsLost, int newScore, string reason);


    }
}