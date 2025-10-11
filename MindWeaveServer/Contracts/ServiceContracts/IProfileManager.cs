using MindWeaveServer.Contracts.DataContracts;
using System.ServiceModel;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract]
    public interface IProfileManager
    {
        [OperationContract]
        UserProfileDto getProfile(string username);

        [OperationContract]
        OperationResultDto updateProfile(string username, UserProfileDto newProfileDtoData);

        [OperationContract]
        OperationResultDto changePassword(string username, string currentPassword, string newPassword);
    }
}
