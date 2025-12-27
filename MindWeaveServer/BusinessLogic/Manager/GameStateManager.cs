using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Chat;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;

namespace MindWeaveServer.BusinessLogic.Manager
{
    public class GameStateManager : IGameStateManager
    {
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
        }

        public bool isUserConnected(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return false;
            }
            return ConnectedUsers.ContainsKey(username);
        }

        public void addConnectedUser(string username, ISocialCallback callback)
        {
            if (string.IsNullOrWhiteSpace(username) || callback == null)
            {
                return;
            }

            ConnectedUsers.AddOrUpdate(username, callback, (key, oldValue) => callback);
        }

        public void removeConnectedUser(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }
            ConnectedUsers.TryRemove(username, out _);
        }

        public ISocialCallback getUserCallback(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            ConnectedUsers.TryGetValue(username, out var callback);
            return callback;
        }
    }
}