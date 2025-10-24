using MindWeaveServer.Contracts.DataContracts.Social; 
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Utilities; 
using System;
using System.Collections.Generic; 
using System.Data.Entity;
using System.Linq;
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
            if (string.IsNullOrWhiteSpace(email)) return null;
            return await context.Player.FirstOrDefaultAsync(p => p.email == email);
        }

        public void addPlayer(Player player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            context.Player.Add(player);
        }

        public async Task<Player> getPlayerByUsernameAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            return await context.Player
                .FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<Player> getPlayerWithProfileViewDataAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            return await context.Player
                .Include(p => p.PlayerStats)
                .Include(p => p.Achievements)
                .Include(p => p.Gender)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<PlayerSearchResultDto>> SearchPlayersAsync(int requesterId, string query, int maxResults = 10)
        {
            const int INITIAL_FETCH_LIMIT = 20;

            var potentialMatchIds = await context.Player
                .Where(p => p.username.Contains(query) && p.idPlayer != requesterId)
                .Select(p => p.idPlayer)
                .Take(INITIAL_FETCH_LIMIT)
                .ToListAsync();

            if (!potentialMatchIds.Any())
            {
                return new List<PlayerSearchResultDto>(); 
            }

            var existingRelationshipIds = await context.Friendships
                .Where(f => (f.requester_id == requesterId && potentialMatchIds.Contains(f.addressee_id)) ||
                            (f.addressee_id == requesterId && potentialMatchIds.Contains(f.requester_id)))
                .Where(f => f.status_id == FriendshipStatusConstants.PENDING || f.status_id == FriendshipStatusConstants.ACCEPTED)
                .Select(f => f.requester_id == requesterId ? f.addressee_id : f.requester_id) 
                .Distinct()
                .ToListAsync();

            var validResultIds = potentialMatchIds.Except(existingRelationshipIds).ToList();

            if (!validResultIds.Any())
            {
                return new List<PlayerSearchResultDto>();
            }

            var finalResults = await context.Player
                .Where(p => validResultIds.Contains(p.idPlayer))
                .OrderBy(p => p.username) // Order alphabetically
                .Select(p => new PlayerSearchResultDto
                {
                    username = p.username,
                    avatarPath = p.avatar_path ?? "/Resources/Images/Avatar/default_avatar.png"
                })
                .Take(maxResults)
                .ToListAsync();

            return finalResults;
        }

        public async Task<int> saveChangesAsync()
        {
            return await context.SaveChangesAsync();
        }
    }
}