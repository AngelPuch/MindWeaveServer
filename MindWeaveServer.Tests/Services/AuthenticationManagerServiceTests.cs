using FluentValidation;
using FluentValidation.Results;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Services;
using MindWeaveServer.Utilities.Abstractions;
using MindWeaveServer.Utilities.Email;
using Moq;
using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;
using Xunit;

namespace MindWeaveServer.Tests.Services
{
    public class AuthenticationManagerServiceTests
    {
        private readonly Mock<IPlayerRepository> playerRepoMock;
        private readonly Mock<IEmailService> emailServiceMock;
        private readonly Mock<IPasswordService> passwordServiceMock;
        private readonly Mock<IPasswordPolicyValidator> passwordPolicyMock;
        private readonly Mock<IVerificationCodeService> verificationMock;
        private readonly Mock<IUserSessionManager> sessionManagerMock;
        private readonly Mock<IValidator<UserProfileDto>> profileValidatorMock;
        private readonly Mock<IValidator<LoginDto>> loginValidatorMock;
        private readonly Mock<IServiceExceptionHandler> exceptionHandlerMock;

        private readonly AuthenticationLogic authLogic;
        private readonly AuthenticationManagerService service;

        public AuthenticationManagerServiceTests()
        {
            playerRepoMock = new Mock<IPlayerRepository>();
            emailServiceMock = new Mock<IEmailService>();
            passwordServiceMock = new Mock<IPasswordService>();
            passwordPolicyMock = new Mock<IPasswordPolicyValidator>();
            verificationMock = new Mock<IVerificationCodeService>();
            sessionManagerMock = new Mock<IUserSessionManager>();
            profileValidatorMock = new Mock<IValidator<UserProfileDto>>();
            loginValidatorMock = new Mock<IValidator<LoginDto>>();
            exceptionHandlerMock = new Mock<IServiceExceptionHandler>();

            profileValidatorMock.Setup(x => x.ValidateAsync(It.IsAny<UserProfileDto>(), default))
                .ReturnsAsync(new ValidationResult());
            loginValidatorMock.Setup(x => x.ValidateAsync(It.IsAny<LoginDto>(), default))
                .ReturnsAsync(new ValidationResult());
            passwordPolicyMock.Setup(x => x.validate(It.IsAny<string>()))
                .Returns(new OperationResultDto { Success = true });

            authLogic = new AuthenticationLogic(
                playerRepoMock.Object,
                emailServiceMock.Object,
                passwordServiceMock.Object,
                passwordPolicyMock.Object,
                verificationMock.Object,
                sessionManagerMock.Object,
                profileValidatorMock.Object,
                loginValidatorMock.Object);

            service = new AuthenticationManagerService(authLogic, exceptionHandlerMock.Object);
        }

        [Fact]
        public async Task Login_ValidCredentials_ReturnsSuccess()
        {
            var dto = new LoginDto { Email = "user@test.com", Password = "Pwd" };
            playerRepoMock.Setup(x => x.getPlayerByEmailAsync("user@test.com"))
                .ReturnsAsync(new Player { username = "User", password_hash = "Hash", is_verified = true, idPlayer = 1 });
            passwordServiceMock.Setup(x => x.verifyPassword("Pwd", "Hash")).Returns(true);
            sessionManagerMock.Setup(x => x.isUserLoggedIn("User")).Returns(false);

            var result = await service.login(dto);

            Assert.True(result.OperationResult.Success);
        }

        [Fact]
        public async Task Login_ValidCredentials_ReturnsCorrectUsername()
        {
            var dto = new LoginDto { Email = "user@test.com", Password = "Pwd" };
            playerRepoMock.Setup(x => x.getPlayerByEmailAsync("user@test.com"))
                .ReturnsAsync(new Player { username = "User", password_hash = "Hash", is_verified = true, idPlayer = 1 });
            passwordServiceMock.Setup(x => x.verifyPassword("Pwd", "Hash")).Returns(true);
            sessionManagerMock.Setup(x => x.isUserLoggedIn("User")).Returns(false);

            var result = await service.login(dto);

            Assert.Equal("User", result.Username);
        }

        [Fact]
        public async Task Login_Exception_HandlesGracefully()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "LoginOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "Error")));

            playerRepoMock.Setup(x => x.getPlayerByEmailAsync(It.IsAny<string>())).Throws(new Exception());

            try
            {
                var result = await service.login(new LoginDto { Email = "a", Password = "b" });
                Assert.False(result.OperationResult.Success);
            }
            catch (FaultException<ServiceFaultDto>)
            {
                Assert.True(true);
            }
        }

        [Fact]
        public async Task Register_ValidData_ReturnsSuccess()
        {
            var profile = new UserProfileDto { Username = "NewUser", Email = "new@test.com", FirstName = "F" };
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync("NewUser")).ReturnsAsync((Player)null!);
            playerRepoMock.Setup(x => x.getPlayerByEmailAsync("new@test.com")).ReturnsAsync((Player)null!);
            verificationMock.Setup(x => x.generateVerificationCode()).Returns("123456");

            var result = await service.register(profile, "Password123");

