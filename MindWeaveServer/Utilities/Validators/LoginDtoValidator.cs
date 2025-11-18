using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities.Validators
{
    public class LoginDtoValidator : AbstractValidator<LoginDto>
    {
        public LoginDtoValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage(x => Lang.ValidationEmailRequired);

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage(x => Lang.ValidationPasswordRequired);
        }
    }
}