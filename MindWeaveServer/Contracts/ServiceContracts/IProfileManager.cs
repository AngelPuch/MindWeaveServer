using MindWeaveServer.Contracts.DataContracts;
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
        OperationResultDto updateProfile(string username, UserProfileDto newProfileDtoData);

        [OperationContract]
        OperationResultDto changePassword(string username, string currentPassword, string newPassword);
    }
}
