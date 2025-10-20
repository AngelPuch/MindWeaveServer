using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Resources;
using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    // NO LLEVA [ServiceContract] aquí
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class SocialManagerService : ISocialManager // Implementa la interfaz actualizada
    {
        private readonly SocialLogic socialLogic;

        // Constructor... (igual que antes)
        public SocialManagerService()
        {
            var dbContext = new MindWeaveDBEntities1();
            var playerRepository = new PlayerRepository(dbContext);
            var friendshipRepository = new FriendshipRepository(dbContext);
            this.socialLogic = new SocialLogic(playerRepository, friendshipRepository);
        }

        // --- Métodos que implementan la interfaz ISocialManager ---
        // SIN [OperationContract] aquí

        public async Task<List<PlayerSearchResultDto>> searchPlayers(string requesterUsername, string query)
        {
            // ... (implementación igual que antes)
            try
            {
                return await socialLogic.searchPlayersAsync(requesterUsername, query);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in searchPlayers: {ex}");
                return new List<PlayerSearchResultDto>();
            }
        }

        public async Task<OperationResultDto> sendFriendRequest(string requesterUsername, string targetUsername)
        {
            // ... (implementación igual que antes)
            try
            {
                var result = await socialLogic.sendFriendRequestAsync(requesterUsername, targetUsername);
                // TODO: Callback notification logic
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in sendFriendRequest: {ex}");
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> respondToFriendRequest(string responderUsername, string requesterUsername, bool accepted)
        {
            // ... (implementación igual que antes)
            try
            {
                var result = await socialLogic.respondToFriendRequestAsync(responderUsername, requesterUsername, accepted);
                // TODO: Callback notification logic
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in respondToFriendRequest: {ex}");
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> removeFriend(string username, string friendToRemoveUsername)
        {
            // ... (implementación igual que antes)
            try
            {
                var result = await socialLogic.removeFriendAsync(username, friendToRemoveUsername);
                // TODO: Callback notification logic
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in removeFriend: {ex}");
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }


        public async Task<List<FriendDto>> getFriendsList(string username)
        {
            // ... (implementación igual que antes)
            try
            {
                return await socialLogic.getFriendsListAsync(username);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in getFriendsList: {ex}");
                return new List<FriendDto>();
            }
        }

        public async Task<List<FriendRequestInfoDto>> getFriendRequests(string username)
        {
            // ... (implementación igual que antes)
            try
            {
                return await socialLogic.getFriendRequestsAsync(username);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in getFriendRequests: {ex}");
                return new List<FriendRequestInfoDto>();
            }
        }

        // **** ¡¡ELIMINA LOS MÉTODOS WRAPPER Y EL BLOQUE COMENTADO IsOneWay=true DE AQUÍ!! ****

    }
}