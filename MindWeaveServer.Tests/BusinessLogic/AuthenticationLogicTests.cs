using FluentValidation;
using FluentValidation.Results; 
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.DataAccess; 
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Abstractions;
using MindWeaveServer.Utilities.Email;
using MindWeaveServer.Utilities.Email.Templates;
using Moq;

namespace MindWeaveServer.Tests.BusinessLogic
{
    public class AuthenticationLogicTests
    {
        private readonly Mock<IPlayerRepository> mockPlayerRepository;
        private readonly Mock<IEmailService> mockEmailService;
        private readonly Mock<IPasswordService> mockPasswordService;
        private readonly Mock<IPasswordPolicyValidator> mockPasswordPolicyValidator;
        private readonly Mock<IValidator<UserProfileDto>> mockProfileValidator;
        private readonly Mock<IValidator<LoginDto>> mockLoginValidator;
        private readonly Mock<IUserSessionManager> mockUserSessionManager;

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
            var mockVerificationCodeService1 = new Mock<IVerificationCodeService>();
            mockProfileValidator = new Mock<IValidator<UserProfileDto>>();
            mockLoginValidator = new Mock<IValidator<LoginDto>>();

            mockPasswordService.Setup(ps => ps.hashPassword(It.IsAny<string>())).Returns(hashedPassword);
            mockPasswordService.Setup(ps => ps.verifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
            mockPasswordPolicyValidator.Setup(v => v.validate(It.IsAny<string>())).Returns(new OperationResultDto { Success = true });
            mockVerificationCodeService1.Setup(vcs => vcs.generateVerificationCode()).Returns(verificationCode);
            mockVerificationCodeService1.Setup(vcs => vcs.getVerificationExpiryTime()).Returns(futureExpiry);
            mockUserSessionManager.Setup(m => m.isUserLoggedIn(It.IsAny<string>())).Returns(false);

            mockProfileValidator.Setup(v => v.ValidateAsync(It.IsAny<UserProfileDto>(), It.IsAny<CancellationToken>()))
                                 .ReturnsAsync(new ValidationResult());
            mockLoginValidator.Setup(v => v.ValidateAsync(It.IsAny<LoginDto>(), It.IsAny<CancellationToken>()))
                               .ReturnsAsync(new ValidationResult()); 

            authenticationLogic = new AuthenticationLogic(
                mockPlayerRepository.Object,
                mockEmailService.Object,
                mockPasswordService.Object,
                mockPasswordPolicyValidator.Object,
                mockVerificationCodeService1.Object,
                mockUserSessionManager.Object,
                mockProfileValidator.Object,
                mockLoginValidator.Object
            );

            validUserProfileDto = new UserProfileDto
            {
                Username = "testuser",
                Email = "test@example.com",
                FirstName = "Test",
                LastName = "User",
                DateOfBirth = new DateTime(1990, 1, 1),
                GenderId = 1
            };
        }

        private ValidationResult createValidationFailure(string propertyName, string errorMessage)
        {
            return new ValidationResult(new List<ValidationFailure> {
                new(propertyName, errorMessage)
            });
        }


        [Fact]
        public async Task RegisterPlayerAsync_WithNewUserAndValidData_ShouldReturnSuccessAndAddPlayerAndSendEmail()
        {
            mockPlayerRepository.Setup(repo => repo.getPlayerByUsernameAsync(validUserProfileDto.Username)).Returns(Task.FromResult<Player?>(null));
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(validUserProfileDto.Email)).Returns(Task.FromResult<Player?>(null));
            mockPlayerRepository.Setup(repo => repo.saveChangesAsync()).ReturnsAsync(1);

            var result = await authenticationLogic.registerPlayerAsync(validUserProfileDto, validPassword);

            Assert.True(result.Success);
            Assert.Equal(Lang.RegistrationSuccessful, result.Message);

            
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Once);
            
