using MindWeaveServer.Contracts.DataContracts;
using System.ServiceModel;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract]
    public interface IAuthenticationManager
    {
        [OperationContract]
        OperationResultDto login(string username, string password);

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
