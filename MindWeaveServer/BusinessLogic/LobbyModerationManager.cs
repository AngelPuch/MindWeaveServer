using System.Collections.Concurrent;

namespace MindWeaveServer.BusinessLogic
{
    public class LobbyModerationState
    {
        public ConcurrentDictionary<string, string> BannedUsers { get; } = new ConcurrentDictionary<string, string>();
        public ConcurrentDictionary<string, int> Strikes { get; } = new ConcurrentDictionary<string, int>();
    }

    public class LobbyModerationManager
    {
        private readonly ConcurrentDictionary<string, LobbyModerationState> lobbies = new ConcurrentDictionary<string, LobbyModerationState>();

        public void InitializeLobby(string lobbyCode)
        {
            lobbies.TryAdd(lobbyCode, new LobbyModerationState());
        }

        public void RemoveLobby(string lobbyCode)
        {
            lobbies.TryRemove(lobbyCode, out _);
        }

        public bool IsBanned(string lobbyCode, string username)
        {
            if (lobbies.TryGetValue(lobbyCode, out var state))
            {
                return state.BannedUsers.ContainsKey(username);
            }
            return false;
        }

        public void BanUser(string lobbyCode, string username, string reason)
        {
            if (lobbies.TryGetValue(lobbyCode, out var state))
            {
                state.BannedUsers.TryAdd(username, reason);
            }
        }

        public int AddStrike(string lobbyCode, string username)
        {
            if (lobbies.TryGetValue(lobbyCode, out var state))
            {
                return state.Strikes.AddOrUpdate(username, 1, (key, count) => count + 1);
            }
            return 0;
        }
    }
}