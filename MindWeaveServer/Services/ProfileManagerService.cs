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

        private const string OPERATION_GET_PROFILE_VIEW = "GetPlayerProfileViewOperation";
        private const string OPERATION_GET_PROFILE_FOR_EDIT = "GetPlayerProfileForEditOperation";
        private const string OPERATION_UPDATE_PROFILE = "UpdateProfileOperation";
        private const string OPERATION_UPDATE_AVATAR = "UpdateAvatarPathOperation";
        private const string OPERATION_CHANGE_PASSWORD = "ChangePasswordOperation";
        private const string OPERATION_GET_ACHIEVEMENTS = "GetPlayerAchievementsOperation";

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
            logger.Info("Request received: GetPlayerProfileView.");
            try
            {
                return await profileLogic.getPlayerProfileViewAsync(username);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_GET_PROFILE_VIEW);
            }
        }

        public async Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username)
        {
            logger.Info("Request received: GetPlayerProfileForEditAsync.");
            try
            {
                return await profileLogic.getPlayerProfileForEditAsync(username);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_GET_PROFILE_FOR_EDIT);
            }
        }

        public async Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData)
        {
            logger.Info("Request received: UpdateProfileAsync.");
            try
            {
                return await profileLogic.updateProfileAsync(username, updatedProfileData);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_UPDATE_PROFILE);
            }
        }

        public async Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath)
        {
            logger.Info("Request received: UpdateAvatarPathAsync.");
            try
            {
                return await profileLogic.updateAvatarPathAsync(username, newAvatarPath);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_UPDATE_AVATAR);
            }
        }

        public async Task<OperationResultDto> changePasswordAsync(string username, string currentPassword, string newPassword)
        {
            logger.Info("Request received: ChangePasswordAsync.");
            try
            {
                return await profileLogic.changePasswordAsync(username, currentPassword, newPassword);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_CHANGE_PASSWORD);
            }
        }

        public async Task<List<AchievementDto>> getPlayerAchievementsAsync(int playerId)
        {
            logger.Info("Request received: GetPlayerAchievementsAsync for PlayerId: {PlayerId}.", playerId);
            try
            {
                return await profileLogic.getPlayerAchievementsAsync(playerId);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, OPERATION_GET_ACHIEVEMENTS);
            }
        }
    }
}