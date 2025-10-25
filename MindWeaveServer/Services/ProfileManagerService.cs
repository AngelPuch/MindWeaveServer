using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Shared;

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



        public OperationResultDto changePassword(string username, string currentPassword, string newPassword)
        {
            throw new NotImplementedException();
        }

        public async Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username)
        {
            return await profileLogic.getPlayerProfileForEditAsync(username);
        }

        // REEMPLAZA ESTE MÉTODO VACÍO:
        // public OperationResultDto updateProfile(string username, UserProfileDto newProfileDtoData)
        // {
        //     throw new NotImplementedException();
        // }

        // POR ESTE MÉTODO CORREGIDO Y ASÍNCRONO:
        public async Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfileData)
        {
            // Creamos una instancia de la lógica y la llamamos
            var profileLogic = new ProfileLogic();
            return await profileLogic.updateProfileAsync(username, updatedProfileData);
        }
        public async Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath)
        {
            var profileLogic = new ProfileLogic();
            return await profileLogic.updateAvatarPathAsync(username, newAvatarPath);
        }
    }
}