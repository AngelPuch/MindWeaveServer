using System;
using BCrypt.Net;
using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities
{
    internal static class PasswordHasher
    {
        public static string hashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentNullException(nameof(password), Lang.ValidationPasswordRequired);
            }
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        public static bool verifyPassword(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, storedHash);
            }
            catch (SaltParseException)
            {
                return false;
            }
        }
    }
}
