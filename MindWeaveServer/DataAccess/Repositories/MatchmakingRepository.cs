using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.DataAccess.Abstractions;
using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Repositories
{
    public class MatchmakingRepository : IMatchmakingRepository
    {
        private readonly Func<MindWeaveDBEntities1> contextFactory;

        private const int MATCH_STATUS_FINISHED = 2;
        private const int DEFAULT_MATCH_DURATION_SECONDS = 300;

        public MatchmakingRepository(Func<MindWeaveDBEntities1> contextFactory)
        {
            this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task<bool> doesLobbyCodeExistAsync(string lobbyCode)
        {
            using (var context = contextFactory())
            {
                return await context.Matches.AnyAsync(m => m.lobby_code == lobbyCode);
            }
        }

        public async Task<Matches> createMatchAsync(Matches match)
        {
            using (var context = contextFactory())
            {
                context.Matches.Add(match);
                await context.SaveChangesAsync();
                return match;
            }
        }

        public async Task<MatchParticipants> addParticipantAsync(MatchParticipants participant)
        {
            using (var context = contextFactory())
            {
                context.MatchParticipants.Add(participant);
                await context.SaveChangesAsync();
                return participant;
            }
        }

        public async Task<Matches> getMatchByLobbyCodeAsync(string lobbyCode)
        {
            using (var context = contextFactory())
            {
                return await context.Matches
                    .Include(m => m.DifficultyLevels)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.lobby_code == lobbyCode);
            }
        }

        public async Task<MatchParticipants> getParticipantAsync(int matchId, int playerId)
        {
            using (var context = contextFactory())
            {
                return await context.MatchParticipants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(mp => mp.match_id == matchId && mp.player_id == playerId);
            }
        }

        public async Task<bool> removeParticipantAsync(MatchParticipants participant)
        {
            using (var context = contextFactory())
            {
                context.Entry(participant).State = EntityState.Deleted;
                return await context.SaveChangesAsync() > 0;
            }
        }

        public void updateMatchStatus(Matches match, int newStatusId)
        {
            if (match == null)
            {
                return;
            }

            using (var context = contextFactory())
            {
                var entity = new Matches { matches_id = match.matches_id, match_status_id = newStatusId };
                context.Matches.Attach(entity);
                context.Entry(entity).Property(x => x.match_status_id).IsModified = true;
                context.SaveChanges();

                match.match_status_id = newStatusId;
            }
        }

        public void updateMatchStartTime(Matches match)
        {
            if (match == null)
            {
                return;
            }

            using (var context = contextFactory())
            {
                var now = DateTime.UtcNow;
                var entity = new Matches { matches_id = match.matches_id, start_time = now };
                context.Matches.Attach(entity);
                context.Entry(entity).Property(x => x.start_time).IsModified = true;
                context.SaveChanges();

                match.start_time = now;
            }
        }

        public void updateMatchDifficulty(Matches match, int newDifficultyId)
        {
            if (match == null)
            {
                return;
            }

            using (var context = contextFactory())
            {
                var entity = new Matches { matches_id = match.matches_id, difficulty_id = newDifficultyId };
                context.Matches.Attach(entity);
                context.Entry(entity).Property(x => x.difficulty_id).IsModified = true;
                context.SaveChanges();

                match.difficulty_id = newDifficultyId;
            }
        }

        public void AddPlaytimeOnly(int playerId, int minutesPlayed)
        {
            using (var context = contextFactory())
            {
                var stats = context.PlayerStats.FirstOrDefault(s => s.player_id == playerId);
                if (stats != null)
                {
                    stats.total_playtime_minutes += minutesPlayed;
                    context.SaveChanges();
                }
            }
        }

        public async Task updateMatchParticipantStatsAsync(MatchParticipantStatsUpdateDto updateData)
        {
            using (var context = contextFactory())
            {
                var participant = await context.MatchParticipants
                    .FirstOrDefaultAsync(mp => mp.match_id == updateData.MatchId && mp.player_id == updateData.PlayerId);

                if (participant != null)
                {
                    participant.score = updateData.Score;
                    participant.pieces_placed = updateData.PiecesPlaced;
                    participant.final_rank = updateData.Rank;

                    await context.SaveChangesAsync();
                }
            }
        }

        public async Task finishMatchAsync(int matchId)
        {
            using (var context = contextFactory())
            {
                var match = await context.Matches.FirstOrDefaultAsync(m => m.matches_id == matchId);
                if (match != null)
                {
                    match.end_time = DateTime.UtcNow;
                    match.match_status_id = MATCH_STATUS_FINISHED;
                    await context.SaveChangesAsync();
                }
            }
        }

        public int getMatchDuration(int matchId)
        {
            using (var context = contextFactory())
            {
                var matchInfo = context.Matches
                    .Where(m => m.matches_id == matchId)
                    .Select(m => new { m.difficulty_id })
                    .FirstOrDefault();

                if (matchInfo != null)
                {
                    var timeLimit = context.DifficultyLevels
                        .Where(d => d.idDifficulty == matchInfo.difficulty_id)
                        .Select(d => d.time_limit_seconds)
                        .FirstOrDefault();

                    return timeLimit > 0 ? timeLimit : DEFAULT_MATCH_DURATION_SECONDS;
                }

                return DEFAULT_MATCH_DURATION_SECONDS;
            }
        }

        public async Task<int> saveChangesAsync()
        {
            return await Task.FromResult(0);
        }

        public async Task<Matches> getMatchByIdAsync(int matchId)
        {
            using (var context = contextFactory())
            {
                return await context.Matches
                    .Include(m => m.DifficultyLevels)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.matches_id == matchId);
            }
        }

        public async Task registerExpulsionAsync(ExpulsionDto expulsionData)
        {
            if (expulsionData == null)
            {
                throw new ArgumentNullException(nameof(expulsionData));
            }

            using (var context = contextFactory())
            {
                var expulsionEntity = new MatchExpulsions
                {
                    match_id = expulsionData.MatchId,
                    expelled_player_id = expulsionData.PlayerId,
                    reason_id = expulsionData.ReasonId,
                    host_player_id = expulsionData.HostPlayerId,
                    expulsion_time = DateTime.UtcNow
                };

                context.MatchExpulsions.Add(expulsionEntity);
                await context.SaveChangesAsync();
            }
        }

        public async Task updatePlayerScoreAsync(int matchId, int playerId, int score)
        {
            using (var context = contextFactory())
            {
                var participant = await context.MatchParticipants
                    .FirstOrDefaultAsync(p => p.match_id == matchId && p.player_id == playerId);

                if (participant != null)
                {
                    participant.score = score;
                    await context.SaveChangesAsync();
                }
            }
        }
    }
}