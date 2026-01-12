using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using System;

namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface INotificationService
    {
        void notifyLobbyState(string username, LobbyStateDto lobbyState);
        void broadcastLobbyState(LobbyStateDto lobbyState);

        void notifyActionFailed(string username, string message);

        void notifyKicked(string username, string reason);
        void notifyLobbyCreationFailed(string username, string reason);

        void sendToUser(string username, Action<IMatchmakingCallback> action);

        void sendSocialNotification(string username, Action<ISocialCallback> action);
        void broadcastLobbyDestroyed(LobbyStateDto lobbyState, string reason);
    }
}