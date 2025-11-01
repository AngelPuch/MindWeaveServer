using MindWeaveServer.Utilities.Abstractions;
using System;
using System.Security.Cryptography;

namespace MindWeaveServer.Utilities
{
    public class VerificationCodeService : IVerificationCodeService
    {
        private const int VERIFICATION_CODE_EXPIRY_MINUTES = 5;
        private const int CODE_MIN_VALUE = 100000;
        private const int CODE_MAX_VALUE = 1000000;
        private const string CODE_FORMAT = "D6";

        public string generateVerificationCode()
        {
            using (RandomNumberGenerator secureRandom = RandomNumberGenerator.Create())
            {
                int range = CODE_MAX_VALUE - CODE_MIN_VALUE;
                int randomOffset = SecureRandomGenerator.getSecureRandomInt(secureRandom, range);
                int code = CODE_MIN_VALUE + randomOffset;

                return code.ToString(CODE_FORMAT);
            }
        }

        public DateTime getVerificationExpiryTime()
        {
            return DateTime.UtcNow.AddMinutes(VERIFICATION_CODE_EXPIRY_MINUTES);
        }
    }
}