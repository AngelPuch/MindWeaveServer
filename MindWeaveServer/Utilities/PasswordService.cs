using MindWeaveServer.Utilities.Abstractions;

namespace MindWeaveServer.Utilities
{
    public class PasswordService : IPasswordService
    {
        public string hashPassword(string password)
        {
            return PasswordHasher.hashPassword(password);
        }

        public bool verifyPassword(string password, string storedHash)
        {
            return PasswordHasher.verifyPassword(password, storedHash);
        }
    }
}