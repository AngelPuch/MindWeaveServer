using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit;
using Moq;
using FluentValidation;
using FluentValidation.Results;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Utilities.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Utilities.Email;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Utilities.Email.Templates;
using System.Threading;

namespace MindWeaveServer.Tests.BusinessLogic
{
    public class AuthenticationLogicTests
    {
        private readonly Mock<IPlayerRepository> playerRepositoryMock;
        private readonly Mock<IEmailService> emailServiceMock;
        private readonly Mock<IPasswordService> passwordServiceMock;
        private readonly Mock<IPasswordPolicyValidator> passwordPolicyValidatorMock;
        private readonly Mock<IVerificationCodeService> verificationCodeServiceMock;
        private readonly Mock<IUserSessionManager> userSessionManagerMock;
        private readonly Mock<IValidator<UserProfileDto>> profileValidatorMock;
        private readonly Mock<IValidator<LoginDto>> loginValidatorMock;

        private readonly AuthenticationLogic authenticationLogic;

        public AuthenticationLogicTests()
        {
            playerRepositoryMock = new Mock<IPlayerRepository>();
            emailServiceMock = new Mock<IEmailService>();
            passwordServiceMock = new Mock<IPasswordService>();
            passwordPolicyValidatorMock = new Mock<IPasswordPolicyValidator>();
            verificationCodeServiceMock = new Mock<IVerificationCodeService>();
            userSessionManagerMock = new Mock<IUserSessionManager>();
            profileValidatorMock = new Mock<IValidator<UserProfileDto>>();
            loginValidatorMock = new Mock<IValidator<LoginDto>>();

            profileValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<UserProfileDto>(), default(CancellationToken)))
                .ReturnsAsync(new ValidationResult());

            loginValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), default(CancellationToken)))
                .ReturnsAsync(new ValidationResult());

            passwordPolicyValidatorMock.Setup(v => v.validate(It.IsAny<string>()))
                .Returns(new OperationResultDto { Success = true });

            authenticationLogic = new AuthenticationLogic(
                playerRepositoryMock.Object,
                emailServiceMock.Object,
                passwordServiceMock.Object,
                passwordPolicyValidatorMock.Object,
                verificationCodeServiceMock.Object,
                userSessionManagerMock.Object,
                profileValidatorMock.Object,
                loginValidatorMock.Object
            );
        }


        [Fact]
        public async Task loginAsyncNullDtoReturnsFalse()
        {
            var result = await authenticationLogic.loginAsync(null);
            Assert.False(result.OperationResult.Success);
        }

        [Fact]
        public async Task loginAsyncValidationFailureReturnsFalse()
        {
            var failureResult = new ValidationResult(new[] { new ValidationFailure("Email", "Error") });
            loginValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), default(CancellationToken)))
                .ReturnsAsync(failureResult);

            var result = await authenticationLogic.loginAsync(new LoginDto());

            Assert.False(result.OperationResult.Success);
        }

        [Fact]
        public async Task loginAsyncUserNotFoundReturnsFalse()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("test@test.com"))
                .ReturnsAsync((Player)null);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "test@test.com", Password = "123" });

            Assert.False(result.OperationResult.Success);
        }

        [Fact]
        public async Task loginAsyncPasswordMismatchReturnsFalse()
        {
            var player = new Player { email = "t@t.com", password_hash = "Hash" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("t@t.com")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Wrong", "Hash")).Returns(false);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "Wrong" });

            Assert.False(result.OperationResult.Success);
        }

        [Fact]
        public async Task loginAsyncAlreadyLoggedInReturnsSpecificCode()
        {
            var player = new Player { username = "User", password_hash = "Hash" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("t@t.com")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Pass", "Hash")).Returns(true);
            userSessionManagerMock.Setup(s => s.isUserLoggedIn("User")).Returns(true);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "Pass" });

            Assert.False(result.OperationResult.Success);
            Assert.Equal("ALREADY_LOGGED_IN", result.ResultCode);
        }

        [Fact]
        public async Task loginAsyncUnverifiedReturnsSpecificCode()
        {
            var player = new Player { is_verified = false, password_hash = "Hash" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("t@t.com")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Pass", "Hash")).Returns(true);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "Pass" });

            Assert.Equal("ACCOUNT_NOT_VERIFIED", result.ResultCode);
        }

        [Fact]
        public async Task loginAsyncSuccessAddsSession()
        {
            var player = new Player { username = "User", is_verified = true, password_hash = "Hash", idPlayer = 1 };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("t@t.com")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Pass", "Hash")).Returns(true);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "Pass" });

            Assert.True(result.OperationResult.Success);
            userSessionManagerMock.Verify(s => s.addSession("User"), Times.Once);
        }

        [Fact]
        public async Task loginAsyncSuccessReturnsProfileData()
        {
            var player = new Player { username = "User", is_verified = true, password_hash = "Hash", avatar_path = "img.png" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "P" });

            Assert.Equal("User", result.Username);
            Assert.Equal("img.png", result.AvatarPath);
        }

        [Fact]
        public void logoutRemovesSession()
        {
            authenticationLogic.logout("User");
            userSessionManagerMock.Verify(s => s.removeSession("User"), Times.Once);
        }

        [Fact]
        public void logoutNullUsernameDoesNothing()
        {
            authenticationLogic.logout(null);
            userSessionManagerMock.Verify(s => s.removeSession(It.IsAny<string>()), Times.Never);
        }


        [Fact]
        public async Task registerPlayerAsyncNullProfileReturnsError()
        {
            var result = await authenticationLogic.registerPlayerAsync(null, "pass");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task registerPlayerAsyncValidationFailReturnsError()
        {
            var failure = new ValidationResult(new[] { new ValidationFailure("Prop", "Error") });
            profileValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<UserProfileDto>(), default(CancellationToken))).ReturnsAsync(failure);

            var result = await authenticationLogic.registerPlayerAsync(new UserProfileDto(), "pass");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task registerPlayerAsyncWeakPasswordReturnsError()
        {
            passwordPolicyValidatorMock.Setup(p => p.validate("weak")).Returns(new OperationResultDto { Success = false });

            var result = await authenticationLogic.registerPlayerAsync(new UserProfileDto(), "weak");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task registerPlayerAsyncNewUserCallsAddPlayer()
        {
            var dto = new UserProfileDto { Username = "New", Email = "e@e.com", FirstName = "F" };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("New")).ReturnsAsync((Player)null);
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("e@e.com")).ReturnsAsync((Player)null);

            await authenticationLogic.registerPlayerAsync(dto, "Pass123!");

            playerRepositoryMock.Verify(r => r.addPlayer(It.IsAny<Player>()), Times.Once);
        }

        [Fact]
        public async Task registerPlayerAsyncNewUserSendsEmail()
        {
            var dto = new UserProfileDto { Username = "New", Email = "e@e.com", FirstName = "F" };
            await authenticationLogic.registerPlayerAsync(dto, "Pass");
            emailServiceMock.Verify(e => e.sendEmailAsync("e@e.com", "New", It.IsAny<IEmailTemplate>()), Times.Once);
        }

        [Fact]
        public async Task registerPlayerAsyncExistingVerifiedUserThrowsException()
        {
            var existing = new Player { is_verified = true };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Exist")).ReturnsAsync(existing);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                authenticationLogic.registerPlayerAsync(new UserProfileDto { Username = "Exist" }, "Pass"));
        }

        [Fact]
        public async Task registerPlayerAsyncExistingUnverifiedUserUpdatesPlayer()
        {
            var existing = new Player { is_verified = false, username = "U", email = "e@e.com" };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("U")).ReturnsAsync(existing);

            var dto = new UserProfileDto { Username = "U", Email = "e@e.com", FirstName = "NewName" };

            await authenticationLogic.registerPlayerAsync(dto, "Pass");

            playerRepositoryMock.Verify(r => r.updatePlayerAsync(existing), Times.Once);
            Assert.Equal("NewName", existing.first_name);
        }


        [Fact]
        public async Task verifyAccountAsyncNullArgsReturnsError()
        {
            var result = await authenticationLogic.verifyAccountAsync(null, null);
            Assert.False(result.Success);
        }

        [Fact]
        public async Task verifyAccountAsyncInvalidFormatReturnsError()
        {
            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "ABC");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task verifyAccountAsyncPlayerNotFoundReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync((Player)null);
            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "123456");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task verifyAccountAsyncAlreadyVerifiedReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new Player { is_verified = true });

            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "123456");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task verifyAccountAsyncCodeMismatchReturnsError()
        {
            var player = new Player { is_verified = false, verification_code = "654321", code_expiry_date = DateTime.UtcNow.AddHours(1) };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);

            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "123456");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task verifyAccountAsyncCodeExpiredReturnsError()
        {
            var player = new Player { is_verified = false, verification_code = "123456", code_expiry_date = DateTime.UtcNow.AddMinutes(-1) };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);

            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "123456");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task verifyAccountAsyncSuccessUpdatesPlayer()
        {
            var player = new Player { is_verified = false, verification_code = "123456", code_expiry_date = DateTime.UtcNow.AddHours(1) };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);

            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "123456");

            Assert.True(result.Success);
            Assert.True(player.is_verified);
            playerRepositoryMock.Verify(r => r.updatePlayerAsync(player), Times.Once);
        }

        [Fact]
        public async Task resendVerificationCodeAsyncNotFoundReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync((Player)null);
            var result = await authenticationLogic.resendVerificationCodeAsync("e@e.com");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task resendVerificationCodeAsyncAlreadyVerifiedReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(new Player { is_verified = true });
            var result = await authenticationLogic.resendVerificationCodeAsync("e@e.com");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task resendVerificationCodeAsyncSuccessSendsEmail()
        {
            var player = new Player { is_verified = false, email = "e@e.com", username = "U" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("e@e.com")).ReturnsAsync(player);
            verificationCodeServiceMock.Setup(v => v.generateVerificationCode()).Returns("111111");

            var result = await authenticationLogic.resendVerificationCodeAsync("e@e.com");

            Assert.True(result.Success);
            emailServiceMock.Verify(e => e.sendEmailAsync("e@e.com", "U", It.IsAny<IEmailTemplate>()), Times.Once);
        }

        [Fact]
        public async Task sendPasswordRecoveryCodeAsyncUserNotFoundReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync((Player)null);
            var result = await authenticationLogic.sendPasswordRecoveryCodeAsync("e@e.com");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task sendPasswordRecoveryCodeAsyncSuccessSendsEmail()
        {
            var player = new Player { email = "e@e.com", username = "U" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("e@e.com")).ReturnsAsync(player);

            var result = await authenticationLogic.sendPasswordRecoveryCodeAsync("e@e.com");

            Assert.True(result.Success);
            emailServiceMock.Verify(e => e.sendEmailAsync("e@e.com", "U", It.IsAny<PasswordRecoveryEmailTemplate>()), Times.Once);
        }

        [Fact]
        public async Task resetPasswordWithCodeAsyncWeakPasswordReturnsError()
        {
            passwordPolicyValidatorMock.Setup(v => v.validate("weak")).Returns(new OperationResultDto { Success = false });
            var result = await authenticationLogic.resetPasswordWithCodeAsync("e@e.com", "123456", "weak");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task resetPasswordWithCodeAsyncInvalidCodeReturnsError()
        {
            var player = new Player { verification_code = "654321", code_expiry_date = DateTime.UtcNow.AddHours(1) };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);
            passwordPolicyValidatorMock.Setup(v => v.validate(It.IsAny<string>())).Returns(new OperationResultDto { Success = true });

            var result = await authenticationLogic.resetPasswordWithCodeAsync("e@e.com", "123456", "NewPass");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task resetPasswordWithCodeAsyncSuccessUpdatesPassword()
        {
            var player = new Player { verification_code = "123456", code_expiry_date = DateTime.UtcNow.AddHours(1) };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);
            passwordPolicyValidatorMock.Setup(v => v.validate(It.IsAny<string>())).Returns(new OperationResultDto { Success = true });
            passwordServiceMock.Setup(p => p.hashPassword("NewPass")).Returns("NewHash");

            var result = await authenticationLogic.resetPasswordWithCodeAsync("e@e.com", "123456", "NewPass");

            Assert.True(result.Success);
            Assert.Equal("NewHash", player.password_hash);
        }
    }
}