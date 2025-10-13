using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Stats;

using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract]
    public interface IProfileManager
    {
        [OperationContract]
        Task<PlayerProfileViewDto> getPlayerProfileView(string username);

        [OperationContract]
        UserProfileDto getProfile(string username);

        [OperationContract]
        Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData);

        [OperationContract]
        OperationResultDto changePassword(string username, string currentPassword, string newPassword);

        [OperationContract]
        Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username);

        [OperationContract]
        Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath);
    }
}
