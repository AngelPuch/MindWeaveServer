using System.Security.Cryptography;
using System.Text;

namespace MindWeaveServer.Utilities
{
    public static class LobbyCodeGenerator
    {
        private const string ALLOWED_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789abcdefghijklmnopqrstuvwxyz";
        private const int CODE_LENGTH = 6;

        public static string generateUniqueCode()
        {
            StringBuilder codeBuilder = new StringBuilder(CODE_LENGTH);

            using (RandomNumberGenerator secureRandom = RandomNumberGenerator.Create())
            {
                for (int i = 0; i < CODE_LENGTH; i++)
                {
                    int index = SecureRandomGenerator.getSecureRandomInt(secureRandom, ALLOWED_CHARS.Length);
                    codeBuilder.Append(ALLOWED_CHARS[index]);
                }
            }

            return codeBuilder.ToString();
        }
    }
}