            mockEmailService.Verify(es => es.sendEmailAsync(
                validUserProfileDto.Email, validUserProfileDto.Username, It.IsAny<IEmailTemplate>()
            ), Times.Once);
        }

        [Fact]
        public async Task RegisterPlayerAsync_WithExistingVerifiedUserByUsername_ShouldReturnFailure()
        {
            var existingVerifiedPlayer = new Player { username = validUserProfileDto.Username, email = "other@example.com", is_verified = true };
            mockPlayerRepository.Setup(repo => repo.getPlayerByUsernameAsync(validUserProfileDto.Username)).ReturnsAsync(existingVerifiedPlayer);
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(validUserProfileDto.Email)).Returns(Task.FromResult<Player?>(null));

            var result = await authenticationLogic.registerPlayerAsync(validUserProfileDto, validPassword);

            Assert.False(result.Success);
            Assert.Equal(Lang.RegistrationUsernameOrEmailExists, result.Message);
            mockPlayerRepository.Verify(repo => repo.addPlayer(It.IsAny<Player>()), Times.Never);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
            mockEmailService.Verify(es => es.sendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEmailTemplate>()), Times.Never);
        }

        [Fact]
        public async Task RegisterPlayerAsync_WithExistingVerifiedUserByEmail_ShouldReturnFailure()
        {
            var existingVerifiedPlayer = new Player { username = "otheruser", email = validUserProfileDto.Email, is_verified = true };
            mockPlayerRepository.Setup(repo => repo.getPlayerByUsernameAsync(validUserProfileDto.Username)).Returns(Task.FromResult<Player?>(null));
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(validUserProfileDto.Email)).ReturnsAsync(existingVerifiedPlayer);

            var result = await authenticationLogic.registerPlayerAsync(validUserProfileDto, validPassword);

            Assert.False(result.Success);
            Assert.Equal(Lang.RegistrationUsernameOrEmailExists, result.Message);
            mockPlayerRepository.Verify(repo => repo.addPlayer(It.IsAny<Player>()), Times.Never);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
            mockEmailService.Verify(es => es.sendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEmailTemplate>()), Times.Never);
        }


        [Fact]
        public async Task RegisterPlayerAsync_WithInvalidProfileData_ShouldReturnFailureFromValidator()
        {
            string validationError = "Username is too short";
            mockProfileValidator.Setup(v => v.ValidateAsync(It.IsAny<UserProfileDto>(), It.IsAny<CancellationToken>()))
                                 .ReturnsAsync(createValidationFailure("username", validationError));
            mockPlayerRepository.Setup(repo => repo.getPlayerByUsernameAsync(It.IsAny<string>())).Returns(Task.FromResult<Player?>(null));
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(It.IsAny<string>())).Returns(Task.FromResult<Player?>(null));

            var result = await authenticationLogic.registerPlayerAsync(validUserProfileDto, validPassword);

            Assert.False(result.Success);
            Assert.Equal(validationError, result.Message);
            mockPlayerRepository.Verify(repo => repo.addPlayer(It.IsAny<Player>()), Times.Never);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
            mockEmailService.Verify(es => es.sendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEmailTemplate>()), Times.Never);
        }

        [Fact]
        public async Task RegisterPlayerAsync_WithInvalidPasswordPolicy_ShouldReturnFailureFromValidator()
        {
            string policyError = Lang.ValidationPasswordLength;
            mockPasswordPolicyValidator.Setup(v => v.validate(It.IsAny<string>()))
                                      .Returns(new OperationResultDto { Success = false, Message = policyError });
            mockPlayerRepository.Setup(repo => repo.getPlayerByUsernameAsync(It.IsAny<string>())).Returns(Task.FromResult<Player?>(null));
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(It.IsAny<string>())).Returns(Task.FromResult<Player?>(null));

            var result = await authenticationLogic.registerPlayerAsync(validUserProfileDto, "short");

            Assert.False(result.Success);
            Assert.Equal(policyError, result.Message);
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

            Assert.True(result.Success);
            Assert.Equal(Lang.VerificationSuccessful, result.Message);
            Assert.True(playerToVerify.is_verified);
            Assert.Null(playerToVerify.verification_code);
            Assert.Null(playerToVerify.code_expiry_date);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task VerifyAccountAsync_WithInvalidEmail_ShouldReturnFailure()
        {
            string email = "nonexistent@example.com";
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).Returns(Task.FromResult<Player?>(null));

            var result = await authenticationLogic.verifyAccountAsync(email, verificationCode);

            Assert.False(result.Success);
            Assert.Equal(Lang.VerificationEmailNotFound, result.Message);
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

            Assert.False(result.Success);
            Assert.Equal(Lang.VerificationInvalidOrExpiredCode, result.Message);
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

            Assert.False(result.Success);
            Assert.Equal(Lang.VerificationInvalidOrExpiredCode, result.Message);
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

            Assert.False(result.Success);
            Assert.Equal(Lang.VerificationAccountAlreadyVerified, result.Message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task LoginAsync_WithValidCredentialsAndVerifiedUser_ShouldReturnSuccess()
        {
            var loginDto = new LoginDto { Email = "test@example.com", Password = validPassword };
            var player = new Player { username = "testuser", email = loginDto.Email, password_hash = hashedPassword, is_verified = true, avatar_path = "/path/avatar.png" };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(loginDto.Email)).ReturnsAsync(player);
            mockPasswordService.Setup(ps => ps.verifyPassword(loginDto.Password, player.password_hash)).Returns(true);

            var result = await authenticationLogic.loginAsync(loginDto);

            Assert.True(result.OperationResult.Success);
            Assert.Equal(Lang.LoginSuccessful, result.OperationResult.Message);
            Assert.Equal(player.username, result.Username);
            Assert.Equal(player.avatar_path, result.AvatarPath);
            Assert.Null(result.ResultCode);
        }

        [Fact]
        public async Task LoginAsync_WithValidCredentialsButUnverifiedUser_ShouldReturnFailureAndNotVerifiedCode()
        {
            var loginDto = new LoginDto { Email = "unverified@example.com", Password = validPassword };
            var player = new Player { username = "unverifiedUser", email = loginDto.Email, password_hash = hashedPassword, is_verified = false };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(loginDto.Email)).ReturnsAsync(player);
            mockPasswordService.Setup(ps => ps.verifyPassword(loginDto.Password, player.password_hash)).Returns(true);

            var result = await authenticationLogic.loginAsync(loginDto);

            Assert.False(result.OperationResult.Success);
            Assert.Equal(Lang.LoginAccountNotVerified, result.OperationResult.Message);
            Assert.Equal("ACCOUNT_NOT_VERIFIED", result.ResultCode);
            Assert.Null(result.Username);
            Assert.Null(result.AvatarPath);
        }

        [Fact]
        public async Task LoginAsync_WithInvalidEmail_ShouldReturnFailure()
        {
            var loginDto = new LoginDto { Email = "wrong@example.com", Password = validPassword };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(loginDto.Email)).Returns(Task.FromResult<Player?>(null));

            var result = await authenticationLogic.loginAsync(loginDto);

            Assert.False(result.OperationResult.Success);
            Assert.Equal(Lang.LoginPasswordNotEmpty, result.OperationResult.Message);
            mockPasswordService.Verify(ps => ps.verifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task LoginAsync_WithInvalidPassword_ShouldReturnFailure()
        {
            var loginDto = new LoginDto { Email = "test@example.com", Password = "WrongPassword" };
            var player = new Player { username = "testuser", email = loginDto.Email, password_hash = hashedPassword, is_verified = true };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(loginDto.Email)).ReturnsAsync(player);
            mockPasswordService.Setup(ps => ps.verifyPassword(loginDto.Password, player.password_hash)).Returns(false);

            var result = await authenticationLogic.loginAsync(loginDto);

            Assert.False(result.OperationResult.Success);
            Assert.Equal(Lang.LoginPasswordNotEmpty, result.OperationResult.Message);
        }

        [Fact]
        public async Task LoginAsync_WithNullLoginData_ShouldReturnFailure()
        {
            LoginDto nullLoginData = null!;

            var result = await authenticationLogic.loginAsync(nullLoginData);

            Assert.False(result.OperationResult.Success);
            Assert.Equal(Lang.ErrorAllFieldsRequired, result.OperationResult.Message);
        }

        [Fact]
        public async Task LoginAsync_WhenLoginValidatorFails_ShouldReturnFailureFromValidator()
        {
            var loginDto = new LoginDto { Email = "invalid-email", Password = "" };
            string validationError = Lang.ValidationEmailFormat;
            mockLoginValidator.Setup(v => v.ValidateAsync(loginDto, It.IsAny<CancellationToken>()))
                              .ReturnsAsync(createValidationFailure("email", validationError));

            var result = await authenticationLogic.loginAsync(loginDto);

            Assert.False(result.OperationResult.Success);
            Assert.Equal(validationError, result.OperationResult.Message);
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

            Assert.True(result.Success);
            Assert.Equal(Lang.RegistrationSuccessful, result.Message);
            Assert.Equal(verificationCode, player.verification_code);
            Assert.Equal(futureExpiry, player.code_expiry_date); 
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Once);
            mockEmailService.Verify(es => es.sendEmailAsync(email, player.username, It.IsAny<IEmailTemplate>()), Times.Once);
        }

        [Fact]
        public async Task ResendVerificationCodeAsync_WithInvalidEmail_ShouldReturnFailure()
        {
            string email = "nonexistent@example.com";
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).Returns(Task.FromResult<Player?>(null));

            var result = await authenticationLogic.resendVerificationCodeAsync(email);

            Assert.False(result.Success);
            Assert.Equal(Lang.VerificationEmailNotFound, result.Message);
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

            Assert.False(result.Success);
            Assert.Equal(Lang.VerificationAccountAlreadyVerified, result.Message);
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

            Assert.True(result.Success);
            Assert.Equal(Lang.InfoRecoveryCodeSent, result.Message);
            Assert.Equal(verificationCode, player.verification_code);
            Assert.Equal(futureExpiry, player.code_expiry_date);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Once);
            mockEmailService.Verify(es => es.sendEmailAsync(email, player.username, It.IsAny<PasswordRecoveryEmailTemplate>()), Times.Once);
        }

        [Fact]
        public async Task SendPasswordRecoveryCodeAsync_WithInvalidEmail_ShouldReturnFailure()
        {
            string email = "nonexistent@example.com";
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).Returns(Task.FromResult<Player?>(null));

            var result = await authenticationLogic.sendPasswordRecoveryCodeAsync(email);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(Lang.ErrorAccountNotFound, result.Message);
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
            mockPasswordPolicyValidator.Setup(v => v.validate(newPassword)).Returns(new OperationResultDto { Success = true });
            mockPasswordService.Setup(ps => ps.hashPassword(newPassword)).Returns(newHashedPassword);
            mockPlayerRepository.Setup(repo => repo.saveChangesAsync()).ReturnsAsync(1);

            var result = await authenticationLogic.resetPasswordWithCodeAsync(email, verificationCode, newPassword);

            Assert.True(result.Success);
            Assert.Equal(Lang.InfoPasswordResetSuccess, result.Message);
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
            mockPasswordPolicyValidator.Setup(v => v.validate(newPassword)).Returns(new OperationResultDto { Success = true });
            mockPasswordService.Setup(ps => ps.hashPassword(newPassword)).Returns(newHashedPassword);
            mockPlayerRepository.Setup(repo => repo.saveChangesAsync()).ReturnsAsync(1);

            var result = await authenticationLogic.resetPasswordWithCodeAsync(email, verificationCode, newPassword);

            Assert.True(result.Success);
            Assert.Equal(Lang.InfoPasswordResetSuccess, result.Message);
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
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).Returns(Task.FromResult<Player?>(null));

            var result = await authenticationLogic.resetPasswordWithCodeAsync(email, verificationCode, validPassword);

            Assert.False(result.Success);
            Assert.Equal(Lang.ErrorAccountNotFound, result.Message);
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

            Assert.False(result.Success);
            Assert.Equal(Lang.VerificationInvalidOrExpiredCode, result.Message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task ResetPasswordWithCodeAsync_WithExpiredCode_ShouldReturnFailure()
        {
            string email = "reset@example.com";
            var player = new Player { email = email, verification_code = verificationCode, code_expiry_date = pastExpiry };
            mockPlayerRepository.Setup(repo => repo.getPlayerByEmailAsync(email)).ReturnsAsync(player);

            var result = await authenticationLogic.resetPasswordWithCodeAsync(email, verificationCode, validPassword);

            Assert.False(result.Success);
            Assert.Equal(Lang.VerificationInvalidOrExpiredCode, result.Message);
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
                                      .Returns(new OperationResultDto { Success = false, Message = policyError });

            var result = await authenticationLogic.resetPasswordWithCodeAsync(email, verificationCode, weakPassword);

            Assert.False(result.Success);
            Assert.Equal(policyError, result.Message);
            mockPlayerRepository.Verify(repo => repo.saveChangesAsync(), Times.Never);
        }


    }
}