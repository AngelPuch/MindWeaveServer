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
using NLog; 

namespace MindWeaveServer.BusinessLogic
{
    public class ProfileLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger(); 

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
            logger.Info("ProfileLogic instance created.");
        }

        public async Task<PlayerProfileViewDto> getPlayerProfileViewAsync(string username)
        {
            logger.Info("getPlayerProfileViewAsync called for User: {Username}", username ?? "NULL");

            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("getPlayerProfileViewAsync: Username is null or whitespace.");
                return null; 
            }

            try
            {
                logger.Debug("Fetching player with profile view data for User: {Username}", username);
                var player = await playerRepository.getPlayerWithProfileViewDataAsync(username);

                if (player == null)
                {
                    logger.Warn("getPlayerProfileViewAsync: Player not found for User: {Username}", username);
                    return null; 
                }

                logger.Info("Successfully retrieved profile view data for User: {Username}", username);
                
                return mapToPlayerProfileViewDto(player);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during getPlayerProfileViewAsync for User: {Username}", username);
                return null; 
            }
        }

        public async Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username)
        {
            logger.Info("getPlayerProfileForEditAsync called for User: {Username}", username ?? "NULL");

            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("getPlayerProfileForEditAsync: Username is null or whitespace.");
                return null;
            }

            try
            {
                logger.Debug("Fetching player data for editing for User: {Username}", username);
                var player = await playerRepository.getPlayerByUsernameAsync(username); 

                if (player == null)
                {
                    logger.Warn("getPlayerProfileForEditAsync: Player not found for User: {Username}", username);
                    return null;
                }
                logger.Debug("Player found. Fetching all genders.");

                var allGendersData = await genderRepository.getAllGendersAsync();
                var allGendersDto = allGendersData.Select
                    (g => new GenderDto { idGender = g.idGender, name = g.gender1 }).ToList(); 
                logger.Debug("Retrieved {Count} genders.", allGendersDto.Count);

                logger.Info("Successfully retrieved editable profile data for User: {Username}", username);
                return mapToUserProfileForEditDto(player, allGendersDto);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during getPlayerProfileForEditAsync for User: {Username}", username);
                return null;
            }
        }


        public async Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData)
        {
            logger.Info("updateProfileAsync called for User: {Username}", username ?? "NULL");

            if (updatedProfileData == null)
            {
                logger.Warn("Update profile failed for {Username}: Updated profile data is null.", username ?? "NULL");
                return new OperationResultDto { success = false, message = Lang.ValidationProfileOrPasswordRequired };
            }

            logger.Debug("Validating updated profile data for User: {Username}", username);
            var validationResult = await profileEditValidator.ValidateAsync(updatedProfileData);
            if (!validationResult.IsValid)
            {
                string firstError = validationResult.Errors[0].ErrorMessage;
                logger.Warn("Update profile failed for {Username}: Validation failed. Reason: {Reason}", username ?? "NULL", firstError);
                return new OperationResultDto { success = false, message = firstError };
            }
            logger.Debug("Profile data validation successful for User: {Username}", username);

            try
            {
                logger.Debug("Fetching player with tracking for update: {Username}", username);
                var playerToUpdate = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

                if (playerToUpdate == null)
                {
                    logger.Warn("Update profile failed: Player {Username} not found.", username);
                    return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
                }
                logger.Debug("Player {Username} found. Applying updates.", username);

                applyProfileUpdates(playerToUpdate, updatedProfileData);

                logger.Debug("Saving profile changes for User: {Username}", username);
                await playerRepository.saveChangesAsync();
                logger.Info("Profile updated successfully for User: {Username}", username);

                return new OperationResultDto { success = true, message = Lang.ProfileUpdatedSuccessfully };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during updateProfileAsync for User: {Username}", username ?? "NULL");
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }


        public async Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath)
        {
            logger.Info("updateAvatarPathAsync called for User: {Username}, NewPath: {AvatarPath}", username ?? "NULL", newAvatarPath ?? "NULL");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(newAvatarPath))
            {
                logger.Warn("Update avatar path failed: Username or new path is null/whitespace.");
                return new OperationResultDto { success = false, message = Lang.ErrorAvatarPathCannotBeEmpty };
            }

            try
            {
                logger.Debug("Fetching player with tracking for avatar update: {Username}", username);
                var playerToUpdate = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

                if (playerToUpdate == null)
                {
                    logger.Warn("Update avatar path failed: Player {Username} not found.", username);
                    return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
                }

                logger.Debug("Player {Username} found. Updating avatar path.", username);
                playerToUpdate.avatar_path = newAvatarPath; 

                logger.Debug("Saving avatar path change for User: {Username}", username);
                int changes = await playerRepository.saveChangesAsync();
                logger.Debug("SaveChanges result for avatar update: {ChangesCount}", changes);

                bool success = changes > 0;
                if (success)
                    logger.Info("Avatar path updated successfully for User: {Username}", username);
                else
                    logger.Warn("Avatar path update for User {Username} reported 0 changes saved.", username);


                return new OperationResultDto
                {
                    success = success,
                    message = success ? Lang.SuccessAvatarUpdated : Lang.ErrorAvatarUpdateFailed
                };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during updateAvatarPathAsync for User: {Username}", username);
                return new OperationResultDto { success = false, message = Lang.ErrorAvatarUpdateFailed };
            }
        }

        public async Task<OperationResultDto> changePasswordAsync(string username, string currentPassword, string newPassword)
        {
            logger.Info("changePasswordAsync called for User: {Username}", username ?? "NULL");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
            {
                logger.Warn("Change password failed for {Username}: One or more fields are null/whitespace.", username ?? "NULL");
                return new OperationResultDto { success = false, message = Lang.ErrorAllFieldsRequired };
            }

            try
            {
                logger.Debug("Fetching player with tracking for password change: {Username}", username);
                var player = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

                if (player == null)
                {
                    logger.Warn("Change password failed: Player {Username} not found.", username);
                    return new OperationResultDto { success = false, message = Lang.ErrorPlayerNotFound };
                }

                bool currentPasswordVerified = passwordService.verifyPassword(currentPassword, player.password_hash);

                if (!currentPasswordVerified)
                {
                    logger.Warn("Change password failed for {Username}: Current password verification failed.", username);
                    return new OperationResultDto { success = false, message = Lang.ErrorCurrentPasswordIncorrect };
                }

                var policyValidation = passwordPolicyValidator.validate(newPassword);

                if (!policyValidation.success)
                {
                    logger.Warn("Change password failed for {Username}: New password does not meet policy. Reason: {Reason}", username, policyValidation.message);
                    return policyValidation; 
                }

                logger.Debug("Hashing new password for User: {Username}", username);
                string newPasswordHash = passwordService.hashPassword(newPassword);
                player.password_hash = newPasswordHash;
                int changes = await playerRepository.saveChangesAsync();
                logger.Debug("SaveChanges result for password change: {ChangesCount}", changes);

                bool success = changes > 0;
                if (success)
                    logger.Info("Password changed successfully for User: {Username}", username);
                else
                    logger.Warn("Password change for User {Username} reported 0 changes saved.", username);

                return new OperationResultDto
                {
                    success = success,
                    message = success ? Lang.PasswordChangedSuccessfully : Lang.PasswordChangedFailed
                };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during changePasswordAsync for User: {Username}", username);
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
        private static void applyProfileUpdates(Player player, UserProfileForEditDto updatedProfileData)
        {
          
            player.first_name = updatedProfileData.firstName.Trim();
            player.last_name = updatedProfileData.lastName?.Trim(); 
            player.date_of_birth = updatedProfileData.dateOfBirth;
            
            player.gender_id = updatedProfileData.idGender > 0 ? updatedProfileData.idGender : (int?)null;
        }

    }
}