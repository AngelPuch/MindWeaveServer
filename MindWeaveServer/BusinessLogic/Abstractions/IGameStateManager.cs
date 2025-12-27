using System.Collections.Concurrent;
using System.Collections.Generic;
using MindWeaveServer.Contracts.DataContracts.Chat;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;

namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface IGameStateManager
    {
        ConcurrentDictionary<string, LobbyStateDto> ActiveLobbies { get; }

        ConcurrentDictionary<string, IMatchmakingCallback> MatchmakingCallbacks { get; }
        ConcurrentDictionary<string, HashSet<string>> GuestUsernamesInLobby { get; }

        ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> LobbyChatUsers { get; }
        ConcurrentDictionary<string, List<ChatMessageDto>> LobbyChatHistory { get; }

        ConcurrentDictionary<string, ISocialCallback> ConnectedUsers { get; }

        bool isUserConnected(string username);

        void addConnectedUser(string username, ISocialCallback callback);

        void removeConnectedUser(string username);

        ISocialCallback getUserCallback(string username);
    }
}