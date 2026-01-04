using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Shared;
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
                .NotEmpty().WithErrorCode(MessageCodes.VALIDATION_FIELDS_REQUIRED)
                .MaximumLength(NAME_MAX_LENGTH).WithErrorCode(MessageCodes.VALIDATION_NAME_LENGTH)
                .Must(notHaveLeadingOrTrailingWhitespace).WithErrorCode(MessageCodes.VALIDATION_NO_WHITESPACE)
                .Matches(REGEX_LETTERS_AND_SPACES).WithErrorCode(MessageCodes.VALIDATION_NAME_ONLY_LETTERS);

            RuleFor(x => x.LastName)
                .NotEmpty().WithErrorCode(MessageCodes.VALIDATION_FIELDS_REQUIRED)
                .MaximumLength(NAME_MAX_LENGTH).WithErrorCode(MessageCodes.VALIDATION_NAME_LENGTH)
                .Must(notHaveLeadingOrTrailingWhitespace).WithErrorCode(MessageCodes.VALIDATION_NO_WHITESPACE)
                .Matches(REGEX_LETTERS_AND_SPACES).WithErrorCode(MessageCodes.VALIDATION_NAME_ONLY_LETTERS);

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