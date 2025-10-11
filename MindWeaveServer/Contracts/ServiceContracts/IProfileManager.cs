using MindWeaveServer.Contracts.DataContracts;
using System.ServiceModel;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract]
    public interface IProfileManager
    {
        [OperationContract]
        UserProfile getProfile(string username);

        [OperationContract]
        OperationResult updateProfile(string username, UserProfile newProfileData);

        [OperationContract]
        OperationResult changePassword(string username, string currentPassword, string newPassword);
    }
}
