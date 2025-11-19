using MindWeaveServer.Contracts.DataContracts.Social;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(ISocialCallback), SessionMode = SessionMode.Required)] 
    public interface ISocialManager
    {
        [OperationContract(IsOneWay = true)] 
        void connect(string username);

        [OperationContract(IsOneWay = true)] 
        void disconnect(string username);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<List<PlayerSearchResultDto>> searchPlayers(string requesterUsername, string query);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<OperationResultDto> sendFriendRequest(string requesterUsername, string targetUsername);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<OperationResultDto> respondToFriendRequest(string responderUsername, string requesterUsername, bool accepted);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<List<FriendDto>> getFriendsList(string username);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
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