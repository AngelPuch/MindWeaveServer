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
                .Must(notHaveLeadingOrTrailingWhitespace).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace)
                .Matches("^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]*$").WithMessage(Lang.ValidationOnlyLetters);

            RuleFor(x => x.lastName)
                .NotEmpty().WithMessage("El  eapellidos requerido.") // Asumiendo que ahora es requerido en la edición
                .MaximumLength(45).WithMessage(Lang.ValidationLastNameLength)
                .Must(notHaveLeadingOrTrailingWhitespace).When(x => !string.IsNullOrEmpty(x.lastName)).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace)
                .Matches("^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]*$").When(x => !string.IsNullOrEmpty(x.lastName)).WithMessage(Lang.ValidationOnlyLetters);

            RuleFor(x => x.dateOfBirth)
                .NotNull().WithMessage(Lang.ValidationDateOfBirthRequired)
                .Must(beAValidAge).WithMessage(Lang.ValidationDateOfBirthMinimumAge)
                .Must(beARealisticAge).WithMessage(Lang.ValidationDateOfBirthRealistic);
        }

        private bool notHaveLeadingOrTrailingWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            return value.Trim() == value;
        }

        private bool beAValidAge(System.DateTime? dateOfBirth)
        {
            if (!dateOfBirth.HasValue) return false;
            return dateOfBirth.Value.Date <= System.DateTime.Now.Date.AddYears(-13);
        }

        private bool beARealisticAge(System.DateTime? dateOfBirth)
        {
            if (!dateOfBirth.HasValue) return false;
            return dateOfBirth.Value.Year > (System.DateTime.Now.Year - 100);
        }
    }
}