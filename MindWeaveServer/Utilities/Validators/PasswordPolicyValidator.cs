using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Abstractions;
using System.Linq;
using MindWeaveServer.Contracts.DataContracts.Shared;
using NLog;

namespace MindWeaveServer.Utilities.Validators
{
    public class PasswordPolicyValidator : IPasswordPolicyValidator
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

       
        public OperationResultDto validate(string password)
        {
            logger.Debug("Validating password policy.");

            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                logger.Warn("Password validation failed: Length is less than 8 characters or null/whitespace.");
                return new OperationResultDto { Success = false, Message = Lang.ValidationPasswordLength };
            }

            if (password.Any(char.IsWhiteSpace))
            {
                logger.Warn("Password validation failed: Contains whitespace characters.");
                return new OperationResultDto { Success = false, Message = Lang.ValidationPasswordNoSpaces };
            }

            bool hasUpper = password.Any(char.IsUpper);
            bool hasLower = password.Any(char.IsLower);
            bool hasDigit = password.Any(char.IsDigit);

            if (!hasUpper || !hasLower || !hasDigit)
            {
                logger.Warn("Password validation failed: Complexity requirement not met (Upper={HasUpper}, Lower={HasLower}, Digit={HasDigit}).", hasUpper, hasLower, hasDigit);
                return new OperationResultDto { Success = false, Message = Lang.ValidationPasswordComplexity };
            }

            return new OperationResultDto { Success = true };
        }
    }
}