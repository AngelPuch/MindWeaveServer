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
        Task<PlayerProfileViewDto> getPlayerProfileView(string username);

        [OperationContract]
        Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData);

        [OperationContract]
        Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username);

        [OperationContract]
        Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath);

        [OperationContract]
        Task<OperationResultDto> changePasswordAsync(string username, string currentPassword, string newPassword);
    }
}
