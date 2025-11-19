using MindWeaveServer.Contracts.DataContracts.Authentication;
using System.ServiceModel;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract]
    public interface IAuthenticationManager
    {
        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<LoginResultDto> login(LoginDto loginCredentials);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<OperationResultDto> register(UserProfileDto userProfile, string password);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<OperationResultDto> verifyAccount(string email, string code);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<OperationResultDto> resendVerificationCode(string email);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<OperationResultDto> sendPasswordRecoveryCodeAsync(string email);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<OperationResultDto> resetPasswordWithCodeAsync(string email, string code, string newPassword);

        [OperationContract(IsOneWay = true)]
        [FaultContract(typeof(ServiceFaultDto))]
        void logOut(string username);
    }
}
