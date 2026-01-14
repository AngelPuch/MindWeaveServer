using Autofac;
using Autofac.Core;
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
using System.Data.Entity.Core;
using System.Data.SqlClient;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    public class AuthenticationManagerService : IAuthenticationManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private const string OPERATION_LOGIN = "LoginOperation";
        private const string OPERATION_REGISTER = "RegisterOperation";
        private const string OPERATION_VERIFY_ACCOUNT = "VerifyAccountOperation";
        private const string OPERATION_RESEND_VERIFICATION = "ResendVerificationOperation";
        private const string OPERATION_SEND_RECOVERY_CODE = "SendRecoveryCodeOperation";
        private const string OPERATION_RESET_PASSWORD = "ResetPasswordOperation";

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
                throw exceptionHandler.handleException(ex, OPERATION_LOGIN);
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
                throw exceptionHandler.handleException(ex, OPERATION_REGISTER);
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
                throw exceptionHandler.handleException(ex, OPERATION_VERIFY_ACCOUNT);
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
                throw exceptionHandler.handleException(ex, OPERATION_RESEND_VERIFICATION);
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
                throw exceptionHandler.handleException(ex, OPERATION_SEND_RECOVERY_CODE);
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
                throw exceptionHandler.handleException(ex, OPERATION_RESET_PASSWORD);
            }
        }

        public void logOut(string username)
        {
            logger.Info("Logout request received.");

            try
            {
                authenticationLogic.logout(username);
                handlePostLogoutCleanup(username);
            }
            catch (EntityException entityEx)
            {
                logger.Error(entityEx, "Database error during logout.");
            }
            catch (SqlException sqlEx)
            {
                logger.Error(sqlEx, "SQL error during logout.");
            }
            catch (TimeoutException timeoutEx)
            {
                logger.Error(timeoutEx, "Operation timed out during logout.");
            }
        }

        private static void handlePostLogoutCleanup(string username)
        {
            try
            {
                var gameStateManager = Bootstrapper.Container.Resolve<IGameStateManager>();

                if (!gameStateManager.isUserConnected(username))
                {
                    return;
                }

                notifyFriendsUserIsOffline(username, gameStateManager);
                gameStateManager.removeConnectedUser(username);
                logger.Info("User removed from GameStateManager connected list.");
            }
            catch (DependencyResolutionException depEx)
            {
                logger.Fatal(depEx, "Critical: Could not resolve IGameStateManager during logout cleanup.");
            }
        }

        private static void notifyFriendsUserIsOffline(string username, IGameStateManager gameStateManager)
        {
            List<FriendDto> friends = retrieveFriendsList(username);

            if (friends == null)
            {
                return;
            }

            foreach (var friend in friends)
            {
                notifyFriendIfConnected(username, friend.Username, gameStateManager);
            }
        }

        private static List<FriendDto> retrieveFriendsList(string username)
        {
            try
            {
                var socialLogic = Bootstrapper.Container.Resolve<SocialLogic>();
                var task = Task.Run(async () => await socialLogic.getFriendsListAsync(username, null));
                task.Wait();
                return task.Result;
            }
            catch (DependencyResolutionException depEx)
            {
                logger.Error(depEx, "Could not resolve SocialLogic to notify friends.");
                return new List<FriendDto>();
            }
            catch (AggregateException aggEx)
            {
                logger.Warn(aggEx, "Error retrieving friend list for logout notification.");
                return new List<FriendDto>();
            }
        }

        private static void notifyFriendIfConnected(string username, string friendUsername, IGameStateManager gameStateManager)
        {
            var friendCallback = gameStateManager.getUserCallback(friendUsername);

            if (friendCallback == null)
            {
                return;
            }

            notifySingleFriend(friendCallback, username, friendUsername, gameStateManager);
        }

        private static void notifySingleFriend(ISocialCallback friendCallback, string username, string friendUsername, IGameStateManager gameStateManager)
        {
            try
            {
                friendCallback.notifyFriendStatusChanged(username, false);
            }
            catch (CommunicationException commEx)
            {
                logger.Warn(commEx, "Connection lost with friend. Removing from active users.");
                gameStateManager.removeConnectedUser(friendUsername);
            }
            catch (TimeoutException timeoutEx)
            {
                logger.Warn(timeoutEx, "Timeout notifying friend. Removing from active users.");
                gameStateManager.removeConnectedUser(friendUsername);
            }
            catch (ObjectDisposedException disposedEx)
            {
                logger.Warn(disposedEx, "Channel disposed for friend.");
                gameStateManager.removeConnectedUser(friendUsername);
            }
        }
    }
}