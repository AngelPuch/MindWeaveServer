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
using System.Data.Entity.Core;
using System;
using System.ServiceModel;
using System.Threading.Tasks;
using NLog;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class ProfileManagerService : IProfileManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

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

            logger.Info("ProfileManagerService instance created.");
        }


        public async Task<PlayerProfileViewDto> getPlayerProfileView(string username)
        {
            string userForContext = username ?? "NULL";
            logger.Info("getPlayerProfileView request started for user: {Username}", userForContext);
            try
            {
                var result = await profileLogic.getPlayerProfileViewAsync(username);
                if (result != null)
                {
                    logger.Info("Successfully retrieved profile view for user: {Username}", userForContext);
                }
                else
                {
                    logger.Warn("Profile view not found for user: {Username}", userForContext);
                }
                return result;
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Lang.ErrorMsgServerOffline,
                    "Database");

                logger.Fatal(ex, "Profile Fatal: Database unavailable getting view for {Username}", userForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Lang.GenericServerError,
                    "Server");

                logger.Fatal(ex, "Profile Critical: Unhandled exception getting view for {Username}", userForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }


        public async Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username)
        {
            string userForContext = username ?? "NULL";
            logger.Info("getPlayerProfileForEdit request started for user: {Username}", userForContext);
            try
            {
                var result = await profileLogic.getPlayerProfileForEditAsync(username);
                if (result != null)
                {
                    logger.Info("Successfully retrieved editable profile for user: {Username}", userForContext);
                }
                else
                {
                    logger.Warn("Editable profile not found for user: {Username}", userForContext);
                }
                return result;
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Lang.ErrorMsgServerOffline,
                    "Database");

                logger.Fatal(ex, "Profile Fatal: Database unavailable getting edit data for {Username}", userForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Lang.GenericServerError,
                    "Server");

                logger.Fatal(ex, "Profile Critical: Unhandled exception getting edit data for {Username}", userForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public async Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData)
        {
            string userForContext = username ?? "NULL";
            logger.Info("updateProfile request started for user: {Username}", userForContext);
            try
            {
                var result = await profileLogic.updateProfileAsync(username, updatedProfileData);
                if (result.Success)
                {
                    logger.Info("Profile updated successfully for user: {Username}", userForContext);
                }
                else
                {
                    logger.Warn("Profile update failed for user: {Username}. Reason: {Reason}", userForContext, result.Message);
                }
                return result;
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Lang.ErrorMsgServerOffline,
                    "Database");

                logger.Fatal(ex, "Profile Fatal: Database unavailable updating profile for {Username}", userForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Lang.GenericServerError,
                    "Server");

                logger.Fatal(ex, "Profile Critical: Unhandled exception updating profile for {Username}", userForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public async Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath)
        {
            string userForContext = username ?? "NULL";
            logger.Info("updateAvatarPath request started for user: {Username}, NewPath: {AvatarPath}", userForContext, newAvatarPath ?? "NULL");
            try
            {
                var result = await profileLogic.updateAvatarPathAsync(username, newAvatarPath);
                if (result.Success)
                {
                    logger.Info("Avatar path updated successfully for user: {Username}", userForContext);
                }
                else
                {
                    logger.Warn("Avatar path update failed for user: {Username}. Reason: {Reason}", userForContext, result.Message);
                }
                return result;
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Lang.ErrorMsgServerOffline,
                    "Database");

                logger.Fatal(ex, "Profile Fatal: Database unavailable updating avatar for {Username}", userForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Lang.GenericServerError,
                    "Server");

                logger.Fatal(ex, "Profile Critical: Unhandled exception updating avatar for {Username}", userForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public async Task<OperationResultDto> changePasswordAsync(string username, string currentPassword, string newPassword)
        {
            string userForContext = username ?? "NULL";
            logger.Info("changePassword request started for user: {Username}", userForContext);
            try
            {
                var result = await profileLogic.changePasswordAsync(username, currentPassword, newPassword);
                if (result.Success)
                {
                    logger.Info("Password changed successfully for user: {Username}", userForContext);
                }
                else
                {
                    logger.Warn("Password change failed for user: {Username}. Reason: {Reason}", userForContext, result.Message);
                }
                return result;
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Lang.ErrorMsgServerOffline,
                    "Database");

                logger.Fatal(ex, "Profile Fatal: Database unavailable changing password for {Username}", userForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Lang.GenericServerError,
                    "Server");

                logger.Fatal(ex, "Profile Critical: Unhandled exception changing password for {Username}", userForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }     
    }
}