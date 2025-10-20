namespace MindWeaveServer.Utilities.Abstractions
{
    public interface IPasswordService
    {
        string hashPassword(string password);
        bool verifyPassword(string password, string storedHash);
    }
}