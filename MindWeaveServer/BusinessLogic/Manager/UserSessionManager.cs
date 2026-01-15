using System.Collections.Concurrent;
using MindWeaveServer.BusinessLogic.Abstractions;
using NLog;

namespace MindWeaveServer.BusinessLogic.Manager
{
    public class UserSessionManager : IUserSessionManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ConcurrentDictionary<string, bool> activeSessions = new ConcurrentDictionary<string, bool>();

        public bool isUserLoggedIn(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return false;
            }
            return activeSessions.ContainsKey(username.Trim().ToLower());
        }

        public void addSession(string username)
        {
            if (!string.IsNullOrWhiteSpace(username))
            {
                string key = username.Trim().ToLower();
                activeSessions.TryAdd(key, true);
                logger.Info("Session registered");
            }
        }

        public void removeSession(string username)
        { 
            if (!string.IsNullOrWhiteSpace(username))
            {
                string key = username.Trim().ToLower();
                bool removed = activeSessions.TryRemove(key, out _);
                if (removed)
                {
                    logger.Info("Session removed");
                }
            }
        }
    }
}