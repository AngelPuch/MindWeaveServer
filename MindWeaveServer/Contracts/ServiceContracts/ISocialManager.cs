// MindWeaveServer/Contracts/ServiceContracts/ISocialManager.cs
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Social;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    // El CallbackContract no cambia
    [ServiceContract(CallbackContract = typeof(ISocialCallback), SessionMode = SessionMode.Required)] // *** AÑADIR SessionMode.Required ***
    public interface ISocialManager
    {
        // *** NUEVO: Métodos para manejar la conexión/desconexión ***
        [OperationContract(IsOneWay = true)] // Es OneWay porque el cliente no necesita esperar respuesta inmediata
        Task connect(string username);

        [OperationContract(IsOneWay = true)] // Es OneWay para desconexión rápida
        Task disconnect(string username);

        // --- Métodos existentes (sin cambios en la firma) ---
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

    // ISocialCallback no necesita cambios
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