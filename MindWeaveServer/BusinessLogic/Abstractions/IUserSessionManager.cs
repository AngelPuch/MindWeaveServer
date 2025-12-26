namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface IUserSessionManager
    {
        bool isUserLoggedIn(string username);
        void addSession(string username);
        void removeSession(string username);
    }
}