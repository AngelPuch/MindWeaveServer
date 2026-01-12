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
        public async Task LoginAsync_NullDto_ReturnsFalse()
        {
            var result = await authenticationLogic.loginAsync(null);
            Assert.False(result.OperationResult.Success);
        }

        [Fact]
        public async Task LoginAsync_ValidationFailure_ReturnsFalse()
        {
            var failureResult = new ValidationResult(new[] { new ValidationFailure("Email", "Error") });
            loginValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), default(CancellationToken)))
                .ReturnsAsync(failureResult);

            var result = await authenticationLogic.loginAsync(new LoginDto());

            Assert.False(result.OperationResult.Success);
        }

        [Fact]
        public async Task LoginAsync_UserNotFound_ReturnsFalse()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("test@test.com"))
                .ReturnsAsync((Player)null!);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "test@test.com", Password = "123" });

            Assert.False(result.OperationResult.Success);
        }

        [Fact]
        public async Task LoginAsync_PasswordMismatch_ReturnsFalse()
        {
            var player = new Player { email = "t@t.com", password_hash = "Hash" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("t@t.com")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Wrong", "Hash")).Returns(false);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "Wrong" });

            Assert.False(result.OperationResult.Success);
        }
        [Fact]

        public async Task LoginAsync_AlreadyLoggedIn_ReturnsFailure()
        {
            var player = new Player { username = "User", password_hash = "Hash" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("t@t.com")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Pass", "Hash")).Returns(true);
            userSessionManagerMock.Setup(s => s.isUserLoggedIn("User")).Returns(true);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "Pass" });

            Assert.False(result.OperationResult.Success);
        }

        [Fact]
        public async Task LoginAsync_AlreadyLoggedIn_ReturnsSpecificCode()
        {
            var player = new Player { username = "User", password_hash = "Hash" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("t@t.com")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Pass", "Hash")).Returns(true);
            userSessionManagerMock.Setup(s => s.isUserLoggedIn("User")).Returns(true);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "Pass" });

            Assert.Equal("AUTH_USER_ALREADY_LOGGED_IN", result.ResultCode);
        }

        [Fact]
        public async Task LoginAsync_Unverified_ReturnsSpecificCode()
        {
            var player = new Player { is_verified = false, password_hash = "Hash" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("t@t.com")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Pass", "Hash")).Returns(true);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "Pass" });

            Assert.Equal("AUTH_ACCOUNT_NOT_VERIFIED", result.ResultCode);
        }

        [Fact]
        public async Task LoginAsync_Success_ReturnsSuccess()
        {
            var player = new Player { username = "User", is_verified = true, password_hash = "Hash", idPlayer = 1 };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("t@t.com")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Pass", "Hash")).Returns(true);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "Pass" });

            Assert.True(result.OperationResult.Success);
        }

        [Fact]
        public async Task LoginAsync_Success_AddsSession()
        {
            var player = new Player { username = "User", is_verified = true, password_hash = "Hash", idPlayer = 1 };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("t@t.com")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Pass", "Hash")).Returns(true);

            await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "Pass" });

            userSessionManagerMock.Verify(s => s.addSession("User"), Times.Once);
        }

        [Fact]
        public async Task LoginAsync_Success_ReturnsUsername()
        {
            var player = new Player { username = "User", is_verified = true, password_hash = "Hash", avatar_path = "img.png" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "P" });

            Assert.Equal("User", result.Username);
        }

        [Fact]
        public async Task LoginAsync_Success_ReturnsAvatarPath()
        {
            var player = new Player { username = "User", is_verified = true, password_hash = "Hash", avatar_path = "img.png" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

            var result = await authenticationLogic.loginAsync(new LoginDto { Email = "t@t.com", Password = "P" });

            Assert.Equal("img.png", result.AvatarPath);
        }
        [Fact]
        public void Logout_ValidUsername_RemovesSession()
        {
            authenticationLogic.logout("User");
            userSessionManagerMock.Verify(s => s.removeSession("User"), Times.Once);
        }

        [Fact]
        public void Logout_NullUsername_DoesNothing()
        {
            authenticationLogic.logout(null);
            userSessionManagerMock.Verify(s => s.removeSession(It.IsAny<string>()), Times.Never);
        }


        [Fact]
        public async Task RegisterPlayerAsync_NullProfile_ReturnsError()
        {
            var result = await authenticationLogic.registerPlayerAsync(null, "pass");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task RegisterPlayerAsync_ValidationFail_ReturnsError()
        {
            var failure = new ValidationResult(new[] { new ValidationFailure("Prop", "Error") });
            profileValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<UserProfileDto>(), default(CancellationToken))).ReturnsAsync(failure);

            var result = await authenticationLogic.registerPlayerAsync(new UserProfileDto(), "pass");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task RegisterPlayerAsync_WeakPassword_ReturnsError()
        {
            passwordPolicyValidatorMock.Setup(p => p.validate("weak")).Returns(new OperationResultDto { Success = false });

            var result = await authenticationLogic.registerPlayerAsync(new UserProfileDto(), "weak");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task RegisterPlayerAsync_NewUser_CallsAddPlayer()
        {
            var dto = new UserProfileDto { Username = "New", Email = "e@e.com", FirstName = "F" };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("New")).ReturnsAsync((Player)null!);
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("e@e.com")).ReturnsAsync((Player)null!);

            await authenticationLogic.registerPlayerAsync(dto, "Pass123!");

            playerRepositoryMock.Verify(r => r.addPlayer(It.IsAny<Player>()), Times.Once);
        }

        [Fact]
        public async Task RegisterPlayerAsync_NewUser_SendsEmail()
        {
            var dto = new UserProfileDto { Username = "New", Email = "e@e.com", FirstName = "F" };
            await authenticationLogic.registerPlayerAsync(dto, "Pass");
            emailServiceMock.Verify(e => e.sendEmailAsync("e@e.com", "New", It.IsAny<IEmailTemplate>()), Times.Once);
        }

        [Fact]
        public async Task RegisterPlayerAsync_ExistingVerifiedUser_ReturnsFailure()
        {
            var existing = new Player { is_verified = true };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("Exist"))
                .ReturnsAsync(existing);

            var result = await authenticationLogic.registerPlayerAsync(
                new UserProfileDto { Username = "Exist" }, "Pass");

            Assert.Equal(MessageCodes.AUTH_USER_ALREADY_EXISTS, result.MessageCode);
        }


        [Fact]
        public async Task RegisterPlayerAsync_ExistingUnverifiedUser_UpdatesPlayer()
        {
            var existing = new Player { is_verified = false, username = "U", email = "e@e.com" };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("U")).ReturnsAsync(existing);

            var dto = new UserProfileDto { Username = "U", Email = "e@e.com", FirstName = "NewName" };

            await authenticationLogic.registerPlayerAsync(dto, "Pass");

            playerRepositoryMock.Verify(r => r.updatePlayerAsync(existing), Times.Once);
        }


        [Fact]
        public async Task VerifyAccountAsync_NullArgs_ReturnsError()
        {
            var result = await authenticationLogic.verifyAccountAsync(null, null);
            Assert.False(result.Success);
        }

        [Fact]
        public async Task VerifyAccountAsync_InvalidFormat_ReturnsError()
        {
            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "ABC");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task VerifyAccountAsync_PlayerNotFound_ReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync((Player)null!);
            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "123456");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task VerifyAccountAsync_AlreadyVerified_ReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new Player { is_verified = true });

            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "123456");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task VerifyAccountAsync_CodeMismatch_ReturnsError()
        {
            var player = new Player { is_verified = false, verification_code = "654321", code_expiry_date = DateTime.UtcNow.AddHours(1) };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);

            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "123456");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task VerifyAccountAsync_CodeExpired_ReturnsError()
        {
            var player = new Player { is_verified = false, verification_code = "123456", code_expiry_date = DateTime.UtcNow.AddMinutes(-1) };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);

            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "123456");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task VerifyAccountAsync_Success_UpdatesPlayer()
        {
            var player = new Player { is_verified = false, verification_code = "123456", code_expiry_date = DateTime.UtcNow.AddHours(1) };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);

            var result = await authenticationLogic.verifyAccountAsync("e@e.com", "123456");

            playerRepositoryMock.Verify(r => r.updatePlayerAsync(player), Times.Once);
        }

        [Fact]
        public async Task ResendVerificationCodeAsync_NotFound_ReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync((Player)null!);
            var result = await authenticationLogic.resendVerificationCodeAsync("e@e.com");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task ResendVerificationCodeAsync_AlreadyVerified_ReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(new Player { is_verified = true });
            var result = await authenticationLogic.resendVerificationCodeAsync("e@e.com");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task ResendVerificationCodeAsync_Success_SendsEmail()
        {
            var player = new Player { is_verified = false, email = "e@e.com", username = "U" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("e@e.com")).ReturnsAsync(player);
            verificationCodeServiceMock.Setup(v => v.generateVerificationCode()).Returns("111111");

            var result = await authenticationLogic.resendVerificationCodeAsync("e@e.com");

            emailServiceMock.Verify(e => e.sendEmailAsync("e@e.com", "U", It.IsAny<IEmailTemplate>()), Times.Once);
        }

        [Fact]
        public async Task SendPasswordRecoveryCodeAsync_UserNotFound_ReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync((Player)null!);
            var result = await authenticationLogic.sendPasswordRecoveryCodeAsync("e@e.com");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task SendPasswordRecoveryCodeAsync_UnverifiedUser_ReturnsError()
        {
            var player = new Player { email = "e@e.com", username = "U" };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync("e@e.com"))
                .ReturnsAsync(player);

            var result = await authenticationLogic.sendPasswordRecoveryCodeAsync("e@e.com");

            Assert.False(result.Success);
        }

        [Fact]
        public async Task ResetPasswordWithCodeAsync_WeakPassword_ReturnsError()
        {
            passwordPolicyValidatorMock.Setup(v => v.validate("weak")).Returns(new OperationResultDto { Success = false });
            var result = await authenticationLogic.resetPasswordWithCodeAsync("e@e.com", "123456", "weak");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task ResetPasswordWithCodeAsync_InvalidCode_ReturnsError()
        {
            var player = new Player { verification_code = "654321", code_expiry_date = DateTime.UtcNow.AddHours(1) };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);
            passwordPolicyValidatorMock.Setup(v => v.validate(It.IsAny<string>())).Returns(new OperationResultDto { Success = true });

            var result = await authenticationLogic.resetPasswordWithCodeAsync("e@e.com", "123456", "NewPass");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task ResetPasswordWithCodeAsync_Success_UpdatesPassword()
        {
            var player = new Player { verification_code = "123456", code_expiry_date = DateTime.UtcNow.AddHours(1) };
            playerRepositoryMock.Setup(r => r.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync(player);
            passwordPolicyValidatorMock.Setup(v => v.validate(It.IsAny<string>())).Returns(new OperationResultDto { Success = true });
            passwordServiceMock.Setup(p => p.hashPassword("NewPass")).Returns("NewHash");

            var result = await authenticationLogic.resetPasswordWithCodeAsync("e@e.com", "123456", "NewPass");

            Assert.Equal("NewHash", player.password_hash);
        }
    }
}