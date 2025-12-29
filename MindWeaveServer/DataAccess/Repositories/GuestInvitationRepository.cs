using MindWeaveServer.DataAccess.Abstractions;
using System;
using System.Data.Entity;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Repositories
{
    public class GuestInvitationRepository : IGuestInvitationRepository
    {
        private readonly Func<MindWeaveDBEntities1> contextFactory;

        public GuestInvitationRepository(Func<MindWeaveDBEntities1> contextFactory)
        {
            this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task addInvitationAsync(GuestInvitations invitation)
        {
            if (invitation == null)
            {
                throw new ArgumentNullException(nameof(invitation));
            }

            using (var context = contextFactory())
            {
                context.GuestInvitations.Add(invitation);
                await context.SaveChangesAsync();
            }
        }

        public async Task<GuestInvitations> findValidInvitationAsync(int matchId, string guestEmail)
        {
            DateTime now = DateTime.UtcNow;

            using (var context = contextFactory())
            {
                return await context.GuestInvitations
                    .FirstOrDefaultAsync(inv => inv.match_id == matchId
                                             && inv.guest_email.Equals(guestEmail, StringComparison.OrdinalIgnoreCase)
                                             && inv.used_timestamp == null
                                             && inv.expiry_timestamp > now);
            }
        }

        public async Task markInvitationAsUsedAsync(GuestInvitations invitation)
        {
            if (invitation == null)
            {
                return;
            }

            using (var context = contextFactory())
            {
                invitation.used_timestamp = DateTime.UtcNow;

                context.Entry(invitation).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }
        }

        public async Task<int> saveChangesAsync()
        {
            return await Task.FromResult(0);
        }
    }
}