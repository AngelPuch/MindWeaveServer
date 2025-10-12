using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities.Validators
{
    public class LoginDtoValidator : AbstractValidator<LoginDto>
    {
        public LoginDtoValidator()
        {
            // Regla: el usuario no puede estar vacío.
            RuleFor(x => x.Username)
                .NotEmpty().WithMessage(x => Lang.LoginUsernameNotEmpty);

            // Regla: la contraseña no puede estar vacía.
            RuleFor(x => x.Password)
                .NotEmpty().WithMessage(x => Lang.LoginPasswordNotEmpty);
        }
    }
}