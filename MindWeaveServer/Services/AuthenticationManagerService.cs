using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.Utilities.Email;
using System;

namespace MindWeaveServer.Services
{
    internal class AuthenticationManagerService : IAuthenticationManager
    {
        private readonly AuthenticationLogic authenticationLogic;

        public AuthenticationManagerService()
        {
            authenticationLogic = new AuthenticationLogic(new SmtpEmailService());
        }

        public OperationResultDto login(string username, string password)
        {
            throw new NotImplementedException();
        }

        public OperationResultDto register(UserProfileDto userProfile, string password)
        {
            try
            {
                return authenticationLogic.registerPlayerAsync(userProfile, password).Result;
            }
            catch (Exception ex)
            {
                // TO-DO: Implementar un sistema de logging real
                Console.WriteLine(ex.ToString());
                return new OperationResultDto { success = false, message = "An unexpected server error occurred during registration." };
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
                return new OperationResultDto { success = false, message = "An unexpected server error occurred during verification." };
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
    }
}
