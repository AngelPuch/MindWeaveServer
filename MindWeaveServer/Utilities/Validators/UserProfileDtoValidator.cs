using FluentValidation;
using MindWeaveServer.Resources;
using System;
using MindWeaveServer.Contracts.DataContracts.Authentication;

namespace MindWeaveServer.Utilities.Validators
{
    public class UserProfileDtoValidator : AbstractValidator<UserProfileDto>
    {
        public UserProfileDtoValidator()
        {
            RuleFor(x => x.Username)
                .NotEmpty().WithMessage(Lang.ValidationUsernameRequired)
                .Length(3, 16).WithMessage(Lang.ValidationUsernameLength)
                .Matches("^[a-zA-Z0-9]*$").WithMessage(Lang.ValidationUsernameAlphanumeric);

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage(Lang.ValidationEmailRequired)
                .EmailAddress().WithMessage(Lang.ValidationEmailFormat);

            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage(Lang.ValidationFirstNameRequired)
                .MaximumLength(45).WithMessage(Lang.ValidationFirstNameLength)
                .Must(notHaveLeadingOrTrailingWhitespace).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace)
                .Matches("^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]*$").WithMessage(Lang.ValidationOnlyLetters);

            RuleFor(x => x.LastName)
                .MaximumLength(45).WithMessage(Lang.ValidationLastNameLength)
                .Must(notHaveLeadingOrTrailingWhitespace).When(x => !string.IsNullOrEmpty(x.LastName)).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace)
                .Matches("^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]*$").When(x => !string.IsNullOrEmpty(x.LastName)).WithMessage(Lang.ValidationOnlyLetters);

            RuleFor(x => x.DateOfBirth)
                .NotNull().WithMessage(Lang.ValidationDateOfBirthRequired)
                .Must(beAValidAge).WithMessage(Lang.ValidationDateOfBirthMinimumAge)
                .Must(beARealisticAge).WithMessage(Lang.ValidationDateOfBirthRealistic);
        }

        private static bool notHaveLeadingOrTrailingWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            return value.Trim() == value;
        }

        private static bool beAValidAge(DateTime dateOfBirth)
        {
            return dateOfBirth.Date <= DateTime.Now.Date.AddYears(-13);
        }

        private static bool beARealisticAge(DateTime dateOfBirth)
        {
            return dateOfBirth.Year > (DateTime.Now.Year - 100);
        }
    }
}