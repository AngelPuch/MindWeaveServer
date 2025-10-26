using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Abstractions;
using MindWeaveServer.Utilities.Validators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class ProfileLogic
    {
        private readonly IPlayerRepository playerRepository;
        private readonly IGenderRepository genderRepository;
        private readonly IValidator<UserProfileForEditDto> profileEditValidator;
        private readonly IPasswordService passwordService;
        private readonly IPasswordPolicyValidator passwordPolicyValidator;

        public ProfileLogic(
            IPlayerRepository playerRepository,
            IGenderRepository genderRepository,
            IPasswordService passwordService,
            IPasswordPolicyValidator passwordPolicyValidator)
        {
            this.playerRepository = playerRepository ?? throw new ArgumentNullException(nameof(playerRepository));
            this.genderRepository = genderRepository ?? throw new ArgumentNullException(nameof(genderRepository));
            this.passwordService = passwordService ?? throw new ArgumentNullException(nameof(passwordService));
            this.passwordPolicyValidator = passwordPolicyValidator ?? throw new ArgumentNullException(nameof(passwordPolicyValidator));
            this.profileEditValidator = new UserProfileForEditDtoValidator();
        }

        public async Task<PlayerProfileViewDto> getPlayerProfileViewAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            var player = await playerRepository.getPlayerWithProfileViewDataAsync(username);

            if (player == null)
            {
                return null;
            }

            return mapToPlayerProfileViewDto(player);
        }

        public async Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            var player = await playerRepository.getPlayerByUsernameAsync(username); // NoTracking is fine here

            if (player == null)
            {
                return null;
            }

            var allGendersData = await genderRepository.getAllGendersAsync();
            var allGendersDto = allGendersData.Select
                (g => new GenderDto { idGender = g.idGender, name = g.gender1 }).ToList();

            return mapToUserProfileForEditDto(player, allGendersDto);
        }

       
        public async Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData)
        {
            if (updatedProfileData == null)
            {
                return new OperationResultDto { success = false, message = Lang.ValidationProfileOrPasswordRequired }; 
            }

            var validationResult = await profileEditValidator.ValidateAsync(updatedProfileData);
            if (!validationResult.IsValid)
            {
                return new OperationResultDto { success = false, message = validationResult.Errors.First().ErrorMessage };
            }

            try
            {
                var playerToUpdate = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

                if (playerToUpdate == null)
                {
                    return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
                }

                applyProfileUpdates(playerToUpdate, updatedProfileData);
                await playerRepository.saveChangesAsync();

                return new OperationResultDto { success = true, message = Lang.ProfileUpdatedSuccessfully };
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }


        public async Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(newAvatarPath))
            {
                return new OperationResultDto { success = false, message = Lang.ErrorAvatarPathCannotBeEmpty };
            }

            try
            {
                var playerToUpdate = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

                if (playerToUpdate == null)
                {
                    return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
                }

                playerToUpdate.avatar_path = newAvatarPath;
                int changes = await playerRepository.saveChangesAsync();

                return new OperationResultDto
                {
                    success = changes > 0,
                    message = changes > 0 ? Lang.SuccessAvatarUpdated : Lang.ErrorAvatarUpdateFailed
                };
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.ErrorAvatarUpdateFailed };
            }
        }

        public async Task<OperationResultDto> changePasswordAsync(string username, string currentPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
            {
                return new OperationResultDto { success = false, message = Lang.ErrorAllFieldsRequired };
            }

            var player = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

            if (player == null)
            {
                return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
            }

            if (!passwordService.verifyPassword(currentPassword, player.password_hash))
            {
                return new OperationResultDto { success = false, message = Lang.LoginPasswordNotEmpty };
            }

            var policyValidation = passwordPolicyValidator.validate(newPassword);
            if (!policyValidation.success)
            {
                return policyValidation;
            }

            string newPasswordHash = passwordService.hashPassword(newPassword);
            player.password_hash = newPasswordHash;

            try
            {
                int changes = await playerRepository.saveChangesAsync();
                return new OperationResultDto
                {
                    success = changes > 0,
                    message = changes > 0 ? Lang.PasswordChangedSuccessfully: Lang.PasswordChangedFailed
                };
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        private PlayerProfileViewDto mapToPlayerProfileViewDto(Player player)
        {
            return new PlayerProfileViewDto
            {
                username = player.username,
                avatarPath = player.avatar_path ?? "/Resources/Images/Avatar/default_avatar.png",
                firstName = player.first_name,
                lastName = player.last_name,
                dateOfBirth = player.date_of_birth,
                gender = player.Gender?.gender1,
                stats = new PlayerStatsDto
                {
                    puzzlesCompleted = player.PlayerStats?.puzzles_completed ?? 0,
                    puzzlesWon = player.PlayerStats?.puzzles_won ?? 0,
                    totalPlaytime = TimeSpan.FromMinutes(player.PlayerStats?.total_playtime_minutes ?? 0),
                    highestScore = player.PlayerStats?.highest_score ?? 0
                },
                achievements = player.Achievements?.Select(ach => new AchievementDto
                {
                    name = ach.name,
                    description = ach.description,
                    iconPath = ach.icon_path
                }).ToList() ?? new List<AchievementDto>()
            };
        }
        private UserProfileForEditDto mapToUserProfileForEditDto(Player player, List<GenderDto> allGendersDto)
        {
            return new UserProfileForEditDto
            {
                firstName = player.first_name,
                lastName = player.last_name,
                dateOfBirth = player.date_of_birth,
                idGender = player.gender_id ?? 0,
                availableGenders = allGendersDto
            };
        }
        private void applyProfileUpdates(Player player, UserProfileForEditDto updatedProfileData)
        {
            player.first_name = updatedProfileData.firstName.Trim();
            player.last_name = updatedProfileData.lastName?.Trim();
            player.date_of_birth = updatedProfileData.dateOfBirth;
            player.gender_id = updatedProfileData.idGender;
        }

    }
}