using MindWeaveServer.DataAccess.Abstractions;
using System;
using System.Data.Entity;
using System.Runtime.Remoting.Contexts;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Repositories
{
    public class PlayerRepository : IPlayerRepository
    {
        private readonly MindWeaveDBEntities1 context;

        public PlayerRepository(MindWeaveDBEntities1 context)
        {
            this.context = context;
        }

        public async Task<Player> getPlayerByEmailAsync(string email)
        {
            return await context.Player.FirstOrDefaultAsync(p => p.email == email);
        }

        public async Task<Player> getPlayerByUsernameOrEmailAsync(string username, string email)
        {
            return await context.Player.FirstOrDefaultAsync(p => p.username == username || p.email == email);
        }

        public void addPlayer(Player player)
        {
            context.Player.Add(player);
        }

        public async Task<Player> getPlayerByUsernameAsync(string username)
        {
            return await context.Player
                .FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<Player> getPlayerWithProfileViewDataAsync(string username)
        {
            return await context.Player
                .Include(p => p.PlayerStats)
                .Include(p => p.Achievements)
                .Include(p => p.Gender)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<int> saveChangesAsync()
        {
            return await context.SaveChangesAsync();
        }
    }
}