using MindWeaveServer.DataAccess.Abstractions;
using System;
using System.Data.Entity;
using System.Threading.Tasks;

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
                .Include(m => m.DifficultyLevels)
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

        public void updateMatchStatus(Matches match, int newStatusId)
        {
            if (match != null)
            {
                match.match_status_id = newStatusId;
            }
        }

        public void updateMatchStartTime(Matches match)
        {
            if (match != null)
            {
                match.start_time = DateTime.UtcNow;
            }
        }

        public void updateMatchDifficulty(Matches match, int newDifficultyId)
        {
            if (match != null)
            {
                match.difficulty_id = newDifficultyId;
            }
        }

        public async Task updatePlayerScoreAsync(int matchId, int playerId, int newScore)
        {
            var participant = await context.MatchParticipants
                .FirstOrDefaultAsync(mp => mp.match_id == matchId && mp.player_id == playerId);

            if (participant != null)
            {
                participant.score = newScore;
                await saveChangesAsync();
            }
        }

        public async Task<int> saveChangesAsync()
        {
            return await context.SaveChangesAsync();
        }
    }
}