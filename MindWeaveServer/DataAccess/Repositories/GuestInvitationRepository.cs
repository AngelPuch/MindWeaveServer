using MindWeaveServer.DataAccess.Abstractions;
using System;
using System.Data.Entity;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Repositories
{
    public class GuestInvitationRepository : IGuestInvitationRepository
    {
        private readonly MindWeaveDBEntities1 context;

        public GuestInvitationRepository(MindWeaveDBEntities1 context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public Task addInvitationAsync(GuestInvitations invitation)
        {
            if (invitation == null)
            {
                throw new ArgumentNullException(nameof(invitation));
            }
            context.GuestInvitations.Add(invitation);
            return Task.CompletedTask;
        }

        public async Task<GuestInvitations?> findValidInvitationAsync(int matchId, string guestEmail)
        {
            DateTime now = DateTime.UtcNow;
            return await context.GuestInvitations
                .FirstOrDefaultAsync(inv => inv.match_id == matchId
                                         && inv.guest_email.Equals(guestEmail, StringComparison.OrdinalIgnoreCase)
                                         && inv.used_timestamp == null
                                         && inv.expiry_timestamp > now);
        }

        public Task markInvitationAsUsedAsync(GuestInvitations invitation)
        {
            if (invitation != null)
            {
                invitation.used_timestamp = DateTime.UtcNow;
                context.Entry(invitation).State = EntityState.Modified;
            }
            return Task.CompletedTask;
        }

        public async Task<int> saveChangesAsync()
        {
            return await context.SaveChangesAsync();
        }
    }
}