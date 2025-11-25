using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.Contracts.DataContracts.Chat;
using NLog;

namespace MindWeaveServer.BusinessLogic
{
    public class GameStateManager : IGameStateManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public ConcurrentDictionary<string, LobbyStateDto> ActiveLobbies { get; }
        public GameSessionManager GameSessionManager { get; }

        public ConcurrentDictionary<string, IMatchmakingCallback> MatchmakingCallbacks { get; }
        public ConcurrentDictionary<string, HashSet<string>> GuestUsernamesInLobby { get; }

        public ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> LobbyChatUsers { get; }
        public ConcurrentDictionary<string, List<ChatMessageDto>> LobbyChatHistory { get; }

        public ConcurrentDictionary<string, ISocialCallback> ConnectedUsers { get; }

        public GameStateManager(GameSessionManager gameSessionManager)
        {
            ActiveLobbies = new ConcurrentDictionary<string, LobbyStateDto>(StringComparer.OrdinalIgnoreCase);
            GameSessionManager = gameSessionManager;

            MatchmakingCallbacks = new ConcurrentDictionary<string, IMatchmakingCallback>(StringComparer.OrdinalIgnoreCase);
            GuestUsernamesInLobby = new ConcurrentDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            LobbyChatUsers = new ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>>(StringComparer.OrdinalIgnoreCase);
            LobbyChatHistory = new ConcurrentDictionary<string, List<ChatMessageDto>>(StringComparer.OrdinalIgnoreCase);

            ConnectedUsers = new ConcurrentDictionary<string, ISocialCallback>(StringComparer.OrdinalIgnoreCase);

            logger.Info("GameStateManager (Singleton) initialized with full state support.");
        }
    }
}