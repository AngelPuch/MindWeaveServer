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
        private readonly Func<MindWeaveDBEntities1> contextFactory;

        public FriendshipRepository(Func<MindWeaveDBEntities1> contextFactory)
        {
            this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task<List<Friendships>> getAcceptedFriendshipsAsync(int playerId)
        {
            using (var context = contextFactory())
            {
                return await context.Friendships
                    .Include(f => f.Player)
                    .Include(f => f.Player1)
                    .Where(f => (f.requester_id == playerId || f.addressee_id == playerId)
                                && f.status_id == FriendshipStatusConstants.ACCEPTED)
                    .AsNoTracking()
                    .ToListAsync();
            }
        }

        public async Task<List<Friendships>> getPendingFriendRequestsAsync(int addresseeId)
        {
            using (var context = contextFactory())
            {
                return await context.Friendships
                    .Include(f => f.Player1)
                    .Where(f => f.addressee_id == addresseeId && f.status_id == FriendshipStatusConstants.PENDING)
                    .AsNoTracking()
                    .ToListAsync();
            }
        }

        public async Task<Friendships> findFriendshipAsync(int player1Id, int player2Id)
        {
            using (var context = contextFactory())
            {
                return await context.Friendships
                    .FirstOrDefaultAsync(f =>
                        (f.requester_id == player1Id && f.addressee_id == player2Id) ||
                        (f.requester_id == player2Id && f.addressee_id == player1Id));
            }
        }

        public void addFriendship(Friendships friendship)
        {
            if (friendship == null)
            {
                throw new ArgumentNullException(nameof(friendship));
            }

            using (var context = contextFactory())
            {
                context.Friendships.Add(friendship);
                context.SaveChanges();
            }
        }

        public void updateFriendship(Friendships friendship)
        {
            if (friendship == null)
            { 
                throw new ArgumentNullException(nameof(friendship));
            }

            using (var context = contextFactory())
            {
                context.Entry(friendship).State = EntityState.Modified;
                context.SaveChanges();
            }
        }

        public void removeFriendship(Friendships friendship)
        {
            if (friendship == null)
            {
                throw new ArgumentNullException(nameof(friendship));
            }

            using (var context = contextFactory())
            {
                context.Entry(friendship).State = EntityState.Deleted;
                context.SaveChanges();
            }
        }
    }
}