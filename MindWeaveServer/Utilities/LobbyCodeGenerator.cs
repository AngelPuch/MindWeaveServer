// MindWeaveServer/Utilities/LobbyCodeGenerator.cs
using System;
using System.Linq;
using System.Text;

namespace MindWeaveServer.Utilities
{
    public static class LobbyCodeGenerator
    {
        private static readonly Random random = new Random();
        // Caracteres permitidos para el código (excluimos O, 0, I, 1 para evitar confusión)
        private const string CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789abcdefghijklmnopqrstuvwxyz";
        private const int CODE_LENGTH = 6;

        public static string generateUniqueCode()
        {
            // Genera un código aleatorio de CODE_LENGTH caracteres
            StringBuilder codeBuilder = new StringBuilder(CODE_LENGTH);
            lock (random) // Asegura thread-safety si se llama concurrentemente
            {
                for (int i = 0; i < CODE_LENGTH; i++)
                {
                    codeBuilder.Append(CHARS[random.Next(CHARS.Length)]);
                }
            }
            return codeBuilder.ToString();
            // NOTA: En un sistema real, deberías verificar si este código ya existe
            // en la base de datos o en la memoria (lobbies activos) y regenerar si es necesario.
            // Por simplicidad, omitimos esa verificación por ahora.
        }
    }
}