using Xunit;
using MindWeaveServer.Utilities.Validators;
using MindWeaveServer.Contracts.DataContracts.Authentication;

namespace MindWeaveServer.Tests.Utilities
{
    public class ValidatorTests
    {
        [Fact]
        public void PasswordValidator_TooShort_ReturnsFalse()
        {
            var validator = new PasswordPolicyValidator();
            var result = validator.validate("Short1!");
            Assert.False(result.Success);
        }

        [Fact]
        public void PasswordValidator_NoUpperCase_ReturnsFalse()
        {
            var validator = new PasswordPolicyValidator();
            var result = validator.validate("password123!");
            Assert.False(result.Success);
        }

        [Fact]
        public void PasswordValidator_NoNumber_ReturnsFalse()
        {
            var validator = new PasswordPolicyValidator();
            var result = validator.validate("Password!");
            Assert.False(result.Success);
        }

        [Fact]
        public void PasswordValidator_NoSpecialChar_ReturnsFalse()
        {
            var validator = new PasswordPolicyValidator();
            var result = validator.validate("Password123");
            Assert.False(result.Success);
        }

        [Fact]
        public void PasswordValidator_ValidPassword_ReturnsTrue()
        {
            var validator = new PasswordPolicyValidator();
            var result = validator.validate("StrongPass1!");
            Assert.True(result.Success);
        }

        [Fact]
        public void UserProfileValidator_InvalidEmail_ReturnsFalse()
        {
            var validator = new UserProfileDtoValidator();
            var dto = new UserProfileDto { Email = "bad-email", Username = "User" };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void UserProfileValidator_UsernameTooShort_ReturnsFalse()
        {
            var validator = new UserProfileDtoValidator();
            var dto = new UserProfileDto { Email = "test@test.com", Username = "ab" };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void UserProfileValidator_UsernameTooLong_ReturnsFalse()
        {
            var validator = new UserProfileDtoValidator();
            var dto = new UserProfileDto { Email = "test@test.com", Username = "thisusernameistoolongfor" };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void UserProfileValidator_UsernameWithSpecialChars_ReturnsFalse()
        {
            var validator = new UserProfileDtoValidator();
            var dto = new UserProfileDto { Email = "test@test.com", Username = "User@123" };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void UserProfileValidator_MinorAge_ReturnsFalse()
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
        public void UserProfileValidator_UnrealisticAge_ReturnsFalse()
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
        public void UserProfileValidator_ValidDto_ReturnsTrue()
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
        public void LoginValidator_NullFields_ReturnsFalse()
        {
            var validator = new LoginDtoValidator();
            var dto = new LoginDto { Email = null, Password = null };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void LoginValidator_EmptyFields_ReturnsFalse()
        {
            var validator = new LoginDtoValidator();
            var dto = new LoginDto { Email = "", Password = "" };

            var result = validator.Validate(dto);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void LoginValidator_ValidDto_ReturnsTrue()
        {
            var validator = new LoginDtoValidator();
            var dto = new LoginDto { Email = "valid@test.com", Password = "StrongPass1!" };

            var result = validator.Validate(dto);

            Assert.True(result.IsValid);
        }
    }
}