using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts;
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

namespace MindWeaveServer.Services
{
    internal class AuthenticationManagerService : IAuthenticationManager
    {
        private readonly AuthenticationLogic authenticationLogic;

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
        }

        public async Task<LoginResultDto> login(LoginDto loginCredentials)
        {
            try
            {
                return await authenticationLogic.loginAsync(loginCredentials);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new LoginResultDto
                {
                    operationResult = new OperationResultDto { success = false, message = Resources.Lang.GenericServerError }
                };
            }
        }

        public async Task<OperationResultDto> register(UserProfileDto userProfile, string password)
        {
            try
            {
                return await authenticationLogic.registerPlayerAsync(userProfile, password);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new OperationResultDto { success = false, message = Resources.Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> verifyAccount(string email, string code)
        {
            try
            {
                return await authenticationLogic.verifyAccountAsync(email, code);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new OperationResultDto { success = false, message = Resources.Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> resendVerificationCode(string email)
        {
            try
            {
                return await authenticationLogic.resendVerificationCodeAsync(email);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new OperationResultDto { success = false, message = Resources.Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> sendPasswordRecoveryCodeAsync(string email)
        {
            try
            {
                return await authenticationLogic.sendPasswordRecoveryCodeAsync(email);
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service Error - SendPasswordRecovery]: {ex.ToString()}");
                return new OperationResultDto { success = false, message = Resources.Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> resetPasswordWithCodeAsync(string email, string code, string newPassword)
        {
            try
            {
                return await authenticationLogic.resetPasswordWithCodeAsync(email, code, newPassword);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service Error - ResetPassword]: {ex.ToString()}");
                return new OperationResultDto { success = false, message = Resources.Lang.GenericServerError };
            }
        }

        public void logOut(string username)
        {
            throw new NotImplementedException();
        }


    }
}
