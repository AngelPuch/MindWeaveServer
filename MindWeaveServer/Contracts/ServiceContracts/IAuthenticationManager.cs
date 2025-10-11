using MindWeaveServer.Contracts.DataContracts;
using System.ServiceModel;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract]
    public interface IAuthenticationManager
    {
        [OperationContract]
        OperationResult login(string username, string password);

        [OperationContract]
        OperationResult register(UserProfile userProfile, string password);

        [OperationContract]
        OperationResult verifyAccount(string verificationToken);

        [OperationContract]
        OperationResult sendPasswordRecoveryCode(string email);

        [OperationContract]
        OperationResult resetPasswordWithCode(string email, string code, string newPassword);

        [OperationContract(IsOneWay = true)]
        void logOut(string username);
    }
}
