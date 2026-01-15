using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Abstractions
{
    public interface IGuestInvitationRepository
    {
        Task addInvitationAsync(GuestInvitations invitation);
        Task<GuestInvitations> findValidInvitationAsync(int matchId, string guestEmail);
        Task markInvitationAsUsedAsync(GuestInvitations invitation);
    }
}