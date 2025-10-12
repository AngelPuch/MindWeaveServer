using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities.Validators
{
    public class LoginDtoValidator : AbstractValidator<LoginDto>
    {
        public LoginDtoValidator()
        {
            RuleFor(x => x.email)
                .NotEmpty().WithMessage(x => Lang.ValidationEmailRequired);

            RuleFor(x => x.password)
                .NotEmpty().WithMessage(x => Lang.ValidationPasswordRequired);
        }
    }
}