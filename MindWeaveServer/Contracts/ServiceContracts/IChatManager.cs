using MindWeaveServer.Contracts.DataContracts;
using System.Collections.Generic;
using System.ServiceModel;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(IChatCallback))]
    public interface IChatManager
    {
        [OperationContract(IsOneWay = true)]
        void sendLobbyMessage(string senderUsername, string lobbyId, string message);

        [OperationContract]
        List<ChatMessageDto> getLobbyConversationHistory(string lobbyId, int pageNumber, int pageSize);
    }

    [ServiceContract]
    public interface IChatCallback
    {
        [OperationContract(IsOneWay = true)]
        void receiveLobbyMessage(ChatMessageDto messageDto);
    }
}
