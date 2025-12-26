using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.Utilities.Abstractions;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    public class AuthenticationManagerService : IAuthenticationManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly AuthenticationLogic authenticationLogic;
        private readonly IServiceExceptionHandler exceptionHandler;

        public AuthenticationManagerService()
        {
            Bootstrapper.init();
            this.authenticationLogic = Bootstrapper.Container.Resolve<AuthenticationLogic>();
            this.exceptionHandler = Bootstrapper.Container.Resolve<IServiceExceptionHandler>();
        }

        public AuthenticationManagerService(AuthenticationLogic authenticationLogic, IServiceExceptionHandler exceptionHandler)
        {
            this.authenticationLogic = authenticationLogic;
            this.exceptionHandler = exceptionHandler;

        }

        public async Task<LoginResultDto> login(LoginDto loginCredentials)
        {
            logger.Info("Login service request received.");
            try
            {
                return await authenticationLogic.loginAsync(loginCredentials);
                
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "LoginOperation");
            }
        }

        public async Task<OperationResultDto> register(UserProfileDto userProfile, string password)
        {
            logger.Info("Registration service request received.");
            try
            {
                return await authenticationLogic.registerPlayerAsync(userProfile, password);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "RegisterOperation");
            }
        }

        public async Task<OperationResultDto> verifyAccount(string email, string code)
        {
            logger.Info("Account verification service request received.");
            try
            {
                return await authenticationLogic.verifyAccountAsync(email, code);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "VerifyAccountOperation");
            }
        }

        public async Task<OperationResultDto> resendVerificationCode(string email)
        {
            logger.Info("Resend verification code service request received.");
            try
            {
                return await authenticationLogic.resendVerificationCodeAsync(email);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "ResendVerificationOperation");
            }
        }

        public async Task<OperationResultDto> sendPasswordRecoveryCodeAsync(string email)
        {
            logger.Info("Password recovery code service request received.");
            try
            {
                return await authenticationLogic.sendPasswordRecoveryCodeAsync(email);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "SendRecoveryCodeOperation");
            }
        }

        public async Task<OperationResultDto> resetPasswordWithCodeAsync(string email, string code, string newPassword)
        {
            logger.Info("Reset password service request received.");
            try
            {
                return await authenticationLogic.resetPasswordWithCodeAsync(email, code, newPassword);
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "ResetPasswordOperation");
            }
        }

        public void logOut(string username)
        {
            logger.Info("Logout request received for user: {Username}", username);
            try
            {
                authenticationLogic.logout(username);
                var gameStateManager = Bootstrapper.Container.Resolve<IGameStateManager>();

                if (gameStateManager.isUserConnected(username))
                {
                    notifyFriendsUserIsOffline(username);
                    gameStateManager.removeConnectedUser(username);
                    logger.Info("User {Username} removed from GameStateManager connected list.", username);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error processing logout for {Username}", username);
            }
        }

        private void notifyFriendsUserIsOffline(string username)
        {
            try
            {
                var socialLogic = Bootstrapper.Container.Resolve<SocialLogic>();
                var gameStateManager = Bootstrapper.Container.Resolve<IGameStateManager>();

                var task = System.Threading.Tasks.Task.Run(async () =>
                    await socialLogic.getFriendsListAsync(username, null));
                task.Wait();

                List<FriendDto> friends = task.Result;

                if (friends != null)
                {
                    foreach (var friend in friends)
                    {
                        var friendCallback = gameStateManager.getUserCallback(friend.Username);
                        if (friendCallback != null)
                        {
                            try
                            {
                                friendCallback.notifyFriendStatusChanged(username, false);
                            }
                            catch (Exception)
                            {
                                gameStateManager.removeConnectedUser(friend.Username);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Could not notify friends of logout for {Username}", username);
            }
        }


    }
}
