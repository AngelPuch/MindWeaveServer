using NLog;
using System;
using System.Security.Cryptography;
namespace MindWeaveServer.Utilities
{
    public static class SecureRandomGenerator
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static int getSecureRandomInt(RandomNumberGenerator rng, int upperBound)
        {
            if (upperBound <= 0)
            {
                var ex = new ArgumentOutOfRangeException(nameof(upperBound), "Upper bound must be greater than zero.");
                logger.Error(ex, "GetSecureRandomInt called with an invalid upper bound: {0}. This indicates a programming error.", upperBound);
                throw ex;
            }

            byte[] randomBytes = new byte[4];
            uint randomValue;
            uint maxMultiple = (uint.MaxValue / (uint)upperBound) * (uint)upperBound;

            do
            {
                rng.GetBytes(randomBytes);
                randomValue = BitConverter.ToUInt32(randomBytes, 0);
            }
            while (randomValue >= maxMultiple);

            return (int)(randomValue % (uint)upperBound);
        }
    }
}
