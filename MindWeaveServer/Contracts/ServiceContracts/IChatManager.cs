using System.ServiceModel;
using MindWeaveServer.Contracts.DataContracts.Chat;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(IChatCallback), SessionMode = SessionMode.Required)]
    public interface IChatManager
    {
        [OperationContract(IsOneWay = true)]
        void joinLobbyChat(string username, string lobbyId);

        [OperationContract(IsOneWay = true)]
        void leaveLobbyChat(string username, string lobbyId);

        [OperationContract(IsOneWay = true)]
        void sendLobbyMessage(string senderUsername, string lobbyId, string messageContent);



    }

    [ServiceContract]
    public interface IChatCallback
    {
        [OperationContract(IsOneWay = true)]
        void receiveLobbyMessage(ChatMessageDto messageDto);

        [OperationContract(IsOneWay = true)]
        void receiveSystemMessage(string message);
    }
}