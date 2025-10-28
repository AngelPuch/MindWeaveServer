using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities;
using MindWeaveServer.Utilities.Validators;
using System;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class ProfileManagerService : IProfileManager
    {
        private readonly ProfileLogic profileLogic;

        public ProfileManagerService()
        {
            var dbContext = new MindWeaveDBEntities1();
            var playerRepository = new PlayerRepository(dbContext);
            var genderRepository = new GenderRepository(dbContext);
            var passwordService = new PasswordService();
            var passwordPolicyValidator = new PasswordPolicyValidator();
            this.profileLogic = new ProfileLogic(
                playerRepository, 
                genderRepository, 
                passwordService, 
                passwordPolicyValidator);
        }


        public async Task<PlayerProfileViewDto> getPlayerProfileView(string username)
        {
            try
            {
                return await profileLogic.getPlayerProfileViewAsync(username);
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public async Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username)
        {
            try
            {
                return await profileLogic.getPlayerProfileForEditAsync(username);
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public async Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData)
        {
            try
            {
                return await profileLogic.updateProfileAsync(username, updatedProfileData);
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath)
        {
            try
            {
                return await profileLogic.updateAvatarPathAsync(username, newAvatarPath);
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> changePasswordAsync(string username, string currentPassword, string newPassword)
        {
            try
            {
                return await profileLogic.changePasswordAsync(username, currentPassword, newPassword);
            }
            catch (Exception ex)
            {
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }
    }
}