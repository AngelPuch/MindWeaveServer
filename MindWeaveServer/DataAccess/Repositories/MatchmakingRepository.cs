using MindWeaveServer.DataAccess.Abstractions;
using System;
using System.Data.Entity;
using System.Linq;
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
            using (var freshContext = new MindWeaveDBEntities1())
            {
                var participant = await freshContext.MatchParticipants
                    .FirstOrDefaultAsync(mp => mp.match_id == matchId && mp.player_id == playerId);

                if (participant != null)
                {
                    participant.score = newScore;
                    await freshContext.SaveChangesAsync();
                }
            }
        }

        public async Task finishMatchAsync(int matchId)
        {
            using (var freshContext = new MindWeaveDBEntities1())
            {
                var match = await freshContext.Matches.FirstOrDefaultAsync(m => m.matches_id == matchId);
                if (match != null)
                {
                    match.end_time = DateTime.Now;
                    match.match_status_id = 2;
                    await freshContext.SaveChangesAsync();
                }
            }
        }

        public int getMatchDuration(int matchId)
        {
            var match = context.Matches.FirstOrDefault(m => m.matches_id == matchId);

            if (match != null)
            {
                var difficulty = context.DifficultyLevels.FirstOrDefault(d => d.idDifficulty == match.difficulty_id);

                if (difficulty != null)
                {
                    return difficulty.time_limit_seconds;
                }
            }

            return 300;
        }

        public async Task<int> saveChangesAsync()
        {
            return await context.SaveChangesAsync();
        }
    }
}