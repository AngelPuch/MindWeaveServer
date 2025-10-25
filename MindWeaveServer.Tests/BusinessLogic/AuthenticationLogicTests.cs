using FluentValidation;
using FluentValidation.Results; 
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.DataAccess; 
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Abstractions;
using MindWeaveServer.Utilities.Email;
using Moq;
using MindWeaveServer.Utilities.Email.Templates;

namespace MindWeaveServer.Tests.BusinessLogic
{
    public class AuthenticationLogicTests
    {
        private readonly Mock<IPlayerRepository> mockPlayerRepository;
        private readonly Mock<IEmailService> mockEmailService;
        private readonly Mock<IPasswordService> mockPasswordService;
        private readonly Mock<IPasswordPolicyValidator> mockPasswordPolicyValidator;
        private readonly Mock<IVerificationCodeService> mockVerificationCodeService;
        private readonly Mock<IValidator<UserProfileDto>> mockProfileValidator;
        private readonly Mock<IValidator<LoginDto>> mockLoginValidator;

        private readonly AuthenticationLogic authenticationLogic;

        private readonly UserProfileDto validUserProfileDto;
        private readonly string validPassword = "Password123";
        private readonly string hashedPassword = "hashedPassword123";
        private readonly string verificationCode = "123456";
        private readonly DateTime futureExpiry = DateTime.UtcNow.AddMinutes(5);
        private readonly DateTime pastExpiry = DateTime.UtcNow.AddMinutes(-5);

        public AuthenticationLogicTests()
        {
            mockPlayerRepository = new Mock<IPlayerRepository>();
            mockEmailService = new Mock<IEmailService>();
            mockPasswordService = new Mock<IPasswordService>();
            mockPasswordPolicyValidator = new Mock<IPasswordPolicyValidator>();
            mockVerificationCodeService = new Mock<IVerificationCodeService>();
            mockProfileValidator = new Mock<IValidator<UserProfileDto>>();
            mockLoginValidator = new Mock<IValidator<LoginDto>>();

            mockPasswordService.Setup(ps => ps.hashPassword(It.IsAny<string>())).Returns(hashedPassword);
            mockPasswordService.Setup(ps => ps.verifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
            mockPasswordPolicyValidator.Setup(v => v.validate(It.IsAny<string>())).Returns(new OperationResultDto { success = true });
            mockVerificationCodeService.Setup(vcs => vcs.generateVerificationCode()).Returns(verificationCode);
            mockVerificationCodeService.Setup(vcs => vcs.getVerificationExpiryTime()).Returns(futureExpiry);

            mockProfileValidator.Setup(v => v.ValidateAsync(It.IsAny<UserProfileDto>(), It.IsAny<CancellationToken>()))
                                 .ReturnsAsync(new ValidationResult());
            mockLoginValidator.Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), It.IsAny<CancellationToken>()))
                               .ReturnsAsync(new ValidationResult()); 

            authenticationLogic = new AuthenticationLogic(
                mockPlayerRepository.Object,
                mockEmailService.Object,
                mockPasswordService.Object,
                mockPasswordPolicyValidator.Object,
                mockVerificationCodeService.Object,
                mockProfileValidator.Object,
                mockLoginValidator.Object
            );

