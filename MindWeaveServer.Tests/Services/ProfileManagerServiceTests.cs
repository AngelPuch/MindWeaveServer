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
        public async Task GetPlayerProfileView_ValidUser_ReturnsDto()
        {
            playerRepoMock.Setup(x => x.getPlayerWithProfileViewDataAsync("User"))
                .ReturnsAsync(new Player { username = "User", PlayerStats = new PlayerStats(), Gender = new Gender { gender1 = "M" } });

            var result = await service.getPlayerProfileView("User");

            Assert.Equal("User", result.Username);
        }

        [Fact]
        public async Task GetPlayerProfileView_Exception_HandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "GetPlayerProfileViewOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            var res = await service.getPlayerProfileView(null);
            Assert.Null(res);
        }



        [Fact]
        public async Task GetPlayerProfileForEditAsync_Exception_HandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "GetPlayerProfileForEditOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            var res = await service.getPlayerProfileForEditAsync(null);
            Assert.Null(res);
        }

        [Fact]
        public async Task UpdateProfileAsync_ValidData_CallsLogic()
        {
            var dto = new UserProfileForEditDto { IdGender = 1, FirstName = "A" };
            playerRepoMock.Setup(x => x.getPlayerByUsernameWithTrackingAsync("User"))
                .ReturnsAsync(new Player { username = "User" });

            var result = await service.updateProfileAsync("User", dto);

            playerRepoMock.Verify(x => x.updatePlayerAsync(It.IsAny<Player>()), Times.Once);
        }

        [Fact]
        public async Task UpdateProfileAsync_Exception_HandlesError()
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
        public async Task UpdateAvatarPathAsync_ValidData_CallsLogic()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameWithTrackingAsync("User"))
                .ReturnsAsync(new Player());

            var result = await service.updateAvatarPathAsync("User", "path.png");

            Assert.True(result.Success);
        }

        [Fact]
        public async Task UpdateAvatarPathAsync_Exception_HandlesError()
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
        public async Task ChangePasswordAsync_ValidData_VerifiesAndSetsNew()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameWithTrackingAsync("User"))
                .ReturnsAsync(new Player { password_hash = "OldHash" });
            passwordServiceMock.Setup(x => x.verifyPassword("Old", "OldHash")).Returns(true);
            passwordServiceMock.Setup(x => x.hashPassword("New")).Returns("NewHash");

            var result = await service.changePasswordAsync("User", "Old", "New");

            playerRepoMock.Verify(x => x.updatePlayerAsync(It.Is<Player>(p => p.password_hash == "NewHash")), Times.Once);
        }

        [Fact]
        public async Task ChangePasswordAsync_Exception_HandlesError()
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
        public async Task GetPlayerAchievementsAsync_ValidId_ReturnsList()
        {
            statsRepoMock.Setup(x => x.getPlayerAchievementIdsAsync(1)).ReturnsAsync(new List<int> { 1 });
            statsRepoMock.Setup(x => x.getAllAchievementsAsync())
                .ReturnsAsync(new List<Achievements> { new Achievements { achievements_id = 1, name = "A" } });

            var result = await service.getPlayerAchievementsAsync(1);

            Assert.Equal("A", result[0].Name);
        }

        [Fact]
        public async Task GetPlayerAchievementsAsync_Exception_HandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "GetPlayerAchievementsOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.getPlayerAchievementsAsync(0));
        }

        [Fact]
        public async Task UpdateProfileAsync_UserNotFound_ReturnsFalse()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameWithTrackingAsync("Ghost")).ReturnsAsync((Player)null!);
            var res = await service.updateProfileAsync("Ghost", new UserProfileForEditDto());
            Assert.False(res.Success);
        }

        [Fact]
        public async Task ChangePasswordAsync_InvalidOldPassword_ReturnsFalse()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameWithTrackingAsync("User"))
                .ReturnsAsync(new Player { password_hash = "Hash" });
            passwordServiceMock.Setup(x => x.verifyPassword("Wrong", "Hash")).Returns(false);

            var res = await service.changePasswordAsync("User", "Wrong", "New");
            Assert.False(res.Success);
        }

        [Fact]
        public async Task GetPlayerProfileForEditAsync_UserNotFound_ReturnsNull()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync("Ghost")).ReturnsAsync((Player)null!);
            var res = await service.getPlayerProfileForEditAsync("Ghost");
            Assert.Null(res);
        }
    }
}
