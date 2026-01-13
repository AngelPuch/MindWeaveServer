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
        private const int USERNAME_MAX_LENGTH = 100; 
        private const string NAME_VALIDATOR_REGEX = @"^(?=.*\p{L})[\p{L}\p{M} '\-]{3,45}$";
        private const string USERNAME_VALIDATOR_REGEX = @"^[a-zA-Z0-9._-]+$";
        public UserProfileForEditDtoValidator()
        {
            RuleFor(x => x.FirstName)
                .NotEmpty().WithErrorCode(MessageCodes.VALIDATION_FIELDS_REQUIRED)
                .MaximumLength(NAME_MAX_LENGTH).WithErrorCode(MessageCodes.VALIDATION_NAME_LENGTH)
                .Must(notHaveLeadingOrTrailingWhitespace).WithErrorCode(MessageCodes.VALIDATION_NO_WHITESPACE)
                .Matches(NAME_VALIDATOR_REGEX).WithErrorCode(MessageCodes.VALIDATION_NAME_ONLY_LETTERS);

            RuleFor(x => x.LastName)
                .NotEmpty().WithErrorCode(MessageCodes.VALIDATION_FIELDS_REQUIRED)
                .MaximumLength(NAME_MAX_LENGTH).WithErrorCode(MessageCodes.VALIDATION_NAME_LENGTH)
                .Must(notHaveLeadingOrTrailingWhitespace).WithErrorCode(MessageCodes.VALIDATION_NO_WHITESPACE)
                .Matches(NAME_VALIDATOR_REGEX).WithErrorCode(MessageCodes.VALIDATION_NAME_ONLY_LETTERS);

            RuleFor(x => x.DateOfBirth)
                .NotNull().WithErrorCode(MessageCodes.VALIDATION_DATE_REQUIRED)
                .Must(beAValidAge).WithErrorCode(MessageCodes.VALIDATION_AGE_MINIMUM)
                .Must(beARealisticAge).WithErrorCode(MessageCodes.VALIDATION_AGE_REALISTIC);

            RuleForEach(x => x.SocialMedia).ChildRules(socialMedia =>
            {
                socialMedia.RuleFor(sm => sm.Username)
                    .NotEmpty().WithErrorCode(MessageCodes.VALIDATION_SOCIAL_USERNAME_REQUIRED)
                    .MaximumLength(USERNAME_MAX_LENGTH).WithErrorCode(MessageCodes.VALIDATION_SOCIAL_USERNAME_LENGTH) 
                    .Matches(USERNAME_VALIDATOR_REGEX).WithErrorCode(MessageCodes.VALIDATION_SOCIAL_USERNAME_INVALID);
            });
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