using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Resources;
using NLog;
using System;
using System.Collections.Generic;
using System.Data.Entity.Core;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Linq;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class ProfileManagerService : IProfileManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly ProfileLogic profileLogic;

        public ProfileManagerService() : this(resolveDep()) { }

        private static ProfileLogic resolveDep()
        {
            Bootstrapper.init();
            return Bootstrapper.Container.Resolve<ProfileLogic>();
        }
        public ProfileManagerService(ProfileLogic profileLogic)
        {
            this.profileLogic = profileLogic;
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

        public List<AchievementDto> GetPlayerAchievements(int playerId)
        {
            using (var context = new MindWeaveDBEntities1())
            {
                var allAchievements = context.Achievements.ToList();

                
                var player = context.Player
                                    .Include("Achievements") 
                                    .FirstOrDefault(p => p.idPlayer == playerId);

                var userAchievementIds = new List<int>();

                if (player != null && player.Achievements != null)
                {
                    userAchievementIds = player.Achievements
                                               .Select(a => a.achievements_id)
                                               .ToList();
                }

                var achievementDtos = new List<AchievementDto>();

                foreach (var achievement in allAchievements)
                {
                    var dto = new AchievementDto
                    {
                        Id = achievement.achievements_id,
                        Name = achievement.name,
                        Description = achievement.description,
                        IconPath = achievement.icon_path,
                        IsUnlocked = userAchievementIds.Contains(achievement.achievements_id)
                    };

                    achievementDtos.Add(dto);
                }

                return achievementDtos;
            }
        }
    }
}