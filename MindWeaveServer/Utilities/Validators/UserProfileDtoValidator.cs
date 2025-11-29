using FluentValidation;
using MindWeaveServer.Resources;
using System;
using MindWeaveServer.Contracts.DataContracts.Authentication;

namespace MindWeaveServer.Utilities.Validators
{
    public class UserProfileDtoValidator : AbstractValidator<UserProfileDto>
    {
        private const string REGEX_ALPHANUMERIC = "^[a-zA-Z0-9]*$";
        private const string REGEX_LETTERS_AND_SPACES = "^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]*$";

        private const int USERNAME_MIN_LENGTH = 3;
        private const int USERNAME_MAX_LENGTH = 16;
        private const int NAME_MAX_LENGTH = 45;
        private const int MINIMUM_AGE_YEARS = 13;
        private const int MAXIMUM_REALISTIC_AGE_YEARS = 100;

        public UserProfileDtoValidator()
        {
            RuleFor(x => x.Username)
                .NotEmpty().WithMessage(Lang.ValidationUsernameRequired)
                .Length(USERNAME_MIN_LENGTH, USERNAME_MAX_LENGTH).WithMessage(Lang.ValidationUsernameLength)
                .Matches(REGEX_ALPHANUMERIC).WithMessage(Lang.ValidationUsernameAlphanumeric);

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage(Lang.ValidationEmailRequired)
                .EmailAddress().WithMessage(Lang.ValidationEmailFormat);

            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage(Lang.ValidationFirstNameRequired)
                .MaximumLength(NAME_MAX_LENGTH).WithMessage(Lang.ValidationFirstNameLength)
                .Must(notHaveLeadingOrTrailingWhitespace).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace)
                .Matches(REGEX_LETTERS_AND_SPACES).WithMessage(Lang.ValidationOnlyLetters);

            RuleFor(x => x.LastName)
                .MaximumLength(NAME_MAX_LENGTH).WithMessage(Lang.ValidationLastNameLength)
                .Must(notHaveLeadingOrTrailingWhitespace).When(x => !string.IsNullOrEmpty(x.LastName)).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace)
                .Matches(REGEX_LETTERS_AND_SPACES).When(x => !string.IsNullOrEmpty(x.LastName)).WithMessage(Lang.ValidationOnlyLetters);

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

        private static bool beAValidAge(DateTime dateOfBirth)
        {
            return dateOfBirth.Date <= DateTime.UtcNow.Date.AddYears(-MINIMUM_AGE_YEARS);
        }

        private static bool beARealisticAge(DateTime dateOfBirth)
        {
            return dateOfBirth.Year > (DateTime.UtcNow.Year - MAXIMUM_REALISTIC_AGE_YEARS);
        }
    }
}