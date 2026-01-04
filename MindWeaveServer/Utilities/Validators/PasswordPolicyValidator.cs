using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Utilities.Abstractions;
using NLog;
using System.Linq;

namespace MindWeaveServer.Utilities.Validators
{
    public class PasswordPolicyValidator : IPasswordPolicyValidator
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private const int MIN_PASSWORD_LENGTH = 8;
        public OperationResultDto validate(string password)
        {
            logger.Debug("Validating password policy.");

            if (string.IsNullOrWhiteSpace(password) || password.Length < MIN_PASSWORD_LENGTH)
            {
                logger.Warn("Password validation failed: Length is less than 8 characters or null/whitespace.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_PASSWORD_TOO_SHORT
                };
            }

            if (password.Any(char.IsWhiteSpace))
            {
                logger.Warn("Password validation failed: Contains whitespace characters.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_PASSWORD_NO_SPACES
                };
            }

            bool hasUpper = password.Any(char.IsUpper);
            bool hasLower = password.Any(char.IsLower);
            bool hasDigit = password.Any(char.IsDigit);
            bool hasSpecialChar = password.Any(ch => !char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch));

            if (!hasUpper || !hasLower || !hasDigit || !hasSpecialChar)
            {
                logger.Warn("Password validation failed: Complexity requirement not met.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_PASSWORD_TOO_WEAK
                };
            }

            return new OperationResultDto { Success = true };
        }
    }
}