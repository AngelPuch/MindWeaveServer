using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Abstractions;
using System.Linq;
using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.Utilities.Validators
{
    public class PasswordPolicyValidator : IPasswordPolicyValidator
    {
        public OperationResultDto validate(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                return new OperationResultDto { success = false, message = Lang.ValidationPasswordLength };
            }
            if (password.Any(char.IsWhiteSpace))
            {
                return new OperationResultDto { success = false, message = Lang.ValidationPasswordNoSpaces };
            }
            if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            {
                return new OperationResultDto { success = false, message = Lang.ValidationPasswordComplexity };
            }

            return new OperationResultDto { success = true };
        }
    }
}