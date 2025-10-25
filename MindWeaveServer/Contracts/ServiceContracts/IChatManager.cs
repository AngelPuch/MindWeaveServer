using System.ServiceModel;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Chat; 

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(IChatCallback), SessionMode = SessionMode.Required)]
    public interface IChatManager
    {
        [OperationContract(IsOneWay = true)]
        Task joinLobbyChat(string username, string lobbyId);

        [OperationContract(IsOneWay = true)]
        Task leaveLobbyChat(string username, string lobbyId);

        [OperationContract(IsOneWay = true)]
        Task sendLobbyMessage(string senderUsername, string lobbyId, string messageContent);

    }

    [ServiceContract]
    public interface IChatCallback
    {
        [OperationContract(IsOneWay = true)]
        void receiveLobbyMessage(ChatMessageDto messageDto);
        }
}