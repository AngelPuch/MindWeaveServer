using System.Collections.Generic;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Abstractions
{
    public interface IFriendshipRepository
    {
        
        Task<List<Friendships>> getAcceptedFriendshipsAsync(int playerId);

        Task<List<Friendships>> getPendingFriendRequestsAsync(int addresseeId);

        Task<Friendships> findFriendshipAsync(int player1Id, int player2Id);

        void addFriendship(Friendships friendship);

        void updateFriendship(Friendships friendship);

        void removeFriendship(Friendships friendship);

        Task<int> saveChangesAsync();
    }
}