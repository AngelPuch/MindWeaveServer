using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentValidation;
using FluentValidation.Results;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Utilities.Abstractions;

namespace MindWeaveServer.Tests.BusinessLogic
{
    public class ProfileLogicTests
    {
        private readonly Mock<IPlayerRepository> playerRepositoryMock;
        private readonly Mock<IGenderRepository> genderRepositoryMock;
        private readonly Mock<IStatsRepository> statsRepositoryMock;
        private readonly Mock<IPasswordService> passwordServiceMock;
        private readonly Mock<IPasswordPolicyValidator> passwordPolicyValidatorMock;
        private readonly Mock<IValidator<UserProfileForEditDto>> profileEditValidatorMock;

        private readonly ProfileLogic profileLogic;

        public ProfileLogicTests()
        {
            playerRepositoryMock = new Mock<IPlayerRepository>();
            genderRepositoryMock = new Mock<IGenderRepository>();
            statsRepositoryMock = new Mock<IStatsRepository>();
            passwordServiceMock = new Mock<IPasswordService>();
            passwordPolicyValidatorMock = new Mock<IPasswordPolicyValidator>();
            profileEditValidatorMock = new Mock<IValidator<UserProfileForEditDto>>();

            profileEditValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<UserProfileForEditDto>(), default))
                .ReturnsAsync(new ValidationResult());
            passwordPolicyValidatorMock.Setup(v => v.validate(It.IsAny<string>()))
                .Returns(new OperationResultDto { Success = true });

