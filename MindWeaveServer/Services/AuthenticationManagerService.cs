using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.ServiceContracts;
using NLog;
using System;
using System.ServiceModel;
using System.Threading.Tasks;
using Autofac;

namespace MindWeaveServer.Services
{
    public class AuthenticationManagerService : IAuthenticationManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly AuthenticationLogic authenticationLogic;

        public AuthenticationManagerService()
            : this(resolveDependencies())
        {
        }
        private static AuthenticationLogic resolveDependencies()
        {
            Bootstrapper.init();
            return Bootstrapper.Container.Resolve<AuthenticationLogic>();
        }

        public AuthenticationManagerService(AuthenticationLogic authenticationLogic)
        {
            this.authenticationLogic = authenticationLogic;
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
            catch (System.Data.Entity.Core.EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Resources.Lang.ErrorMsgServerOffline,
                    "Database");

                logger.Fatal(ex, "Login Fatal: Database unavailable for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (TimeoutException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Resources.Lang.GenericServerError,
                    "Timeout");

                logger.Error(ex, "Login Error: Operation timed out for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Service Timeout"));
            }

            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Resources.Lang.GenericServerError,
                    "Server");

                logger.Fatal(ex, "Login Critical: Unhandled exception for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
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
            catch (InvalidOperationException ex) when (ex.Message == "DuplicateUser")
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DuplicateRecord,
                    Resources.Lang.RegistrationUsernameOrEmailExists,
                    "Username/Email");

                logger.Warn("Registration failed: Duplicate user detected for {Username}", usernameForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Duplicate Record"));
            }
            catch (System.Data.Entity.Core.EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Resources.Lang.ErrorMsgServerOffline,
                    "Database");

                logger.Fatal(ex, "Database connectivity error during registration for {Username}", usernameForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Resources.Lang.GenericServerError,
                    "Server");

                logger.Fatal(ex, "Critical unhandled error during registration for {Username}", usernameForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
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
            catch (System.Data.Entity.Core.EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Resources.Lang.ErrorMsgServerOffline,
                    "Database");

                logger.Fatal(ex, "Verification Fatal: Database unavailable for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Resources.Lang.GenericServerError,
                    "Server");

                logger.Fatal(ex, "Verification Critical: Unhandled exception for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
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
            catch (System.Data.Entity.Core.EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Resources.Lang.ErrorMsgServerOffline,
                    "Database");

                logger.Fatal(ex, "ResendCode Fatal: Database unavailable for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.CommunicationError,
                    "Error sending email. Please try again later.",
                    "EmailService");
                logger.Error(ex, "ResendCode Error: Email service failed for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Email Service Failed"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Resources.Lang.GenericServerError,
                    "Server");

                logger.Fatal(ex, "ResendCode Critical: Unhandled exception for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
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

            catch (System.Data.Entity.Core.EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Resources.Lang.ErrorMsgServerOffline,
                    "Database");

                logger.Fatal(ex, "RecoveryCode Fatal: Database unavailable for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.CommunicationError,
                    "Error sending email. Please try again later.",
                    "EmailService");

                logger.Error(ex, "RecoveryCode Error: Email service failed (SocketException) for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Email Service Failed"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Resources.Lang.GenericServerError,
                    "Server");

                logger.Fatal(ex, "RecoveryCode Critical: Unhandled exception for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
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
            catch (System.Data.Entity.Core.EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Resources.Lang.ErrorMsgServerOffline,
                    "Database");

                logger.Fatal(ex, "ResetPassword Fatal: Database unavailable for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Resources.Lang.GenericServerError,
                    "Server");

                logger.Fatal(ex, "ResetPassword Critical: Unhandled exception for {Email}", emailForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public void logOut(string username)
        {
            string userForContext = username ?? "NULL";
            logger.Info("Logout requested for user: {Username}", userForContext);
        }


    }
}
