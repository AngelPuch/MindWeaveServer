// MindWeaveServer/Contracts/ServiceContracts/IChatManager.cs
using MindWeaveServer.Contracts.DataContracts; // Namespace for ChatMessageDto
using System.ServiceModel;
using System.Threading.Tasks; // Added for potential async operations if needed later

namespace MindWeaveServer.Contracts.ServiceContracts
{
    // Define the service contract for chat operations
    [ServiceContract(CallbackContract = typeof(IChatCallback), SessionMode = SessionMode.Required)]
    public interface IChatManager
    {
        /// <summary>
        /// Connects a user to the chat service for a specific lobby.
        /// </summary>
        [OperationContract(IsOneWay = true)]
        Task joinLobbyChat(string username, string lobbyId);

        /// <summary>
        /// Disconnects a user from the chat service for a specific lobby.
        /// </summary>
        [OperationContract(IsOneWay = true)]
        Task leaveLobbyChat(string username, string lobbyId);

        /// <summary>
        /// Sends a message to a specific lobby chat.
        /// </summary>
        [OperationContract(IsOneWay = true)]
        Task sendLobbyMessage(string senderUsername, string lobbyId, string messageContent);

        // We are keeping chat in memory for now, so no history retrieval from DB.
        // If history is needed later, add:
        // [OperationContract]
        // Task<List<ChatMessageDto>> getLobbyConversationHistory(string lobbyId);
    }

    // Define the callback contract for receiving messages
    [ServiceContract]
    public interface IChatCallback
    {
        /// <summary>
        /// Receives a chat message sent to a lobby the user is in.
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void receiveLobbyMessage(ChatMessageDto messageDto);

        // Optional: Could add callbacks for user joined/left notifications
        // [OperationContract(IsOneWay = true)]
        // void userJoinedChat(string username, string lobbyId);
        // [OperationContract(IsOneWay = true)]
        // void userLeftChat(string username, string lobbyId);
    }
}