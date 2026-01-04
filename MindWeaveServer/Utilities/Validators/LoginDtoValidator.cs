using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Shared; // Importante

namespace MindWeaveServer.Utilities.Validators
{
    public class LoginDtoValidator : AbstractValidator<LoginDto>
    {
        public LoginDtoValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage(MessageCodes.VALIDATION_EMAIL_REQUIRED);

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage(MessageCodes.VALIDATION_PASSWORD_REQUIRED);
        }
    }
}