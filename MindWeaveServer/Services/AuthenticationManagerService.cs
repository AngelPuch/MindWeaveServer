using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Utilities;
using MindWeaveServer.Utilities.Email;
using MindWeaveServer.Utilities.Validators;
using System;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Shared;
using NLog;

namespace MindWeaveServer.Services
{

    

    internal class AuthenticationManagerService : IAuthenticationManager
    {
        private readonly AuthenticationLogic authenticationLogic;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public AuthenticationManagerService()
        {
            var emailService = new SmtpEmailService();
            var passwordService = new PasswordService();
            var passwordPolicyValidator = new PasswordPolicyValidator();
            var verificationCodeService = new VerificationCodeService();
            var userProfileValidator = new UserProfileDtoValidator();
            var loginValidator = new LoginDtoValidator();
            var dbContext = new MindWeaveDBEntities1();
            var playerRepository = new PlayerRepository(dbContext);

            authenticationLogic = new AuthenticationLogic(
                playerRepository,
                emailService,
                passwordService,
                passwordPolicyValidator,
                verificationCodeService,
                userProfileValidator,
                loginValidator

            );

            logger.Info("AuthenticationManagerService instance created.");

        }

        public async Task<LoginResultDto> login(LoginDto loginCredentials)
        {
            string emailForContext = loginCredentials?.Email ?? "NULL";
            logger.Info("Login attempt started for email: {Email}", emailForContext);
            try
            {
                var result = await authenticationLogic.loginAsync(loginCredentials);
                if (result.OperationResult.Success)
                {
                    logger.Info("Login successful for user: {Username}. Email: {Email}", result.Username, emailForContext);
                }
                else if (result.ResultCode == "ACCOUNT_NOT_VERIFIED")
                {
                    logger.Warn("Login failed: Account not verified. Email: {Email}", emailForContext);
                }
                else
                {
                    logger.Warn("Login failed: {Reason}. Email: {Email}", result.OperationResult.Message, emailForContext);
                }
                return result;
            }
            catch (Exception ex)
            {
               logger.Error(ex, "An unexpected error occurred during login. Email: {Email}", emailForContext);
                return new LoginResultDto
                {
                    OperationResult = new OperationResultDto { Success = false, Message = Resources.Lang.GenericServerError }
                };
            }
        }

        public async Task<OperationResultDto> register(UserProfileDto userProfile, string password)
        {
            string usernameForContext = userProfile?.Username ?? "NULL";
            string emailForContext = userProfile?.Email ?? "NULL";
            logger.Info("Registration attempt started for user: {Username}, Email: {Email}", usernameForContext, emailForContext);
            try
            {
                var result = await authenticationLogic.registerPlayerAsync(userProfile, password);
                if (result.Success)
                {
                    logger.Info("Registration successful for user: {Username}, Email: {Email}", usernameForContext, emailForContext);
                }
                else
                {
                    logger.Warn("Registration failed: {Reason}. User: {Username}, Email: {Email}", result.Message, usernameForContext, emailForContext);
                }
                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An unexpected error occurred during registration. User: {Username}, Email: {Email}", usernameForContext, emailForContext);
                return new OperationResultDto { Success = false, Message = Resources.Lang.GenericServerError };
            }
        }
        public async Task<OperationResultDto> verifyAccount(string email, string code)
        {
            string emailForContext = email ?? "NULL";
            logger.Info("Account verification attempt started for email: {Email}", emailForContext);
            try
            {
                var result = await authenticationLogic.verifyAccountAsync(email, code);
                if (result.Success)
                {
                    logger.Info("Account verification successful for email: {Email}", emailForContext);
                }
                else
                {
                    logger.Warn("Account verification failed: {Reason}. Email: {Email}", result.Message, emailForContext);
                }
                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An unexpected error occurred during account verification. Email: {Email}", emailForContext);
                return new OperationResultDto { Success = false, Message = Resources.Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> resendVerificationCode(string email)
        {
            string emailForContext = email ?? "NULL";
            logger.Info("Resend verification code attempt started for email: {Email}", emailForContext);
            try
            {
                var result = await authenticationLogic.resendVerificationCodeAsync(email);
                if (result.Success)
                {
                    logger.Info("Resent verification code successfully for email: {Email}", emailForContext);
                }
                else
                {
                    logger.Warn("Resend verification code failed: {Reason}. Email: {Email}", result.Message, emailForContext);
                }
                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An unexpected error occurred while resending verification code. Email: {Email}", emailForContext);
                return new OperationResultDto { Success = false, Message = Resources.Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> sendPasswordRecoveryCodeAsync(string email)
        {
            string emailForContext = email ?? "NULL";
            logger.Info("Send password recovery code attempt started for email: {Email}", emailForContext);
            try
            {
                var result = await authenticationLogic.sendPasswordRecoveryCodeAsync(email);
                if (result.Success)
                {
                    logger.Info("Sent password recovery code successfully for email: {Email}", emailForContext);
                }
                else
                {
                    logger.Warn("Send password recovery code failed: {Reason}. Email: {Email}", result.Message, emailForContext);
                }
                return result;

            }
            catch (Exception ex)
            {
                logger.Error(ex, "An unexpected error occurred while sending password recovery code. Email: {Email}", emailForContext);
                return new OperationResultDto { Success = false, Message = Resources.Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> resetPasswordWithCodeAsync(string email, string code, string newPassword)
        {
            string emailForContext = email ?? "NULL";
            logger.Info("Reset password with code attempt started for email: {Email}", emailForContext);
            try
            {
                var result = await authenticationLogic.resetPasswordWithCodeAsync(email, code, newPassword);
                if (result.Success)
                {
                    logger.Info("Password reset successful for email: {Email}", emailForContext);
                }
                else
                {
                    logger.Warn("Password reset failed: {Reason}. Email: {Email}", result.Message, emailForContext);
                }
                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An unexpected error occurred while resetting password. Email: {Email}", emailForContext);
                return new OperationResultDto { Success = false, Message = Resources.Lang.GenericServerError };
            }
        }

        public void logOut(string username)
        {
            string userForContext = username ?? "NULL";
            logger.Info("Logout requested for user: {Username}", userForContext);
        }


    }
}
