using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities.Validators
{
    public class UserProfileForEditDtoValidator : AbstractValidator<UserProfileForEditDto>
    {
        public UserProfileForEditDtoValidator()
        {
            RuleFor(x => x.firstName)
                .NotEmpty().WithMessage(Lang.ValidationFirstNameRequired)
                .MaximumLength(45).WithMessage(Lang.ValidationFirstNameLength)
                .Must(NotHaveLeadingOrTrailingWhitespace).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace)
                .Matches("^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]*$").WithMessage(Lang.ValidationOnlyLetters);

            RuleFor(x => x.lastName)
                .NotEmpty().WithMessage("El apellido es requerido.") // Asumiendo que ahora es requerido en la edición
                .MaximumLength(45).WithMessage(Lang.ValidationLastNameLength)
                .Must(NotHaveLeadingOrTrailingWhitespace).When(x => !string.IsNullOrEmpty(x.lastName)).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace)
                .Matches("^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]*$").When(x => !string.IsNullOrEmpty(x.lastName)).WithMessage(Lang.ValidationOnlyLetters);

            RuleFor(x => x.dateOfBirth)
                .NotNull().WithMessage(Lang.ValidationDateOfBirthRequired)
                .Must(BeAValidAge).WithMessage(Lang.ValidationDateOfBirthMinimumAge)
                .Must(BeARealisticAge).WithMessage(Lang.ValidationDateOfBirthRealistic);
        }

        private bool NotHaveLeadingOrTrailingWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            return value.Trim() == value;
        }

        private bool BeAValidAge(System.DateTime? dateOfBirth)
        {
            if (!dateOfBirth.HasValue) return false;
            return dateOfBirth.Value.Date <= System.DateTime.Now.Date.AddYears(-13);
        }

        private bool BeARealisticAge(System.DateTime? dateOfBirth)
        {
            if (!dateOfBirth.HasValue) return false;
            return dateOfBirth.Value.Year > (System.DateTime.Now.Year - 100);
        }
    }
}