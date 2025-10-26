using MindWeaveServer.DataAccess.Abstractions;
using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Threading.Tasks;
using System.Linq; // Added for FirstOrDefaultAsync

namespace MindWeaveServer.DataAccess.Repositories
{
    public class MatchmakingRepository : IMatchmakingRepository
    {
        private readonly MindWeaveDBEntities1 context;

        public MatchmakingRepository(MindWeaveDBEntities1 context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<bool> doesLobbyCodeExistAsync(string lobbyCode)
        {
            return await context.Matches.AnyAsync(m => m.lobby_code == lobbyCode);
        }

        public async Task<Matches> createMatchAsync(Matches match)
        {
            context.Matches.Add(match);
            await saveChangesAsync();
            return match; 
        }

        public async Task<MatchParticipants> addParticipantAsync(MatchParticipants participant)
        {
            context.MatchParticipants.Add(participant);
            await saveChangesAsync();
            return participant;
        }

        public async Task<Matches> getMatchByLobbyCodeAsync(string lobbyCode)
        {
            return await context.Matches
                .FirstOrDefaultAsync(m => m.lobby_code == lobbyCode);
        }

        public async Task<MatchParticipants> getParticipantAsync(int matchId, int playerId)
        {
            return await context.MatchParticipants
                .FirstOrDefaultAsync(mp => mp.match_id == matchId && mp.player_id == playerId);
        }

        public async Task<bool> removeParticipantAsync(MatchParticipants participant)
        {
            context.MatchParticipants.Remove(participant);
            return await saveChangesAsync() > 0;
        }

        public async Task<int> updateMatchStatusAsync(Matches match, int newStatusId)
        {
            match.match_status_id = newStatusId;
            context.Entry(match).State = EntityState.Modified;
            return await saveChangesAsync();
        }

        public async Task<int> updateMatchStartTimeAsync(Matches match)
        {
            match.start_time = DateTime.UtcNow;
            context.Entry(match).State = EntityState.Modified;
            return await saveChangesAsync();
        }

        public async Task<int> updateMatchDifficultyAsync(Matches match, int newDifficultyId)
        {
            match.difficulty_id = newDifficultyId;
            context.Entry(match).State = EntityState.Modified;
            return await saveChangesAsync();
        }

        public async Task<int> saveChangesAsync()
        {
            try
            {
                return await context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // Basic logging, consider a more robust logging framework
                Console.WriteLine($"Database Update Error: {ex.InnerException?.InnerException?.Message ?? ex.Message}");
                // Handle specific exceptions if necessary (e.g., unique key violation)
                throw; // Re-throw to allow higher layers to handle
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Generic Database Error: {ex.Message}");
                throw;
            }
        }
    }
}