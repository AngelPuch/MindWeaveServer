using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.Utilities.Email;
using System;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    internal class AuthenticationManagerService : IAuthenticationManager
    {
        private readonly AuthenticationLogic authenticationLogic;

        public AuthenticationManagerService()
        {
            authenticationLogic = new AuthenticationLogic(new SmtpEmailService());
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

        public OperationResultDto verifyAccount(string email, string code)
        {
            try
            {
                return authenticationLogic.verifyAccount(email, code);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new OperationResultDto { success = false, message = Resources.Lang.GenericServerError };
            }
        }

        public OperationResultDto resendVerificationCode(string email)
        {
            try
            {
                return authenticationLogic.resendVerificationCodeAsync(email).Result;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return new OperationResultDto { success = false, message = Resources.Lang.GenericServerError };
            }
        }

        public OperationResultDto sendPasswordRecoveryCode(string email)
        {
            throw new NotImplementedException();
        }

        public OperationResultDto resetPasswordWithCode(string email, string code, string newPassword)
        {
            throw new NotImplementedException();
        }

        public void logOut(string username)
        {
            throw new NotImplementedException();
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

    }
}
