using MindWeaveServer.BusinessLogic; // For PlayerSearchResultDto
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Social;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks; // Added for Task

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(ISocialCallback))]
    public interface ISocialManager
    {
        // Search (New) - Returns results directly
        [OperationContract]
        Task<List<PlayerSearchResultDto>> searchPlayers(string requesterUsername, string query);

        // Send Request - Returns success/failure message
        [OperationContract]
        Task<OperationResultDto> sendFriendRequest(string requesterUsername, string targetUsername);

        // Respond Request - Returns success/failure message
        [OperationContract]
        Task<OperationResultDto> respondToFriendRequest(string responderUsername, string requesterUsername, bool accepted);

        // Remove Friend - Returns success/failure message
        [OperationContract]
        Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername);

        // Get Lists - Return lists directly
        [OperationContract]
        Task<List<FriendDto>> getFriendsList(string username);

        [OperationContract]
        Task<List<FriendRequestInfoDto>> getFriendRequests(string username); // Renamed for consistency

    }

    [ServiceContract]
    public interface ISocialCallback
    {
        [OperationContract(IsOneWay = true)]
        void notifyFriendRequest(string fromUsername);

        [OperationContract(IsOneWay = true)]
        void notifyFriendResponse(string fromUsername, bool accepted); // Response received

        [OperationContract(IsOneWay = true)]
        void notifyFriendStatusChanged(string friendUsername, bool isOnline);

        // Optional: Notify when removed by a friend
        // [OperationContract(IsOneWay = true)]
        // void notifyFriendRemoved(string byUsername);

        // Optional: Notify when friend list updated (new friend accepted)
        // [OperationContract(IsOneWay = true)]
        // void notifyFriendListUpdated(); // Or pass updated list/friend details
    }
}