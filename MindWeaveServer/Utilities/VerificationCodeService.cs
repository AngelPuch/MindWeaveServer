using MindWeaveServer.Utilities.Abstractions;
using System;

namespace MindWeaveServer.Utilities
{
    public class VerificationCodeService : IVerificationCodeService
    {
        private static readonly Random random = new Random();
        private const int VERIFICATION_CODE_EXPIRY_MINUTES = 5;
        private const int CODE_MIN_VALUE = 100000;
        private const int CODE_MAX_VALUE = 999999;
        private const string CODE_FORMAT = "D6";

        public string generateVerificationCode()
        {
            lock (random)
            {
                return random.Next(CODE_MIN_VALUE, CODE_MAX_VALUE).ToString(CODE_FORMAT);
            }
        }

        public DateTime getVerificationExpiryTime()
        {
            return DateTime.UtcNow.AddMinutes(VERIFICATION_CODE_EXPIRY_MINUTES);
        }
    }
}