using System;
using System.Text;

namespace MindWeaveServer.Utilities
{
    public static class LobbyCodeGenerator
    {
        private static readonly Random random = new Random();
        private const string CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789abcdefghijklmnopqrstuvwxyz";
        private const int CODE_LENGTH = 6;

        public static string generateUniqueCode()
        {
            StringBuilder codeBuilder = new StringBuilder(CODE_LENGTH);
            lock (random)
            {
                for (int i = 0; i < CODE_LENGTH; i++)
                {
                    codeBuilder.Append(CHARS[random.Next(CHARS.Length)]);
                }
            }
            return codeBuilder.ToString();
        }
    }
}