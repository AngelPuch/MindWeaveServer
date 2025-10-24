using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Utilities;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Repositories
{
    public class FriendshipRepository : IFriendshipRepository
    {
        private readonly MindWeaveDBEntities1 context;

        public FriendshipRepository(MindWeaveDBEntities1 context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<List<Friendships>> getAcceptedFriendshipsAsync(int playerId)
        {
            return await context.Friendships
                .Include(f => f.Player) 
                .Include(f => f.Player1) 
                .Where(f => (f.requester_id == playerId || f.addressee_id == playerId)
                            && f.status_id == FriendshipStatusConstants.ACCEPTED)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Friendships>> getPendingFriendRequestsAsync(int addresseeId)
        {
            return await context.Friendships
                .Include(f => f.Player1) 
                .Where(f => f.addressee_id == addresseeId && f.status_id == FriendshipStatusConstants.PENDING)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<Friendships> findFriendshipAsync(int player1Id, int player2Id)
        {
            
            return await context.Friendships
                .FirstOrDefaultAsync(f =>
                    (f.requester_id == player1Id && f.addressee_id == player2Id) ||
                    (f.requester_id == player2Id && f.addressee_id == player1Id));
        }

        public void addFriendship(Friendships friendship)
        {
            if (friendship == null)
            {
                throw new ArgumentNullException(nameof(friendship));
            }
            context.Friendships.Add(friendship);
        }

        public void updateFriendship(Friendships friendship)
        {
            if (friendship == null)
            {
                throw new ArgumentNullException(nameof(friendship));
            }
            var entry = context.Entry(friendship);
            if (entry.State == EntityState.Detached)
            {
                var existing = context.Friendships.Local.FirstOrDefault(f => f.friendships_id == friendship.friendships_id);
                if (existing != null)
                {
                    context.Entry(existing).State = EntityState.Detached;
                }
                context.Friendships.Attach(friendship);
                context.Entry(friendship).State = EntityState.Modified;
            }
            else
            {
                entry.State = EntityState.Modified;
            }
        }

        public void removeFriendship(Friendships friendship)
        {
            if (friendship == null)
            {
                throw new ArgumentNullException(nameof(friendship));
            }
            context.Friendships.Remove(friendship);
        }

        public async Task<int> saveChangesAsync()
        {
            return await context.SaveChangesAsync();
        }
    }
}