using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Social;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(ISocialCallback), SessionMode = SessionMode.Required)] 
    public interface ISocialManager
    {
        [OperationContract(IsOneWay = true)] 
        Task connect(string username);

        [OperationContract(IsOneWay = true)] 
        Task disconnect(string username);

        [OperationContract]
        Task<List<PlayerSearchResultDto>> searchPlayers(string requesterUsername, string query);

        [OperationContract]
        Task<OperationResultDto> sendFriendRequest(string requesterUsername, string targetUsername);

        [OperationContract]
        Task<OperationResultDto> respondToFriendRequest(string responderUsername, string requesterUsername, bool accepted);

        [OperationContract]
        Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername);

        [OperationContract]
        Task<List<FriendDto>> getFriendsList(string username);

        [OperationContract]
        Task<List<FriendRequestInfoDto>> getFriendRequests(string username);
    }

    
    [ServiceContract]
    public interface ISocialCallback
    {
        [OperationContract(IsOneWay = true)]
        void notifyFriendRequest(string fromUsername);

        [OperationContract(IsOneWay = true)]
        void notifyFriendResponse(string fromUsername, bool accepted);

        [OperationContract(IsOneWay = true)]
        void notifyFriendStatusChanged(string friendUsername, bool isOnline);

        [OperationContract(IsOneWay = true)]
        void notifyLobbyInvite(string fromUsername, string lobbyId);
    }
}