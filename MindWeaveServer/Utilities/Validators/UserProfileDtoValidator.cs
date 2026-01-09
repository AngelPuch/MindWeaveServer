using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Shared;
using System;

namespace MindWeaveServer.Utilities.Validators
{
    public class UserProfileDtoValidator : AbstractValidator<UserProfileDto>
    {
        private const string NAME_VALIDATOR_REGEX = @"^(?=.*\p{L})[\p{L}\p{M} '\-\.]+$";
        private const string USERNAME_VALIDATOR_REGEX = @"^[a-zA-Z0-9][a-zA-Z0-9._-]*$";

        private const int USERNAME_MIN_LENGTH = 3;
        private const int USERNAME_MAX_LENGTH = 20;
        private const int NAME_MAX_LENGTH = 45;

        private const int MINIMUM_AGE_YEARS = 13;
        private const int MAXIMUM_REALISTIC_AGE_YEARS = 100;

        public UserProfileDtoValidator()
        {
            RuleFor(x => x.Username)
                .NotEmpty().WithErrorCode(MessageCodes.VALIDATION_USERNAME_REQUIRED)
                .Length(USERNAME_MIN_LENGTH, USERNAME_MAX_LENGTH).WithErrorCode(MessageCodes.VALIDATION_USERNAME_LENGTH)
                .Matches(USERNAME_VALIDATOR_REGEX).WithErrorCode(MessageCodes.VALIDATION_USERNAME_ALPHANUMERIC);

            RuleFor(x => x.Email)
                .NotEmpty().WithErrorCode(MessageCodes.VALIDATION_EMAIL_REQUIRED)
                .EmailAddress().WithErrorCode(MessageCodes.VALIDATION_EMAIL_FORMAT);

            RuleFor(x => x.FirstName)
                .NotEmpty().WithErrorCode(MessageCodes.VALIDATION_FIELDS_REQUIRED)
                .MaximumLength(NAME_MAX_LENGTH).WithErrorCode(MessageCodes.VALIDATION_NAME_LENGTH)
                .Must(notHaveLeadingOrTrailingWhitespace).WithErrorCode(MessageCodes.VALIDATION_NO_WHITESPACE)
                .Matches(NAME_VALIDATOR_REGEX).WithErrorCode(MessageCodes.VALIDATION_NAME_ONLY_LETTERS);

            RuleFor(x => x.LastName)
                .MaximumLength(NAME_MAX_LENGTH).WithErrorCode(MessageCodes.VALIDATION_NAME_LENGTH)
                .Must(notHaveLeadingOrTrailingWhitespace).When(x => !string.IsNullOrEmpty(x.LastName)).WithErrorCode(MessageCodes.VALIDATION_NO_WHITESPACE)
                .Matches(NAME_VALIDATOR_REGEX).When(x => !string.IsNullOrEmpty(x.LastName)).WithErrorCode(MessageCodes.VALIDATION_NAME_ONLY_LETTERS);

            RuleFor(x => x.DateOfBirth)
                .NotNull().WithErrorCode(MessageCodes.VALIDATION_DATE_REQUIRED)
                .Must(beAValidAge).WithErrorCode(MessageCodes.VALIDATION_AGE_MINIMUM)
                .Must(beARealisticAge).WithErrorCode(MessageCodes.VALIDATION_AGE_REALISTIC);
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
            return dateOfBirth.Date >= DateTime.UtcNow.Date.AddYears(-MAXIMUM_REALISTIC_AGE_YEARS);
        }
    }
}