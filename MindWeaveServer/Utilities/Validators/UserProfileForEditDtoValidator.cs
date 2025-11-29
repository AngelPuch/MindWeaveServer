using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Resources;
using System;

namespace MindWeaveServer.Utilities.Validators
{
    public class UserProfileForEditDtoValidator : AbstractValidator<UserProfileForEditDto>
    {
        private const int NAME_MAX_LENGTH = 45;
        private const int MINIMUM_AGE_YEARS = 13;
        private const int MAX_REALISTIC_AGE_YEARS = 100;
        private const string REGEX_LETTERS_AND_SPACES = "^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]*$";

        public UserProfileForEditDtoValidator()
        {
            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage(Lang.ValidationFirstNameRequired)
                .MaximumLength(NAME_MAX_LENGTH).WithMessage(Lang.ValidationFirstNameLength)
                .Must(notHaveLeadingOrTrailingWhitespace).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace)
                .Matches(REGEX_LETTERS_AND_SPACES).WithMessage(Lang.ValidationOnlyLetters);

            RuleFor(x => x.LastName)
                .NotEmpty().WithMessage(Lang.ValidationLastNameRequired)
                .MaximumLength(NAME_MAX_LENGTH).WithMessage(Lang.ValidationLastNameLength)
                .Must(notHaveLeadingOrTrailingWhitespace).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace)
                .Matches(REGEX_LETTERS_AND_SPACES).WithMessage(Lang.ValidationOnlyLetters);

            RuleFor(x => x.DateOfBirth)
                .NotNull().WithMessage(Lang.ValidationDateOfBirthRequired)
                .Must(beAValidAge).WithMessage(Lang.ValidationDateOfBirthMinimumAge)
                .Must(beARealisticAge).WithMessage(Lang.ValidationDateOfBirthRealistic);
        }

        private static bool notHaveLeadingOrTrailingWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            return value.Trim().Length == value.Length;
        }

        private static bool beAValidAge(DateTime? dateOfBirth)
        {
            if (!dateOfBirth.HasValue) return false;
            return dateOfBirth.Value.Date <= DateTime.UtcNow.Date.AddYears(-MINIMUM_AGE_YEARS);
        }

        private static bool beARealisticAge(DateTime? dateOfBirth)
        {
            if (!dateOfBirth.HasValue) return false;
            return dateOfBirth.Value.Year > (DateTime.UtcNow.Year - MAX_REALISTIC_AGE_YEARS);
        }
    }
}