            Assert.True(result.Success);
        }

        [Fact]
        public async Task Register_ValidData_CallsAddPlayer()
        {
            var profile = new UserProfileDto { Username = "NewUser", Email = "new@test.com", FirstName = "F" };
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync("NewUser")).ReturnsAsync((Player)null!);
            playerRepoMock.Setup(x => x.getPlayerByEmailAsync("new@test.com")).ReturnsAsync((Player)null!);
            verificationMock.Setup(x => x.generateVerificationCode()).Returns("123456");

            await service.register(profile, "Password123");

            playerRepoMock.Verify(x => x.addPlayer(It.IsAny<Player>()), Times.Once);
        }

        [Fact]
        public async Task VerifyAccount_ValidCode_CallsLogic()
        {
            playerRepoMock.Setup(x => x.getPlayerByEmailAsync("mail"))
                .ReturnsAsync(new Player
                {
                    is_verified = false,
                    verification_code = "123456",
                    code_expiry_date = DateTime.UtcNow.AddMinutes(10)
                });

            var result = await service.verifyAccount("mail", "123456");

            Assert.True(result.Success);
        }

        [Fact]
        public async Task VerifyAccount_Exception_HandlesGracefully()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "VerifyAccountOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "Error")));

            try
            {
                var res = await service.verifyAccount(null, null);
                Assert.False(res.Success);
            }
            catch (FaultException<ServiceFaultDto>)
            {
                Assert.True(true);
            }
        }

        [Fact]
        public async Task ResendVerificationCode_ValidEmail_Delegates()
        {
            playerRepoMock.Setup(x => x.getPlayerByEmailAsync("mail"))
                .ReturnsAsync(new Player { is_verified = false, email = "mail", username = "u" });
            verificationMock.Setup(x => x.generateVerificationCode()).Returns("123456");

            var result = await service.resendVerificationCode("mail");

            Assert.True(result.Success);
        }

        [Fact]
        public async Task ResendVerificationCode_Exception_HandlesGracefully()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "ResendVerificationOperation"))
               .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "Error")));

            try
            {
                var res = await service.resendVerificationCode(null);
                Assert.False(res.Success);
            }
            catch (FaultException<ServiceFaultDto>) { Assert.True(true); }
        }

        [Fact]
        public async Task SendPasswordRecoveryCodeAsync_ValidEmail_Delegates()
        {
            playerRepoMock.Setup(x => x.getPlayerByEmailAsync("mail"))
                .ReturnsAsync(new Player { email = "mail", username = "u", is_verified = true });

            verificationMock.Setup(x => x.generateVerificationCode()).Returns("123456");

            var result = await service.sendPasswordRecoveryCodeAsync("mail");

            Assert.True(result.Success);
        }

        [Fact]
        public async Task SendPasswordRecoveryCodeAsync_Exception_HandlesGracefully()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "SendRecoveryCodeOperation"))
               .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "Error")));

            try
            {
                var res = await service.sendPasswordRecoveryCodeAsync(null);
                Assert.False(res.Success);
            }
            catch (FaultException<ServiceFaultDto>) { Assert.True(true); }
        }

        [Fact]
        public async Task ResetPasswordWithCodeAsync_ValidCode_Delegates()
        {
            playerRepoMock.Setup(x => x.getPlayerByEmailAsync("mail"))
                .ReturnsAsync(new Player
                {
                    verification_code = "123456",
                    code_expiry_date = DateTime.UtcNow.AddMinutes(10)
                });

            var result = await service.resetPasswordWithCodeAsync("mail", "123456", "NewPwd");

            Assert.True(result.Success);
        }

        [Fact]
        public async Task ResetPasswordWithCodeAsync_Exception_HandlesGracefully()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "ResetPasswordOperation"))
               .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "Error")));

            try
            {
                var res = await service.resetPasswordWithCodeAsync(null, null, null);
                Assert.False(res.Success);
            }
            catch (FaultException<ServiceFaultDto>) { Assert.True(true); }
        }

        [Fact]
        public void LogOut_ValidUser_CallsLogic()
        {

            try
            {
                service.logOut("User");
            }
            catch (ArgumentNullException ex) when (ex.Source!.Contains("Autofac") || ex.StackTrace!.Contains("ResolutionExtensions"))
            {
            }

            sessionManagerMock.Verify(x => x.removeSession("User"), Times.Once);
        }

        [Fact]
        public void LogOut_Exception_PropagatesFromLogic()
        {
            sessionManagerMock.Setup(x => x.removeSession(It.IsAny<string>())).Throws(new Exception("Logic Fail"));

            Assert.Throws<Exception>(() => service.logOut("User"));
        }

        [Fact]
        public async Task Register_ValidatorFails_ReturnsFail()
        {
            var failure = new ValidationResult(new[] { new ValidationFailure("Prop", "Error") });
            profileValidatorMock.Setup(x => x.ValidateAsync(It.IsAny<UserProfileDto>(), default))
                .ReturnsAsync(failure);

            var res = await service.register(new UserProfileDto(), "pass");
            Assert.False(res.Success);
        }

        [Fact]
        public async Task Login_AlreadyLoggedIn_ReturnsFail()
        {
            var dto = new LoginDto { Email = "u@t.com", Password = "p" };
            playerRepoMock.Setup(x => x.getPlayerByEmailAsync("u@t.com"))
                .ReturnsAsync(new Player { username = "User", password_hash = "h" });
            passwordServiceMock.Setup(x => x.verifyPassword("p", "h")).Returns(true);
            sessionManagerMock.Setup(x => x.isUserLoggedIn("User")).Returns(true);

            var res = await service.login(dto);
            Assert.False(res.OperationResult.Success);
        }
    }
}
