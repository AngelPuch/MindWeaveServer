using MindWeaveServer.DataAccess;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Abstractions
{
    public interface IPlayerRepository
    {
        Task<Player> getPlayerByEmailAsync(string email);
        void addPlayer(Player player);
        Task<Player> getPlayerByUsernameAsync(string username);
        Task<Player> getPlayerWithProfileViewDataAsync(string username);
        Task<int> saveChangesAsync();
    }
}