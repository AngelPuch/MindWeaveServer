using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.Utilities.Abstractions;
using NLog;
using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class ProfileManagerService : IProfileManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private const string UNKNOWN_USER = "Unknown";

        private readonly ProfileLogic profileLogic;
        private readonly IServiceExceptionHandler exceptionHandler;

        public ProfileManagerService()
        {
            Bootstrapper.init();
            this.profileLogic = Bootstrapper.Container.Resolve<ProfileLogic>();
            this.exceptionHandler = Bootstrapper.Container.Resolve<IServiceExceptionHandler>();
        }

        public ProfileManagerService(ProfileLogic profileLogic, IServiceExceptionHandler exceptionHandler)
        {
            this.profileLogic = profileLogic;
            this.exceptionHandler = exceptionHandler;
        }

        public async Task<PlayerProfileViewDto> getPlayerProfileView(string username)
        {
            logger.Info("Request received: GetPlayerProfileView for user {Username}", username ?? UNKNOWN_USER);
            try
            {
                return await profileLogic.getPlayerProfileViewAsync(username);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "GetPlayerProfileViewOperation");
            }
        }

        public async Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username)
        {
            logger.Info("Request received: GetPlayerProfileForEditAsync for user {Username}", username ?? UNKNOWN_USER);
            try
            {
                return await profileLogic.getPlayerProfileForEditAsync(username);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "GetPlayerProfileForEditOperation");
            }
        }

        public async Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData)
        {
            logger.Info("Request received: UpdateProfileAsync for user {Username}", username ?? UNKNOWN_USER);
            try
            {
                return await profileLogic.updateProfileAsync(username, updatedProfileData);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "UpdateProfileOperation");
            }
        }

        public async Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath)
        {
            logger.Info("Request received: UpdateAvatarPathAsync for user {Username}", username ?? UNKNOWN_USER);
            try
            {
                return await profileLogic.updateAvatarPathAsync(username, newAvatarPath);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "UpdateAvatarPathOperation");
            }
        }

        public async Task<OperationResultDto> changePasswordAsync(string username, string currentPassword, string newPassword)
        {
            logger.Info("Request received: ChangePasswordAsync for user {Username}", username ?? UNKNOWN_USER);
            try
            {
                return await profileLogic.changePasswordAsync(username, currentPassword, newPassword);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "ChangePasswordOperation");
            }
        }

        public async Task<List<AchievementDto>> getPlayerAchievementsAsync(int playerId)
        {
            logger.Info("getPlayerAchievements request started for PlayerId: {PlayerId}", playerId);
            try
            {
                return await profileLogic.getPlayerAchievementsAsync(playerId);

            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "GetPlayerAchievementsOperation");
            }
        }

    }
}