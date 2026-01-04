using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Shared;
using System;

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
                .NotEmpty().WithMessage(MessageCodes.VALIDATION_USERNAME_REQUIRED)
                .Length(USERNAME_MIN_LENGTH, USERNAME_MAX_LENGTH).WithMessage(MessageCodes.VALIDATION_USERNAME_LENGTH)
                .Matches(REGEX_ALPHANUMERIC).WithMessage(MessageCodes.VALIDATION_USERNAME_ALPHANUMERIC);

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage(MessageCodes.VALIDATION_EMAIL_REQUIRED)
                .EmailAddress().WithMessage(MessageCodes.VALIDATION_EMAIL_FORMAT);

            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage(MessageCodes.VALIDATION_FIELDS_REQUIRED)
                .MaximumLength(NAME_MAX_LENGTH).WithMessage(MessageCodes.VALIDATION_NAME_LENGTH)
                .Must(notHaveLeadingOrTrailingWhitespace).WithMessage(MessageCodes.VALIDATION_NO_WHITESPACE)
                .Matches(REGEX_LETTERS_AND_SPACES).WithMessage(MessageCodes.VALIDATION_NAME_ONLY_LETTERS);

            RuleFor(x => x.LastName)
                .MaximumLength(NAME_MAX_LENGTH).WithMessage(MessageCodes.VALIDATION_NAME_LENGTH)
                .Must(notHaveLeadingOrTrailingWhitespace).When(x => !string.IsNullOrEmpty(x.LastName)).WithMessage(MessageCodes.VALIDATION_NO_WHITESPACE)
                .Matches(REGEX_LETTERS_AND_SPACES).When(x => !string.IsNullOrEmpty(x.LastName)).WithMessage(MessageCodes.VALIDATION_NAME_ONLY_LETTERS);

            RuleFor(x => x.DateOfBirth)
                .NotNull().WithMessage(MessageCodes.VALIDATION_DATE_REQUIRED)
                .Must(beAValidAge).WithMessage(MessageCodes.VALIDATION_AGE_MINIMUM)
                .Must(beARealisticAge).WithMessage(MessageCodes.VALIDATION_AGE_REALISTIC);
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