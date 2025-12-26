using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Abstractions;
using MindWeaveServer.Utilities.Validators;
using NLog; 
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac.Features.OwnedInstances;

namespace MindWeaveServer.BusinessLogic
{
    public class ProfileLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IPlayerRepository playerRepository;
        private readonly IGenderRepository genderRepository;
        private readonly Func<Owned<IStatsRepository>> statsRepositoryFactory;
        private readonly IPasswordService passwordService;
        private readonly IPasswordPolicyValidator passwordPolicyValidator;
        private readonly IValidator<UserProfileForEditDto> profileEditValidator;

        public ProfileLogic(
            IPlayerRepository playerRepository,
            IGenderRepository genderRepository,
            IPasswordService passwordService,
            Func<Owned<IStatsRepository>> statsRepositoryFactory,
            IPasswordPolicyValidator passwordPolicyValidator,
            IValidator<UserProfileForEditDto> profileEditValidator)
        {
            this.playerRepository = playerRepository;
            this.genderRepository = genderRepository;
            this.statsRepositoryFactory = statsRepositoryFactory;
            this.passwordService = passwordService;
            this.passwordPolicyValidator = passwordPolicyValidator;
            this.profileEditValidator = profileEditValidator;
        }

        public async Task<PlayerProfileViewDto> getPlayerProfileViewAsync(string username)
        {
            logger.Info("getPlayerProfileViewAsync called for User: {Username}", username ?? "NULL");

            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("getPlayerProfileViewAsync: Username is null or whitespace.");
                return null; 
            }

         
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

