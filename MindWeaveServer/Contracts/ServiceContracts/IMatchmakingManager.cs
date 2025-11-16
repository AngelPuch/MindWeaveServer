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


        /// <summary>
        /// Client notifies server they started dragging a piece.
        /// Server will lock this piece and broadcast.
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void requestPieceDrag(string lobbyCode, int pieceId);

        /// <summary>
        /// Client notifies server they dropped a piece at a new position.
        /// Server will validate the move (snap logic) and broadcast the result.
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void requestPieceDrop(string lobbyCode, int pieceId, double newX, double newY);

        /// <summary>
        /// Client notifies server they released a piece without a successful drop (e.g., drag cancel).
        /// Server will unlock this piece and broadcast.
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void requestPieceRelease(string lobbyCode, int pieceId);

    }

    [ServiceContract]
    public interface IMatchmakingCallback
    {
        /// <summary>
        /// Notifies clients in a lobby that the state has changed
        /// (e.g., player joined, settings changed).
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void updateLobbyState(LobbyStateDto lobbyStateDto);

        /// <summary>
        /// (Legacy or specific use) Notifies a client that a match has been found.
        /// </summary>

        [OperationContract(IsOneWay = true)]
        void matchFound(string lobbyCode, List<string> players, LobbySettingsDto settings, string puzzleImagePath);

        /// <summary>
        /// Notifies the host that their lobby creation request failed.
        /// </summary>

        [OperationContract(IsOneWay = true)]
        void lobbyCreationFailed(string reason);

        /// <summary>
        /// Notifies a specific client that they have been kicked from a lobby.
        /// </summary>

        [OperationContract(IsOneWay = true)]
        void kickedFromLobby(string reason);

        /// <summary>
        /// Notifies all clients in a lobby that the game is starting and sends the puzzle data.
        /// </summary>

        [OperationContract(IsOneWay = true)]
        void onGameStarted(PuzzleDefinitionDto puzzleDefinition);

        /// <summary>
        /// Notifies all clients that a piece is now held by a player.
        /// UI should show this piece as "locked" or "ghosted" for others.
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void onPieceDragStarted(int pieceId, int playerId);

        /// <summary>
        /// Confirms a successful "snap". Tells all clients to move this piece
        /// to its final, correct position and disable it.
        /// Also broadcasts the new score for the scoring player.
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void onPiecePlaced(int pieceId, double correctX, double correctY, int scoringPlayerId, int newScore);

        /// <summary>
        /// Notifies all clients where a player moved a piece (when not snapping).
        /// The client UI should update the piece's floating position.
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void onPieceMoved(int pieceId, double newX, double newY);

        /// <summary>
        /// Notifies all clients that a piece is no longer being held (e.g., invalid drop or cancel)
        /// and is now available to be dragged again.
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void onPieceDragReleased(int pieceId);


    }
}