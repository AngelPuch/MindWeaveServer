using FluentValidation;
using FluentValidation.Results;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Services;
using MindWeaveServer.Utilities.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;
using Xunit;

namespace MindWeaveServer.Tests.Services
{
    public class ProfileManagerServiceTests
    {
        private readonly Mock<IPlayerRepository> playerRepoMock;
        private readonly Mock<IGenderRepository> genderRepoMock;
        private readonly Mock<IStatsRepository> statsRepoMock;
        private readonly Mock<IPasswordService> passwordServiceMock;
        private readonly Mock<IPasswordPolicyValidator> passwordPolicyMock;
        private readonly Mock<IValidator<UserProfileForEditDto>> editValidatorMock;
        private readonly Mock<IServiceExceptionHandler> exceptionHandlerMock;

        private readonly ProfileLogic logic;
        private readonly ProfileManagerService service;

        public ProfileManagerServiceTests()
        {
            playerRepoMock = new Mock<IPlayerRepository>();
            genderRepoMock = new Mock<IGenderRepository>();
            statsRepoMock = new Mock<IStatsRepository>();
            passwordServiceMock = new Mock<IPasswordService>();
            passwordPolicyMock = new Mock<IPasswordPolicyValidator>();
            editValidatorMock = new Mock<IValidator<UserProfileForEditDto>>();
            exceptionHandlerMock = new Mock<IServiceExceptionHandler>();

            editValidatorMock.Setup(x => x.ValidateAsync(It.IsAny<UserProfileForEditDto>(), default))
                .ReturnsAsync(new ValidationResult());
            passwordPolicyMock.Setup(x => x.validate(It.IsAny<string>()))
                .Returns(new OperationResultDto { Success = true });

            logic = new ProfileLogic(
                playerRepoMock.Object,
                genderRepoMock.Object,
                statsRepoMock.Object,
                passwordServiceMock.Object,
                passwordPolicyMock.Object,
                editValidatorMock.Object);

            service = new ProfileManagerService(logic, exceptionHandlerMock.Object);
        }

        [Fact]
        public async Task getPlayerProfileViewReturnsDto()
        {
            playerRepoMock.Setup(x => x.getPlayerWithProfileViewDataAsync("User"))
                .ReturnsAsync(new Player { username = "User", PlayerStats = new PlayerStats(), Gender = new Gender { gender1 = "M" } });

            var result = await service.getPlayerProfileView("User");

            Assert.NotNull(result);
            Assert.Equal("User", result.Username);
        }

        [Fact]
        public async Task getPlayerProfileViewHandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "GetPlayerProfileViewOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            var res = await service.getPlayerProfileView(null);
            Assert.Null(res);
        }

        [Fact]
        public async Task getPlayerProfileForEditAsyncReturnsDto()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync("User"))
                .ReturnsAsync(new Player { username = "User", email = "e@e.com", Gender = new Gender() });
            genderRepoMock.Setup(x => x.getAllGendersAsync()).ReturnsAsync(new List<Gender>());

            var result = await service.getPlayerProfileForEditAsync("User");

            Assert.NotNull(result);
        }

        [Fact]
        public async Task getPlayerProfileForEditAsyncHandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "GetPlayerProfileForEditOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            var res = await service.getPlayerProfileForEditAsync(null);
            Assert.Null(res);
        }

        [Fact]
        public async Task updateProfileAsyncCallsLogic()
        {
            var dto = new UserProfileForEditDto { IdGender = 1, FirstName = "A" };
            playerRepoMock.Setup(x => x.getPlayerByUsernameWithTrackingAsync("User"))
                .ReturnsAsync(new Player { username = "User" });

            var result = await service.updateProfileAsync("User", dto);

            Assert.True(result.Success);
            playerRepoMock.Verify(x => x.updatePlayerAsync(It.IsAny<Player>()), Times.Once);
        }

        [Fact]
        public async Task updateProfileAsyncHandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "UpdateProfileOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            try
            {
                var res = await service.updateProfileAsync("User", null);
                Assert.False(res.Success);
            }
            catch (FaultException<ServiceFaultDto>) { Assert.True(true); }
        }

        [Fact]
        public async Task updateAvatarPathAsyncCallsLogic()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameWithTrackingAsync("User"))
                .ReturnsAsync(new Player());

            var result = await service.updateAvatarPathAsync("User", "path.png");

            Assert.True(result.Success);
        }

        [Fact]
        public async Task updateAvatarPathAsyncHandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "UpdateAvatarPathOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            try
            {
                var res = await service.updateAvatarPathAsync(null, null);
                Assert.False(res.Success);
            }
            catch (FaultException<ServiceFaultDto>) { Assert.True(true); }
        }

        [Fact]
        public async Task changePasswordAsyncVerifiesOldAndSetsNew()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameWithTrackingAsync("User"))
                .ReturnsAsync(new Player { password_hash = "OldHash" });
            passwordServiceMock.Setup(x => x.verifyPassword("Old", "OldHash")).Returns(true);
            passwordServiceMock.Setup(x => x.hashPassword("New")).Returns("NewHash");

            var result = await service.changePasswordAsync("User", "Old", "New");

            Assert.True(result.Success);
            playerRepoMock.Verify(x => x.updatePlayerAsync(It.Is<Player>(p => p.password_hash == "NewHash")), Times.Once);
        }

        [Fact]
        public async Task changePasswordAsyncHandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "ChangePasswordOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            try
            {
                var res = await service.changePasswordAsync(null, null, null);
                Assert.False(res.Success);
            }
            catch (FaultException<ServiceFaultDto>) { Assert.True(true); }
        }

        [Fact]
        public async Task getPlayerAchievementsAsyncReturnsList()
        {
            statsRepoMock.Setup(x => x.getPlayerAchievementIdsAsync(1)).ReturnsAsync(new List<int> { 1 });
            statsRepoMock.Setup(x => x.getAllAchievementsAsync())
                .ReturnsAsync(new List<Achievements> { new Achievements { achievements_id = 1, name = "A" } });

            var result = await service.getPlayerAchievementsAsync(1);

            Assert.Single(result);
            Assert.Equal("A", result[0].Name);
        }

        [Fact]
        public async Task getPlayerAchievementsAsyncHandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "GetPlayerAchievementsOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.getPlayerAchievementsAsync(0));
        }

        [Fact]
        public async Task updateProfileAsyncReturnsFalseIfUserNotFound()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameWithTrackingAsync("Ghost")).ReturnsAsync((Player)null!);
            var res = await service.updateProfileAsync("Ghost", new UserProfileForEditDto());
            Assert.False(res.Success);
        }

        [Fact]
        public async Task changePasswordAsyncReturnsFalseIfOldInvalid()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameWithTrackingAsync("User"))
                .ReturnsAsync(new Player { password_hash = "Hash" });
            passwordServiceMock.Setup(x => x.verifyPassword("Wrong", "Hash")).Returns(false);

            var res = await service.changePasswordAsync("User", "Wrong", "New");
            Assert.False(res.Success);
        }

        [Fact]
        public async Task getPlayerProfileForEditAsyncReturnsNullIfNotFound()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync("Ghost")).ReturnsAsync((Player)null!);
            var res = await service.getPlayerProfileForEditAsync("Ghost");
            Assert.Null(res);
        }
    }
}