            profileLogic = new ProfileLogic(
                playerRepositoryMock.Object,
                genderRepositoryMock.Object,
                statsRepositoryMock.Object,
                passwordServiceMock.Object,
                passwordPolicyValidatorMock.Object,
                profileEditValidatorMock.Object
            );
        }


        [Fact]
        public async Task GetPlayerProfileViewAsync_NullUser_ReturnsNull()
        {
            var result = await profileLogic.getPlayerProfileViewAsync(null);
            Assert.Null(result);
        }

        [Fact]
        public async Task GetPlayerProfileViewAsync_UserNotFound_ReturnsNull()
        {
            playerRepositoryMock.Setup(r => r.getPlayerWithProfileViewDataAsync("Unknown")).ReturnsAsync((Player)null!);
            var result = await profileLogic.getPlayerProfileViewAsync("Unknown");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetPlayerProfileViewAsync_ValidUser_MapsData()
        {
            var player = new Player { username = "U", first_name = "F", PlayerStats = new PlayerStats { highest_score = 100 } };
            playerRepositoryMock.Setup(r => r.getPlayerWithProfileViewDataAsync("U")).ReturnsAsync(player);

            var result = await profileLogic.getPlayerProfileViewAsync("U");

            Assert.Equal("F", result.FirstName);
        }

        [Fact]
        public async Task GetPlayerProfileViewAsync_NoAchievements_ReturnsEmptyList()
        {
            var player = new Player { username = "U" };
            playerRepositoryMock.Setup(r => r.getPlayerWithProfileViewDataAsync("U")).ReturnsAsync(player);

            var result = await profileLogic.getPlayerProfileViewAsync("U");
            Assert.Empty(result.Achievements);
        }

        [Fact]
        public async Task GetPlayerProfileViewAsync_NullAvatar_ReturnsDefault()
        {
            var player = new Player { username = "U", avatar_path = null };
            playerRepositoryMock.Setup(r => r.getPlayerWithProfileViewDataAsync("U")).ReturnsAsync(player);
            var result = await profileLogic.getPlayerProfileViewAsync("U");
            Assert.Contains("default_avatar", result.AvatarPath);
        }


        [Fact]
        public async Task GetPlayerProfileForEditAsync_UserNotFound_ReturnsNull()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameAsync("U")).ReturnsAsync((Player)null!);
            var result = await profileLogic.getPlayerProfileForEditAsync("U");
            Assert.Null(result);
        }


        [Fact]
        public async Task UpdateProfileAsync_NullDto_ReturnsError()
        {
            var result = await profileLogic.updateProfileAsync("U", null);
            Assert.False(result.Success);
        }

        [Fact]
        public async Task UpdateProfileAsync_ValidationFail_ReturnsError()
        {
            profileEditValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<UserProfileForEditDto>(), default))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("F", "Err") }));

            var result = await profileLogic.updateProfileAsync("U", new UserProfileForEditDto());
            Assert.False(result.Success);
        }

        [Fact]
        public async Task UpdateProfileAsync_PlayerNotFound_ReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameWithTrackingAsync("U")).ReturnsAsync((Player)null!);
            var result = await profileLogic.updateProfileAsync("U", new UserProfileForEditDto());
            Assert.False(result.Success);
        }

        [Fact]
        public async Task UpdateProfileAsync_ValidData_UpdatesFields()
        {
            var player = new Player { first_name = "Old" };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameWithTrackingAsync("U")).ReturnsAsync(player);

            var dto = new UserProfileForEditDto { FirstName = "New", LastName = "L", DateOfBirth = DateTime.Now };

            var result = await profileLogic.updateProfileAsync("U", dto);

            Assert.Equal("New", player.first_name);
        }

        [Fact]
        public async Task UpdateProfileAsync_ZeroGender_SetsNull()
        {
            var player = new Player { gender_id = 1 };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameWithTrackingAsync("U")).ReturnsAsync(player);
            var dto = new UserProfileForEditDto { IdGender = 0, FirstName = "F" };

            await profileLogic.updateProfileAsync("U", dto);

            Assert.Null(player.gender_id);
        }


        [Fact]
        public async Task UpdateAvatarPathAsync_NullUsername_ReturnsError()
        {
            var result = await profileLogic.updateAvatarPathAsync(null, "path");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task UpdateAvatarPathAsync_UserNotFound_ReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameWithTrackingAsync("U")).ReturnsAsync((Player)null!);
            var result = await profileLogic.updateAvatarPathAsync("U", "path");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task UpdateAvatarPathAsync_ValidData_UpdatesPath()
        {
            var player = new Player { avatar_path = "old" };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameWithTrackingAsync("U")).ReturnsAsync(player);

            var result = await profileLogic.updateAvatarPathAsync("U", "new");

            Assert.Equal("new", player.avatar_path);
        }


        [Fact]
        public async Task ChangePasswordAsync_UserNotFound_ReturnsError()
        {
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameWithTrackingAsync("U")).ReturnsAsync((Player)null!);
            var result = await profileLogic.changePasswordAsync("U", "Old", "New");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task ChangePasswordAsync_WrongOldPassword_ReturnsError()
        {
            var player = new Player { password_hash = "Hash" };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameWithTrackingAsync("U")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Wrong", "Hash")).Returns(false);

            var result = await profileLogic.changePasswordAsync("U", "Wrong", "New");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task ChangePasswordAsync_WeakNewPassword_ReturnsError()
        {
            var player = new Player { password_hash = "Hash" };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameWithTrackingAsync("U")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Old", "Hash")).Returns(true);
            passwordPolicyValidatorMock.Setup(v => v.validate("weak")).Returns(new OperationResultDto { Success = false });

            var result = await profileLogic.changePasswordAsync("U", "Old", "weak");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task ChangePasswordAsync_ValidData_UpdatesHash()
        {
            var player = new Player { password_hash = "Hash" };
            playerRepositoryMock.Setup(r => r.getPlayerByUsernameWithTrackingAsync("U")).ReturnsAsync(player);
            passwordServiceMock.Setup(p => p.verifyPassword("Old", "Hash")).Returns(true);
            passwordServiceMock.Setup(p => p.hashPassword("New")).Returns("NewHash");

            var result = await profileLogic.changePasswordAsync("U", "Old", "New");

            Assert.Equal("NewHash", player.password_hash);
        }

        [Fact]
        public async Task GetPlayerAchievementsAsync_ValidId_ReturnsMappedList()
        {
            statsRepositoryMock.Setup(s => s.getAllAchievementsAsync()).ReturnsAsync(new List<Achievements> { new Achievements { achievements_id = 1, name = "A1" } });
            statsRepositoryMock.Setup(s => s.getPlayerAchievementIdsAsync(1)).ReturnsAsync(new List<int> { 1 });

            var result = await profileLogic.getPlayerAchievementsAsync(1);

            Assert.True(result[0].IsUnlocked);
        }
    }
}
