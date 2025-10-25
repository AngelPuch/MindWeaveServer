using MindWeaveServer.Contracts.DataContracts.Social;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Abstractions
{
    public interface IPlayerRepository
    {
        Task<Player> getPlayerByEmailAsync(string email);
        void addPlayer(Player player);
        Task<Player> getPlayerByUsernameAsync(string username);
        Task<Player> getPlayerWithProfileViewDataAsync(string username);
        Task<List<PlayerSearchResultDto>> SearchPlayersAsync(int requesterId, string query, int maxResults = 10);
        Task<int> saveChangesAsync();
    }
}