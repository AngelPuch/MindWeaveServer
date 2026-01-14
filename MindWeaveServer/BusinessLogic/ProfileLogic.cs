using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Utilities.Abstractions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class ProfileLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private const string DEFAULT_AVATAR_PATH = "/Resources/Images/Avatar/default_avatar.png";
        private const string UNKNOWN_PLATFORM_NAME = "Unknown";

        private readonly IPlayerRepository playerRepository;
        private readonly IGenderRepository genderRepository;
        private readonly IStatsRepository statsRepository;
        private readonly IPasswordService passwordService;
        private readonly IPasswordPolicyValidator passwordPolicyValidator;
        private readonly IValidator<UserProfileForEditDto> profileEditValidator;

        public ProfileLogic(
            IPlayerRepository playerRepository,
            IGenderRepository genderRepository,
            IStatsRepository statsRepository,
            IPasswordService passwordService,
            IPasswordPolicyValidator passwordPolicyValidator,
            IValidator<UserProfileForEditDto> profileEditValidator)
        {
            this.playerRepository = playerRepository;
            this.genderRepository = genderRepository;
            this.statsRepository = statsRepository;
            this.passwordService = passwordService;
            this.passwordPolicyValidator = passwordPolicyValidator;
            this.profileEditValidator = profileEditValidator;
        }

        public async Task<PlayerProfileViewDto> getPlayerProfileViewAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("getPlayerProfileViewAsync: Username is null or whitespace.");
                return null;
            }

            var player = await playerRepository.getPlayerWithProfileViewDataAsync(username);

            if (player == null)
            {
                logger.Warn("getPlayerProfileViewAsync: Player not found.");
                return null;
            }

            return mapToPlayerProfileViewDto(player);
        }

        public async Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("getPlayerProfileForEditAsync: Username is null or whitespace.");
                return null;
            }

            var player = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

            if (player == null)
            {
                logger.Warn("getPlayerProfileForEditAsync: Player not found.");
                return null;
            }

            var allGendersData = await genderRepository.getAllGendersAsync();
            var allGendersDto = mapToGenderDtoList(allGendersData);
            var allPlatforms = await playerRepository.getAllSocialMediaPlatformsAsync();

            return mapToUserProfileForEditDto(player, allGendersDto, allPlatforms);
        }

        public async Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData)
        {
            if (updatedProfileData == null)
            {
                logger.Warn("updateProfileAsync: Updated profile data is null.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_PROFILE_PASSWORD_REQUIRED
                };
            }

            var validationResult = await profileEditValidator.ValidateAsync(updatedProfileData);
            if (!validationResult.IsValid)
            {
                var firstError = validationResult.Errors[0];
                logger.Warn("updateProfileAsync: Validation failed.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = firstError.ErrorCode
                };
            }

            var playerToUpdate = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

            if (playerToUpdate == null)
            {
                logger.Warn("updateProfileAsync: Player not found.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.PROFILE_NOT_FOUND
                };
            }

            applyProfileUpdates(playerToUpdate, updatedProfileData);

            await playerRepository.updatePlayerProfileWithSocialsAsync(playerToUpdate);
            return new OperationResultDto
            {
                Success = true,
                MessageCode = MessageCodes.PROFILE_UPDATE_SUCCESS
            };
        }

        public async Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(newAvatarPath))
            {
                logger.Warn("updateAvatarPathAsync: Username or new path is null/whitespace.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_FIELDS_REQUIRED
                };
            }

            var playerToUpdate = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

            if (playerToUpdate == null)
            {
                logger.Warn("updateAvatarPathAsync: Player not found.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.PROFILE_NOT_FOUND
                };
            }

            playerToUpdate.avatar_path = newAvatarPath;

            await playerRepository.updatePlayerAsync(playerToUpdate);

            return new OperationResultDto
            {
                Success = true,
                MessageCode = MessageCodes.PROFILE_AVATAR_UPDATE_SUCCESS
            };
        }

        public async Task<OperationResultDto> changePasswordAsync(string username, string currentPassword, string newPassword)
        {
            var inputValidation = validateChangePasswordInput(username, currentPassword, newPassword);
            if (!inputValidation.Success)
            {
                return inputValidation;
            }

            var player = await playerRepository.getPlayerByUsernameWithTrackingAsync(username);

            if (player == null)
            {
                logger.Warn("changePasswordAsync: Player not found.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.PROFILE_NOT_FOUND
                };
            }

            bool currentPasswordVerified = passwordService.verifyPassword(currentPassword, player.password_hash);

            if (!currentPasswordVerified)
            {
                logger.Warn("changePasswordAsync: Current password verification failed.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.PROFILE_CURRENT_PASSWORD_INCORRECT
                };
            }

            var policyValidation = passwordPolicyValidator.validate(newPassword);
            if (!policyValidation.Success)
            {
                logger.Warn("changePasswordAsync: New password does not meet policy.");
                if (string.IsNullOrEmpty(policyValidation.MessageCode))
                {
                    policyValidation.MessageCode = MessageCodes.VALIDATION_PASSWORD_TOO_WEAK;
                }
                return policyValidation;
            }

            player.password_hash = passwordService.hashPassword(newPassword);

            await playerRepository.updatePlayerAsync(player);

            return new OperationResultDto
            {
                Success = true,
                MessageCode = MessageCodes.PROFILE_PASSWORD_CHANGE_SUCCESS
            };
        }

        public async Task<List<AchievementDto>> getPlayerAchievementsAsync(int playerId)
        {
            var allAchievements = await statsRepository.getAllAchievementsAsync();
            var unlockedIds = await statsRepository.getPlayerAchievementIdsAsync(playerId);

            var achievementList = allAchievements.Select(a => new AchievementDto
            {
                Id = a.achievements_id,
                Name = a.name,
                Description = a.description,
                IconPath = a.icon_path,
                IsUnlocked = unlockedIds.Contains(a.achievements_id)
            }).ToList();

            return achievementList;
        }

        private OperationResultDto validateChangePasswordInput(string username, string currentPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
            {
                logger.Warn("changePasswordAsync: One or more fields are null/whitespace.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_FIELDS_REQUIRED
                };
            }

            return new OperationResultDto { Success = true };
        }

        private static List<GenderDto> mapToGenderDtoList(IEnumerable<Gender> genders)
        {
            return genders.Select(g => new GenderDto
            {
                IdGender = g.idGender,
                Name = g.gender1
            }).ToList();
        }

        private static PlayerProfileViewDto mapToPlayerProfileViewDto(Player player)
        {
            return new PlayerProfileViewDto
            {
                Username = player.username,
                AvatarPath = player.avatar_path ?? DEFAULT_AVATAR_PATH,
                FirstName = player.first_name,
                LastName = player.last_name,
                DateOfBirth = player.date_of_birth,
                Gender = player.Gender?.gender1,
                Stats = mapToPlayerStatsDto(player.PlayerStats),
                Achievements = mapToAchievementDtoList(player.Achievements),
                SocialMedia = mapToPlayerSocialMediaDtoList(player.PlayerSocialMedias)
            };
        }

        private static PlayerStatsDto mapToPlayerStatsDto(PlayerStats stats)
        {
            return new PlayerStatsDto
            {
                PuzzlesCompleted = stats?.puzzles_completed ?? 0,
                PuzzlesWon = stats?.puzzles_won ?? 0,
                TotalPlaytime = TimeSpan.FromMinutes(stats?.total_playtime_minutes ?? 0),
                HighestScore = stats?.highest_score ?? 0
            };
        }

        private static List<AchievementDto> mapToAchievementDtoList(ICollection<Achievements> achievements)
        {
            if (achievements == null)
            {
                return new List<AchievementDto>();
            }

            return achievements.Select(ach => new AchievementDto
            {
                Name = ach.name,
                Description = ach.description,
                IconPath = ach.icon_path
            }).ToList();
        }

        private static List<PlayerSocialMediaDto> mapToPlayerSocialMediaDtoList(ICollection<PlayerSocialMedias> socialMedias)
        {
            if (socialMedias == null)
            {
                return new List<PlayerSocialMediaDto>();
            }

            return socialMedias.Select(sm => new PlayerSocialMediaDto
            {
                IdSocialMediaPlatform = sm.IdSocialMediaPlatform,
                PlatformName = sm.SocialMediaPlatforms?.Name ?? UNKNOWN_PLATFORM_NAME,
                Username = sm.Username
            }).ToList();
        }

        private static UserProfileForEditDto mapToUserProfileForEditDto(
            Player player,
            List<GenderDto> allGendersDto,
            List<SocialMediaPlatforms> allPlatforms)
        {
            var socialMediaList = buildSocialMediaList(player.PlayerSocialMedias, allPlatforms);

            return new UserProfileForEditDto
            {
                FirstName = player.first_name,
                LastName = player.last_name,
                DateOfBirth = player.date_of_birth,
                IdGender = player.gender_id ?? 0,
                AvailableGenders = allGendersDto,
                SocialMedia = socialMediaList
            };
        }

        private static List<PlayerSocialMediaDto> buildSocialMediaList(
            ICollection<PlayerSocialMedias> playerSocialMedias,
            List<SocialMediaPlatforms> allPlatforms)
        {
            var socialMediaList = new List<PlayerSocialMediaDto>();

            addExistingPlayerSocialMedias(socialMediaList, playerSocialMedias);
            addMissingPlatforms(socialMediaList, allPlatforms);

            return socialMediaList.OrderBy(sm => sm.PlatformName).ToList();
        }

        private static void addExistingPlayerSocialMedias(
            List<PlayerSocialMediaDto> socialMediaList,
            ICollection<PlayerSocialMedias> playerSocialMedias)
        {
            if (playerSocialMedias == null)
            {
                return;
            }

            foreach (var userMedia in playerSocialMedias)
            {
                socialMediaList.Add(new PlayerSocialMediaDto
                {
                    IdSocialMediaPlatform = userMedia.IdSocialMediaPlatform,
                    PlatformName = userMedia.SocialMediaPlatforms?.Name ?? UNKNOWN_PLATFORM_NAME,
                    Username = userMedia.Username
                });
            }
        }

        private static void addMissingPlatforms(
            List<PlayerSocialMediaDto> socialMediaList,
            List<SocialMediaPlatforms> allPlatforms)
        {
            if (allPlatforms == null)
            {
                return;
            }

            var missingPlatforms = allPlatforms
                .Where(platform => socialMediaList.All(sm => sm.IdSocialMediaPlatform != platform.IdSocialMediaPlatform))
                .Select(platform => new PlayerSocialMediaDto
                {
                    IdSocialMediaPlatform = platform.IdSocialMediaPlatform,
                    PlatformName = platform.Name,
                    Username = string.Empty
                });

            socialMediaList.AddRange(missingPlatforms);
        }

        private static void applyProfileUpdates(Player player, UserProfileForEditDto updatedProfileData)
        {
            player.first_name = updatedProfileData.FirstName.Trim();
            player.last_name = updatedProfileData.LastName?.Trim();
            player.date_of_birth = updatedProfileData.DateOfBirth;
            player.gender_id = updatedProfileData.IdGender > 0
                ? updatedProfileData.IdGender : (int?)null;

            if (updatedProfileData.SocialMedia != null)
            {
                foreach (var mediaDto in updatedProfileData.SocialMedia)
                {
                    updateSocialMedia(player, mediaDto);
                }
            }
        }

        private static void updateSocialMedia(Player player, PlayerSocialMediaDto mediaDto)
        {
            var existingMedia = player.PlayerSocialMedias
                .FirstOrDefault(pm => pm.IdSocialMediaPlatform == mediaDto.IdSocialMediaPlatform);

            bool inputIsEmpty = string.IsNullOrWhiteSpace(mediaDto.Username);

            if (existingMedia != null)
            {
                if (inputIsEmpty)
                {
                    player.PlayerSocialMedias.Remove(existingMedia);
                }
                else
                {
                    existingMedia.Username = mediaDto.Username.Trim();
                }
            }
            else if (!inputIsEmpty)
            {
                player.PlayerSocialMedias.Add(new PlayerSocialMedias
                {
                    IdPlayer = player.idPlayer,
                    IdSocialMediaPlatform = mediaDto.IdSocialMediaPlatform,
                    Username = mediaDto.Username.Trim()
                });
            }
        }
    }
}