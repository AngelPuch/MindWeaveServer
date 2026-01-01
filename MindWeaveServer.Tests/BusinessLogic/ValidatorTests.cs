using Xunit;
using MindWeaveServer.Utilities.Validators;
using MindWeaveServer.Contracts.DataContracts.Authentication;

namespace MindWeaveServer.Tests.Utilities
{
    public class ValidatorTests
    {
        [Fact]
        public void passwordValidatorTooShortReturnsFalse()
        {
            var validator = new PasswordPolicyValidator();
            var result = validator.validate("Short1!");
            Assert.False(result.Success);
        }

        [Fact]
        public void passwordValidatorNoUpperCaseReturnsFalse()
        {
            var validator = new PasswordPolicyValidator();
            var result = validator.validate("password123!");
            Assert.False(result.Success);
        }

        [Fact]
        public void passwordValidatorNoNumberReturnsFalse()
        {
            var validator = new PasswordPolicyValidator();
            var result = validator.validate("Password!");
            Assert.False(result.Success);
        }

        [Fact]
        public void passwordValidatorNoSpecialCharReturnsFalse()
        {
            var validator = new PasswordPolicyValidator();
            var result = validator.validate("Password123");
            Assert.False(result.Success);
        }

        [Fact]
        public void passwordValidatorValidPasswordReturnsTrue()
        {
            var validator = new PasswordPolicyValidator();
            var result = validator.validate("StrongPass1!");
            Assert.True(result.Success);
        }

        [Fact]
        public void userProfileValidatorInvalidEmailReturnsFalse()
        {
            var validator = new UserProfileDtoValidator();
            var dto = new UserProfileDto { Email = "bad-email", Username = "User" };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void userProfileValidatorInvalidUsernameTooShortReturnsFalse()
        {
            var validator = new UserProfileDtoValidator();
            var dto = new UserProfileDto { Email = "test@test.com", Username = "ab" };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void userProfileValidatorInvalidUsernameTooLongReturnsFalse()
        {
            var validator = new UserProfileDtoValidator();
            var dto = new UserProfileDto { Email = "test@test.com", Username = "thisusernameistoolongfor" };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void userProfileValidatorInvalidUsernameWithSpecialCharsReturnsFalse()
        {
            var validator = new UserProfileDtoValidator();
            var dto = new UserProfileDto { Email = "test@test.com", Username = "User@123" };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void userProfileValidatorMinorAgeReturnsFalse()
        {
            var validator = new UserProfileDtoValidator();
            var dto = new UserProfileDto
            {
                Email = "test@test.com",
                Username = "ValidUser",
                FirstName = "Juan",
                DateOfBirth = System.DateTime.UtcNow.AddYears(-12)
            };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void userProfileValidatorUnrealisticAgeReturnsFalse()
        {
            var validator = new UserProfileDtoValidator();
            var dto = new UserProfileDto
            {
                Email = "test@test.com",
                Username = "ValidUser",
                FirstName = "Juan",
                DateOfBirth = System.DateTime.UtcNow.AddYears(-101)
            };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void userProfileValidatorValidDtoReturnsTrue()
        {
            var validator = new UserProfileDtoValidator();
            var dto = new UserProfileDto
            {
                Email = "good@test.com",
                Username = "User",
                FirstName = "Juan",
                DateOfBirth = System.DateTime.UtcNow.AddYears(-20)
            };

            var result = validator.Validate(dto);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void loginValidatorNullFieldsReturnsFalse()
        {
            var validator = new LoginDtoValidator();
            var dto = new LoginDto { Email = null, Password = null };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void loginValidatorEmptyFieldsReturnsFalse()
        {
            var validator = new LoginDtoValidator();
            var dto = new LoginDto { Email = "", Password = "" };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void loginValidatorValidDtoReturnsTrue()
        {
            var validator = new LoginDtoValidator();
            var dto = new LoginDto { Email = "valid@test.com", Password = "StrongPass1!" };

            var result = validator.Validate(dto);

            Assert.True(result.IsValid);
        }
    }
}