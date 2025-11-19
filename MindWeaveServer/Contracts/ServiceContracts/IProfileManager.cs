using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Stats;

using System.ServiceModel;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract]
    public interface IProfileManager
    {
        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<PlayerProfileViewDto> getPlayerProfileView(string username);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath);

        [OperationContract]
        [FaultContract(typeof(ServiceFaultDto))]
        Task<OperationResultDto> changePasswordAsync(string username, string currentPassword, string newPassword);
    }
}