        public async Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username)
        {
            logger.Info("getPlayerProfileForEditAsync called for User: {Username}", username ?? "NULL");

            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("getPlayerProfileForEditAsync: Username is null or whitespace.");
                return null;
            }

           
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
                (g => new GenderDto { IdGender = g.idGender, Name = g.gender1 }).ToList(); 
            logger.Debug("Retrieved {Count} genders.", allGendersDto.Count);

            logger.Info("Successfully retrieved editable profile data for User: {Username}", username);
            return mapToUserProfileForEditDto(player, allGendersDto);
            
        }


        public async Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData)
        {
            logger.Info("updateProfileAsync called for User: {Username}", username ?? "NULL");

            if (updatedProfileData == null)
            {
                logger.Warn("Update profile failed for {Username}: Updated profile data is null.", username ?? "NULL");
                return new OperationResultDto { Success = false, Message = Lang.ValidationProfileOrPasswordRequired };
            }

            logger.Debug("Validating updated profile data for User: {Username}", username);
            var validationResult = await profileEditValidator.ValidateAsync(updatedProfileData);
            if (!validationResult.IsValid)
            {
                string firstError = validationResult.Errors[0].ErrorMessage;
                logger.Warn("Update profile failed for {Username}: Validation failed. Reason: {Reason}", username ?? "NULL", firstError);
                return new OperationResultDto { Success = false, Message = firstError };
            }
            logger.Debug("Profile data validation successful for User: {Username}", username);

            logger.Debug("Fetching player with tracking for update: {Username}", username);
            var playerToUpdate = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

            if (playerToUpdate == null)
            {
                logger.Warn("Update profile failed: Player {Username} not found.", username);
                return new OperationResultDto { Success = false, Message = Lang.ErrorPlayerNotFound };
            }
            logger.Debug("Player {Username} found. Applying updates.", username);

            applyProfileUpdates(playerToUpdate, updatedProfileData);

            logger.Debug("Saving profile changes for User: {Username}", username);
            await playerRepository.saveChangesAsync();
            logger.Info("Profile updated successfully for User: {Username}", username);

            return new OperationResultDto { Success = true, Message = Lang.ProfileUpdatedSuccessfully };
            
        }


        public async Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath)
        {
            logger.Info("updateAvatarPathAsync called for User: {Username}, NewPath: {AvatarPath}", username ?? "NULL", newAvatarPath ?? "NULL");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(newAvatarPath))
            {
                logger.Warn("Update avatar path failed: Username or new path is null/whitespace.");
                return new OperationResultDto { Success = false, Message = Lang.ErrorAvatarPathCannotBeEmpty };
            }

            
            logger.Debug("Fetching player with tracking for avatar update: {Username}", username);
            var playerToUpdate = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

            if (playerToUpdate == null)
            {
                logger.Warn("Update avatar path failed: Player {Username} not found.", username);
                return new OperationResultDto { Success = false, Message = Lang.ErrorPlayerNotFound };
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
                Success = success,
                Message = success ? Lang.SuccessAvatarUpdated : Lang.ErrorAvatarUpdateFailed
            };

        }

        public async Task<OperationResultDto> changePasswordAsync(string username, string currentPassword, string newPassword)
        {
            logger.Info("changePasswordAsync logic called for User: {Username}", username ?? "NULL");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
            {
                logger.Warn("Change password failed for {Username}: One or more fields are null/whitespace.", username ?? "NULL");
                return new OperationResultDto { Success = false, Message = Lang.ErrorAllFieldsRequired };
            }

            logger.Debug("Fetching player with tracking for password change: {Username}", username);
            var player = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

            if (player == null)
            {
                logger.Warn("Change password failed: Player {Username} not found.", username);
                return new OperationResultDto { Success = false, Message = Lang.ErrorPlayerNotFound };
            }

            bool currentPasswordVerified = passwordService.verifyPassword(currentPassword, player.password_hash);

            if (!currentPasswordVerified)
            {
                logger.Warn("Change password failed for {Username}: Current password verification failed.", username);
                return new OperationResultDto { Success = false, Message = Lang.ErrorCurrentPasswordIncorrect };
            }

            var policyValidation = passwordPolicyValidator.validate(newPassword);
            if (!policyValidation.Success)
            {
                logger.Warn("Change password failed for {Username}: New password does not meet policy.", username);
                return policyValidation;
            }

            logger.Debug("Hashing new password for User: {Username}", username);
            string newPasswordHash = passwordService.hashPassword(newPassword);
            player.password_hash = newPasswordHash;
            
            int changes = await playerRepository.saveChangesAsync();

            if (changes > 0)
            {
                logger.Info("Password changed successfully for User: {Username}", username);
                return new OperationResultDto { Success = true, Message = Lang.PasswordChangedSuccessfully };
            }
            else
            {
                logger.Warn("Password change for User {Username} reported 0 changes saved.", username);
                return new OperationResultDto { Success = false, Message = Lang.PasswordChangedFailed };
            }
        }

        public async Task<List<AchievementDto>> getPlayerAchievementsAsync(int playerId)
        {
            logger.Info("getPlayerAchievementsAsync called for PlayerId: {PlayerId}", playerId);

            using (var repoScope = statsRepositoryFactory())
            {
                var repository = repoScope.Value;

                var allAchievements = await repository.getAllAchievementsAsync();
                var unlockedIds = await repository.getPlayerAchievementIdsAsync(playerId);

                var achievementDtos = allAchievements.Select(a => new AchievementDto
                {
                    Id = a.achievements_id,
                    Name = a.name,
                    Description = a.description,
                    IconPath = a.icon_path,
                    IsUnlocked = unlockedIds.Contains(a.achievements_id)
                }).ToList();

                return achievementDtos;
            }
        }

        private PlayerProfileViewDto mapToPlayerProfileViewDto(Player player)
        {
            return new PlayerProfileViewDto
            {
                Username = player.username,
                AvatarPath = player.avatar_path ?? "/Resources/Images/Avatar/default_avatar.png",
                FirstName = player.first_name,
                LastName = player.last_name,
                DateOfBirth = player.date_of_birth,
                Gender = player.Gender?.gender1, 
                Stats = new PlayerStatsDto 
                {
                    PuzzlesCompleted = player.PlayerStats?.puzzles_completed ?? 0,
                    PuzzlesWon = player.PlayerStats?.puzzles_won ?? 0,
                    TotalPlaytime = TimeSpan.FromMinutes(player.PlayerStats?.total_playtime_minutes ?? 0),
                    HighestScore = player.PlayerStats?.highest_score ?? 0
                },
                Achievements = player.Achievements?.Select(ach => new AchievementDto
                {
                    Name = ach.name,
                    Description = ach.description,
                    IconPath = ach.icon_path
                }).ToList() ?? new List<AchievementDto>() 
            };
        }
        private UserProfileForEditDto mapToUserProfileForEditDto(Player player, List<GenderDto> allGendersDto)
        {
            return new UserProfileForEditDto
            {
                FirstName = player.first_name,
                LastName = player.last_name,
                DateOfBirth = player.date_of_birth,
                IdGender = player.gender_id ?? 0, 
                AvailableGenders = allGendersDto 
            };
        }
        private static void applyProfileUpdates(Player player, UserProfileForEditDto updatedProfileData)
        {
          
            player.first_name = updatedProfileData.FirstName.Trim();
            player.last_name = updatedProfileData.LastName?.Trim(); 
            player.date_of_birth = updatedProfileData.DateOfBirth;
            
            player.gender_id = updatedProfileData.IdGender > 0 ? updatedProfileData.IdGender : (int?)null;
        }

    }
}