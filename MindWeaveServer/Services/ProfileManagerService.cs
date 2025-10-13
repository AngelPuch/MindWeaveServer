using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    public class ProfileManagerService : IProfileManager
    {
        private readonly ProfileLogic profileLogic = new ProfileLogic();

        public async Task<PlayerProfileViewDto> getPlayerProfileView(string username)
        {
            try
            {
                return await profileLogic.getPlayerProfileViewAsync(username);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString()); // TO-DO: Implement a real logging system
                // Consider returning a DTO with an error state instead of null
                return null;
            }
        }

        public UserProfileDto getProfile(string username)
        {
            throw new NotImplementedException();
        }

        public OperationResultDto updateProfile(string username, UserProfileDto newProfileDtoData)
        {
            throw new NotImplementedException();
        }

        public OperationResultDto changePassword(string username, string currentPassword, string newPassword)
        {
            throw new NotImplementedException();
        }
    }
}