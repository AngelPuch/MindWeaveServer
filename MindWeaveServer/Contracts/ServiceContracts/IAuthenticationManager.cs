using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract]
    public interface IAuthenticationManager
    {
        [OperationContract]
        Task<LoginResultDto> login(LoginDto loginCredentials);

        [OperationContract]
        Task<OperationResultDto> register(UserProfileDto userProfile, string password);

        [OperationContract]
        Task<OperationResultDto> verifyAccount(string email, string code);

        [OperationContract]
        Task<OperationResultDto> resendVerificationCode(string email);

        [OperationContract]
        Task<OperationResultDto> sendPasswordRecoveryCodeAsync(string email);

        [OperationContract]
        Task<OperationResultDto> resetPasswordWithCodeAsync(string email, string code, string newPassword);

        [OperationContract(IsOneWay = true)]
        void logOut(string username);
    }
}
