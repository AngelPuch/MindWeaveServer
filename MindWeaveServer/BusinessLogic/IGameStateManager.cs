using MindWeaveServer.Contracts.DataContracts.Chat;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MindWeaveServer.BusinessLogic
{
    public interface IGameStateManager
    {
        ConcurrentDictionary<string, LobbyStateDto> ActiveLobbies { get; }

        GameSessionManager GameSessionManager { get; }

        ConcurrentDictionary<string, IMatchmakingCallback> MatchmakingCallbacks { get; }
        ConcurrentDictionary<string, HashSet<string>> GuestUsernamesInLobby { get; }

        ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> LobbyChatUsers { get; }
        ConcurrentDictionary<string, List<ChatMessageDto>> LobbyChatHistory { get; }

        ConcurrentDictionary<string, ISocialCallback> ConnectedUsers { get; }
    }
}