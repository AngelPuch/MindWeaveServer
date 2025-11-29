using System.Collections.Concurrent;

namespace MindWeaveServer.Services
{
    public class LobbyModerationState
    {
        public ConcurrentDictionary<string, string> BannedUsers { get; } = new ConcurrentDictionary<string, string>();
        public ConcurrentDictionary<string, int> Strikes { get; } = new ConcurrentDictionary<string, int>();
    }

    public class LobbyModerationManager
    {
        private readonly ConcurrentDictionary<string, LobbyModerationState> lobbies = new ConcurrentDictionary<string, LobbyModerationState>();

        public void initializeLobby(string lobbyCode)
        {
            lobbies.TryAdd(lobbyCode, new LobbyModerationState());
        }

        public bool isBanned(string lobbyCode, string username)
        {
            if (lobbies.TryGetValue(lobbyCode, out var state))
            {
                return state.BannedUsers.ContainsKey(username);
            }
            return false;
        }

        public int addStrike(string lobbyCode, string username)
        {
            if (lobbies.TryGetValue(lobbyCode, out var state))
            {
                return state.Strikes.AddOrUpdate(username, 1, (key, count) => count + 1);
            }
            return 0;
        }
    }
}