using MindWeaveServer.Contracts.DataContracts;
using System.Collections.Generic;
using System.ServiceModel;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(ISocialCallback))]
    public interface ISocialManager
    {
        [OperationContract]
        List<Friend> getFriendsList(string username);

        [OperationContract]
        List<FriendRequestInfo> getFriendRequests(string username);

        [OperationContract(IsOneWay = true)]
        void sendFriendRequest(string requesterUsername, string targetUsername);

        [OperationContract(IsOneWay = true)]
        void respondToFriendRequest(string requesterUsername, string targetUsername, bool accepted);

        [OperationContract(IsOneWay = true)]
        void removeFriend(string username, string friendToRemove);
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
    }
}
