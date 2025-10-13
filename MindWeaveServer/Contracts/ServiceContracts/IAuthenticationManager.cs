using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using System.ServiceModel;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract]
    public interface IAuthenticationManager
    {
        [OperationContract]
        LoginResultDto login(LoginDto loginCredentials);

        [OperationContract]
        OperationResultDto register(UserProfileDto userProfile, string password);

        [OperationContract]
        OperationResultDto verifyAccount(string email, string code);

        [OperationContract]
        OperationResultDto sendPasswordRecoveryCode(string email);

        [OperationContract]
        OperationResultDto resetPasswordWithCode(string email, string code, string newPassword);

        [OperationContract(IsOneWay = true)]
        void logOut(string username);
    }
}