            validUserProfileDto = new UserProfileDto
            {
                username = "testuser",
                email = "test@example.com",
                firstName = "Test",
                lastName = "User",
                dateOfBirth = new DateTime(1990, 1, 1),
                genderId = 1
            };
        }

        private ValidationResult CreateValidationFailure(string propertyName, string errorMessage)
        {
            return new ValidationResult(new List<ValidationFailure> {
                new ValidationFailure(propertyName, errorMessage)
            });
        }


        [Fact]
        public async Task RegisterPlayerAsync_WithNewUserAndValidData_ShouldReturnSuccessAndAddPlayerAndSendEmail()
        {
            mockPlayerRepository.Setup(repo => repo.getPlayerByUsernameAsync(validUserProfileDto.username)).ReturnsAsync((Player)null);
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(validUserProfileDto.email)).ReturnsAsync((Player)null);
            mockPlayerRepository.Setup(repo => repo.saveChangesAsync()).ReturnsAsync(1);

            var result = await authenticationLogic.registerPlayerAsync(validUserProfileDto, validPassword);

            Assert.True(result.success);
            Assert.Equal(Lang.RegistrationSuccessful, result.message);

            
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Once);
            
            mockEmailService.Verify(es => es.sendEmailAsync(
                validUserProfileDto.email, validUserProfileDto.username, It.IsAny<IEmailTemplate>()
            ), Times.Once);
        }

        [Fact]
        public async Task RegisterPlayerAsync_WithExistingVerifiedUserByUsername_ShouldReturnFailure()
        {
            var existingVerifiedPlayer = new Player { username = validUserProfileDto.username, email = "other@example.com", is_verified = true };
            mockPlayerRepository.Setup(repo => repo.getPlayerByUsernameAsync(validUserProfileDto.username)).ReturnsAsync(existingVerifiedPlayer);
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(validUserProfileDto.email)).ReturnsAsync((Player)null);

            var result = await authenticationLogic.registerPlayerAsync(validUserProfileDto, validPassword);

            Assert.False(result.success);
            Assert.Equal(Lang.RegistrationUsernameOrEmailExists, result.message);
            mockPlayerRepository.Verify(repo => repo.addPlayer(It.IsAny<Player>()), Times.Never);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
            mockEmailService.Verify(es => es.sendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEmailTemplate>()), Times.Never);
        }

        [Fact]
        public async Task RegisterPlayerAsync_WithExistingVerifiedUserByEmail_ShouldReturnFailure()
        {
            var existingVerifiedPlayer = new Player { username = "otheruser", email = validUserProfileDto.email, is_verified = true };
            mockPlayerRepository.Setup(repo => repo.getPlayerByUsernameAsync(validUserProfileDto.username)).ReturnsAsync((Player)null);
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(validUserProfileDto.email)).ReturnsAsync(existingVerifiedPlayer);

            var result = await authenticationLogic.registerPlayerAsync(validUserProfileDto, validPassword);

            Assert.False(result.success);
            Assert.Equal(Lang.RegistrationUsernameOrEmailExists, result.message);
            mockPlayerRepository.Verify(repo => repo.addPlayer(It.IsAny<Player>()), Times.Never);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
            mockEmailService.Verify(es => es.sendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEmailTemplate>()), Times.Never);
        }


        [Fact]
        public async Task RegisterPlayerAsync_WithInvalidProfileData_ShouldReturnFailureFromValidator()
        {
            string validationError = "Username is too short";
            mockProfileValidator.Setup(v => v.ValidateAsync(It.IsAny<UserProfileDto>(), It.IsAny<CancellationToken>()))
                                 .ReturnsAsync(CreateValidationFailure("username", validationError));
            mockPlayerRepository.Setup(repo => repo.getPlayerByUsernameAsync(It.IsAny<string>())).ReturnsAsync((Player)null);
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync((Player)null);

            var result = await authenticationLogic.registerPlayerAsync(validUserProfileDto, validPassword);

            Assert.False(result.success);
            Assert.Equal(validationError, result.message);
            mockPlayerRepository.Verify(repo => repo.addPlayer(It.IsAny<Player>()), Times.Never);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
            mockEmailService.Verify(es => es.sendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEmailTemplate>()), Times.Never);
        }

        [Fact]
        public async Task RegisterPlayerAsync_WithInvalidPasswordPolicy_ShouldReturnFailureFromValidator()
        {
            string policyError = Lang.ValidationPasswordLength;
            mockPasswordPolicyValidator.Setup(v => v.validate(It.IsAny<string>()))
                                      .Returns(new OperationResultDto { success = false, message = policyError });
            mockPlayerRepository.Setup(repo => repo.getPlayerByUsernameAsync(It.IsAny<string>())).ReturnsAsync((Player)null);
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(It.IsAny<string>())).ReturnsAsync((Player)null);

            var result = await authenticationLogic.registerPlayerAsync(validUserProfileDto, "short");

            Assert.False(result.success);
            Assert.Equal(policyError, result.message);
            mockPlayerRepository.Verify(repo => repo.addPlayer(It.IsAny<Player>()), Times.Never);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
            mockEmailService.Verify(es => es.sendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEmailTemplate>()), Times.Never);
        }

        [Fact]
        public async Task VerifyAccountAsync_WithValidCodeAndEmail_ShouldReturnSuccessAndUpdatePlayer()
        {
            string email = "verify@example.com";
            var playerToVerify = new Player { email = email, is_verified = false, verification_code = verificationCode, code_expiry_date = futureExpiry };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(playerToVerify);
            mockPlayerRepository.Setup(repo => repo.saveChangesAsync()).ReturnsAsync(1);

            var result = await authenticationLogic.verifyAccountAsync(email, verificationCode);

            Assert.True(result.success);
            Assert.Equal(Lang.VerificationSuccessful, result.message);
            Assert.True(playerToVerify.is_verified);
            Assert.Null(playerToVerify.verification_code);
            Assert.Null(playerToVerify.code_expiry_date);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task VerifyAccountAsync_WithInvalidEmail_ShouldReturnFailure()
        {
            string email = "nonexistent@example.com";
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync((Player)null);

            var result = await authenticationLogic.verifyAccountAsync(email, verificationCode);

            Assert.False(result.success);
            Assert.Equal(Lang.VerificationEmailNotFound, result.message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task VerifyAccountAsync_WithInvalidCode_ShouldReturnFailure()
        {
            string email = "verify@example.com";
            string wrongCode = "654321";
            var playerToVerify = new Player { email = email, is_verified = false, verification_code = verificationCode, code_expiry_date = futureExpiry };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(playerToVerify);

            var result = await authenticationLogic.verifyAccountAsync(email, wrongCode);

            Assert.False(result.success);
            Assert.Equal(Lang.VerificationInvalidOrExpiredCode, result.message);
            Assert.False(playerToVerify.is_verified);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task VerifyAccountAsync_WithExpiredCode_ShouldReturnFailure()
        {
            string email = "verify@example.com";
            var playerToVerify = new Player { email = email, is_verified = false, verification_code = verificationCode, code_expiry_date = pastExpiry };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(playerToVerify);

            var result = await authenticationLogic.verifyAccountAsync(email, verificationCode);

            Assert.False(result.success);
            Assert.Equal(Lang.VerificationInvalidOrExpiredCode, result.message);
            Assert.False(playerToVerify.is_verified);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task VerifyAccountAsync_WithAlreadyVerifiedAccount_ShouldReturnFailure()
        {
            string email = "verified@example.com";
            var playerVerified = new Player { email = email, is_verified = true, verification_code = null, code_expiry_date = null };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(playerVerified);

            var result = await authenticationLogic.verifyAccountAsync(email, verificationCode);

            Assert.False(result.success);
            Assert.Equal(Lang.VerificationAccountAlreadyVerified, result.message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task LoginAsync_WithValidCredentialsAndVerifiedUser_ShouldReturnSuccess()
        {
            var loginDto = new LoginDto { email = "test@example.com", password = validPassword };
            var player = new Player { username = "testuser", email = loginDto.email, password_hash = hashedPassword, is_verified = true, avatar_path = "/path/avatar.png" };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(loginDto.email)).ReturnsAsync(player);
            mockPasswordService.Setup(ps => ps.verifyPassword(loginDto.password, player.password_hash)).Returns(true);

            var result = await authenticationLogic.loginAsync(loginDto);

            Assert.True(result.operationResult.success);
            Assert.Equal(Lang.LoginSuccessful, result.operationResult.message);
            Assert.Equal(player.username, result.username);
            Assert.Equal(player.avatar_path, result.avatarPath);
            Assert.Null(result.resultCode);
        }

        [Fact]
        public async Task LoginAsync_WithValidCredentialsButUnverifiedUser_ShouldReturnFailureAndNotVerifiedCode()
        {
            var loginDto = new LoginDto { email = "unverified@example.com", password = validPassword };
            var player = new Player { username = "unverifiedUser", email = loginDto.email, password_hash = hashedPassword, is_verified = false };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(loginDto.email)).ReturnsAsync(player);
            mockPasswordService.Setup(ps => ps.verifyPassword(loginDto.password, player.password_hash)).Returns(true);

            var result = await authenticationLogic.loginAsync(loginDto);

            Assert.False(result.operationResult.success);
            Assert.Equal(Lang.LoginAccountNotVerified, result.operationResult.message);
            Assert.Equal("ACCOUNT_NOT_VERIFIED", result.resultCode);
            Assert.Null(result.username);
            Assert.Null(result.avatarPath);
        }

        [Fact]
        public async Task LoginAsync_WithInvalidEmail_ShouldReturnFailure()
        {
            var loginDto = new LoginDto { email = "wrong@example.com", password = validPassword };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(loginDto.email)).ReturnsAsync((Player)null);

            var result = await authenticationLogic.loginAsync(loginDto);

            Assert.False(result.operationResult.success);
            Assert.Equal(Lang.LoginPasswordNotEmpty, result.operationResult.message);
            mockPasswordService.Verify(ps => ps.verifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task LoginAsync_WithInvalidPassword_ShouldReturnFailure()
        {
            var loginDto = new LoginDto { email = "test@example.com", password = "WrongPassword" };
            var player = new Player { username = "testuser", email = loginDto.email, password_hash = hashedPassword, is_verified = true };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(loginDto.email)).ReturnsAsync(player);
            mockPasswordService.Setup(ps => ps.verifyPassword(loginDto.password, player.password_hash)).Returns(false);

            var result = await authenticationLogic.loginAsync(loginDto);

            Assert.False(result.operationResult.success);
            Assert.Equal(Lang.LoginPasswordNotEmpty, result.operationResult.message);
        }

        [Fact]
        public async Task LoginAsync_WithNullLoginData_ShouldReturnFailure()
        {
            LoginDto nullLoginData = null;

            var result = await authenticationLogic.loginAsync(nullLoginData);

            Assert.False(result.operationResult.success);
            Assert.Equal(Lang.ErrorAllFieldsRequired, result.operationResult.message); // O un mensaje específico para DTO nulo
        }

        [Fact]
        public async Task LoginAsync_WhenLoginValidatorFails_ShouldReturnFailureFromValidator()
        {
            var loginDto = new LoginDto { email = "invalid-email", password = "" };
            string validationError = Lang.ValidationEmailFormat;
            mockLoginValidator.Setup(v => v.ValidateAsync(loginDto, It.IsAny<CancellationToken>()))
                              .ReturnsAsync(CreateValidationFailure("email", validationError));

            var result = await authenticationLogic.loginAsync(loginDto);

            Assert.False(result.operationResult.success);
            Assert.Equal(validationError, result.operationResult.message);
            mockPlayerRepository.Verify(repo => repo.getPlayerByEmailAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ResendVerificationCodeAsync_WithValidEmailAndUnverifiedUser_ShouldReturnSuccessAndUpdateCodeAndSendEmail()
        {
            string email = "resend@example.com";
            var player = new Player { username = "resendUser", email = email, is_verified = false, verification_code = "587541", code_expiry_date = pastExpiry };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(player);
            mockPlayerRepository.Setup(repo => repo.saveChangesAsync()).ReturnsAsync(1);

            var result = await authenticationLogic.resendVerificationCodeAsync(email);

            Assert.True(result.success);
            Assert.Equal(Lang.RegistrationSuccessful, result.message);
            Assert.Equal(verificationCode, player.verification_code);
            Assert.Equal(futureExpiry, player.code_expiry_date); 
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Once);
            mockEmailService.Verify(es => es.sendEmailAsync(email, player.username, It.IsAny<IEmailTemplate>()), Times.Once);
        }

        [Fact]
        public async Task ResendVerificationCodeAsync_WithInvalidEmail_ShouldReturnFailure()
        {
            string email = "nonexistent@example.com";
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync((Player)null);

            var result = await authenticationLogic.resendVerificationCodeAsync(email);

            Assert.False(result.success);
            Assert.Equal(Lang.VerificationEmailNotFound, result.message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
            mockEmailService.Verify(es => es.sendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEmailTemplate>()), Times.Never);
        }

        [Fact]
        public async Task ResendVerificationCodeAsync_WithAlreadyVerifiedUser_ShouldReturnFailure()
        {
            string email = "verified@example.com";
            var player = new Player { email = email, is_verified = true };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(player);

            var result = await authenticationLogic.resendVerificationCodeAsync(email);

            Assert.False(result.success);
            Assert.Equal(Lang.VerificationAccountAlreadyVerified, result.message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
            mockEmailService.Verify(es => es.sendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEmailTemplate>()), Times.Never);
        }

        [Fact]
        public async Task SendPasswordRecoveryCodeAsync_WithValidEmail_ShouldReturnSuccessUpdatePlayerAndSendEmail()
        {
            string email = "recovery@example.com";
            var player = new Player { username = "recoverUser", email = email, verification_code = null, code_expiry_date = null };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(player);
            mockPlayerRepository.Setup(repo => repo.saveChangesAsync()).ReturnsAsync(1);

            var result = await authenticationLogic.sendPasswordRecoveryCodeAsync(email);

            Assert.True(result.success);
            Assert.Equal(Lang.InfoRecoveryCodeSent, result.message);
            Assert.Equal(verificationCode, player.verification_code);
            Assert.Equal(futureExpiry, player.code_expiry_date);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Once);
            mockEmailService.Verify(es => es.sendEmailAsync(email, player.username, It.IsAny<PasswordRecoveryEmailTemplate>()), Times.Once);
        }

        [Fact]
        public async Task SendPasswordRecoveryCodeAsync_WithInvalidEmail_ShouldReturnFailure()
        {
            string email = "nonexistent@example.com";
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync((Player)null);

            var result = await authenticationLogic.sendPasswordRecoveryCodeAsync(email);

            // Assert
            Assert.False(result.success);
            Assert.Equal(Lang.ErrorAccountNotFound, result.message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
            mockEmailService.Verify(es => es.sendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEmailTemplate>()), Times.Never);
        }


        [Fact]
        public async Task ResetPasswordWithCodeAsync_WithValidData_ShouldReturnSuccessAndUpdatePassword()
        {
            // Arrange
            string email = "reset@example.com";
            string newPassword = "NewPassword123";
            string newHashedPassword = "newHashedPassword";
            var player = new Player { email = email, password_hash = hashedPassword, is_verified = true, verification_code = verificationCode, code_expiry_date = futureExpiry };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(player);
            mockPasswordPolicyValidator.Setup(v => v.validate(newPassword)).Returns(new OperationResultDto { success = true });
            mockPasswordService.Setup(ps => ps.hashPassword(newPassword)).Returns(newHashedPassword);
            mockPlayerRepository.Setup(repo => repo.saveChangesAsync()).ReturnsAsync(1);

            var result = await authenticationLogic.resetPasswordWithCodeAsync(email, verificationCode, newPassword);

            Assert.True(result.success);
            Assert.Equal(Lang.InfoPasswordResetSuccess, result.message);
            Assert.Equal(newHashedPassword, player.password_hash);
            Assert.Null(player.verification_code);
            Assert.Null(player.code_expiry_date);
            Assert.True(player.is_verified);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task ResetPasswordWithCodeAsync_WithValidDataUnverifiedUser_ShouldReturnSuccessAndUpdatePasswordAndVerify()
        {
            string email = "reset-unverified@example.com";
            string newPassword = "NewPassword123";
            string newHashedPassword = "newHashedPassword";

            var player = new Player { email = email, password_hash = hashedPassword, is_verified = false, verification_code = verificationCode, code_expiry_date = futureExpiry };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(player);
            mockPasswordPolicyValidator.Setup(v => v.validate(newPassword)).Returns(new OperationResultDto { success = true });
            mockPasswordService.Setup(ps => ps.hashPassword(newPassword)).Returns(newHashedPassword);
            mockPlayerRepository.Setup(repo => repo.saveChangesAsync()).ReturnsAsync(1);

            var result = await authenticationLogic.resetPasswordWithCodeAsync(email, verificationCode, newPassword);

            Assert.True(result.success);
            Assert.Equal(Lang.InfoPasswordResetSuccess, result.message);
            Assert.Equal(newHashedPassword, player.password_hash);
            Assert.Null(player.verification_code);
            Assert.Null(player.code_expiry_date);
            Assert.True(player.is_verified);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Once);
        }


        [Fact]
        public async Task ResetPasswordWithCodeAsync_WithInvalidEmail_ShouldReturnFailure()
        {
            string email = "nonexistent@example.com";
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync((Player)null);

            var result = await authenticationLogic.resetPasswordWithCodeAsync(email, verificationCode, validPassword);

            Assert.False(result.success);
            Assert.Equal(Lang.ErrorAccountNotFound, result.message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task ResetPasswordWithCodeAsync_WithInvalidCode_ShouldReturnFailure()
        {
            string email = "reset@example.com";
            string wrongCode = "000000";
            var player = new Player { email = email, verification_code = verificationCode, code_expiry_date = futureExpiry };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(player);

            var result = await authenticationLogic.resetPasswordWithCodeAsync(email, wrongCode, validPassword);

            Assert.False(result.success);
            Assert.Equal(Lang.VerificationInvalidOrExpiredCode, result.message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task ResetPasswordWithCodeAsync_WithExpiredCode_ShouldReturnFailure()
        {
            string email = "reset@example.com";
            var player = new Player { email = email, verification_code = verificationCode, code_expiry_date = pastExpiry };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(player);

            var result = await authenticationLogic.resetPasswordWithCodeAsync(email, verificationCode, validPassword);

            Assert.False(result.success);
            Assert.Equal(Lang.VerificationInvalidOrExpiredCode, result.message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task ResetPasswordWithCodeAsync_WithInvalidNewPasswordPolicy_ShouldReturnFailure()
        {
            string email = "reset@example.com";
            string weakPassword = "weak";
            string policyError = Lang.ValidationPasswordLength;
            var player = new Player { email = email, verification_code = verificationCode, code_expiry_date = futureExpiry };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(player);

            mockPasswordPolicyValidator.Setup(v => v.validate(weakPassword))
                                      .Returns(new OperationResultDto { success = false, message = policyError });

            var result = await authenticationLogic.resetPasswordWithCodeAsync(email, verificationCode, weakPassword);

            Assert.False(result.success);
            Assert.Equal(policyError, result.message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
        }


    }
}