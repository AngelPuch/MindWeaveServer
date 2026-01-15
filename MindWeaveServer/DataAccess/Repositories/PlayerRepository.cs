using MindWeaveServer.Contracts.DataContracts.Social;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Utilities;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Repositories
{
    public class PlayerRepository : IPlayerRepository
    {
        private readonly Func<MindWeaveDBEntities1> contextFactory;

        private const string DEFAULT_AVATAR_PATH = "/Resources/Images/Avatar/default_avatar.png";
        private const int DEFAULT_MAX_SEARCH_RESULTS = 10;
        private const int INITIAL_SEARCH_FETCH_LIMIT = 20;

        public PlayerRepository(Func<MindWeaveDBEntities1> contextFactory)
        {
            this.contextFactory = contextFactory;
        }

        public async Task<Player> getPlayerByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            using (var context = contextFactory())
            {
                return await context.Player.FirstOrDefaultAsync(p => p.email == email);
            }
        }

        public void addPlayer(Player player)
        {
            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            using (var context = contextFactory())
            {
                context.Player.Add(player);
                context.SaveChanges();
            }
        }

        public async Task updatePlayerAsync(Player player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            using (var context = contextFactory())
            {
                context.Entry(player).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }
        }
        public async Task updatePlayerProfileWithSocialsAsync(Player player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            using (var context = contextFactory())
            {
                var existingPlayer = await getPlayerWithSocialsAsync(context, player.idPlayer);

                if (existingPlayer != null)
                {
                    updatePlayerData(context, existingPlayer, player);

                    if (player.PlayerSocialMedias != null)
                    {
                        updateSocialMedias(context, existingPlayer, player.PlayerSocialMedias);
                    }

                    await context.SaveChangesAsync();
                }
            }
        }

        private async Task<Player> getPlayerWithSocialsAsync(MindWeaveDBEntities1 context, int playerId)
        {
            return await context.Player
                .Include(p => p.PlayerSocialMedias)
                .FirstOrDefaultAsync(p => p.idPlayer == playerId);
        }

        private void updatePlayerData(MindWeaveDBEntities1 context, Player existingPlayer, Player newPlayerData)
        {
            context.Entry(existingPlayer).CurrentValues.SetValues(newPlayerData);
        }

        private void updateSocialMedias(MindWeaveDBEntities1 context, Player existingPlayer, ICollection<PlayerSocialMedias> newSocials)
        {
            removeObsoleteSocials(context, existingPlayer, newSocials);
            upsertSocials(existingPlayer, newSocials);
        }

        private static void removeObsoleteSocials(MindWeaveDBEntities1 context, Player existingPlayer, ICollection<PlayerSocialMedias> newSocials)
        {
            var socialMediasToDelete = existingPlayer.PlayerSocialMedias
                .Where(existing => newSocials.All(newObj => newObj.IdSocialMediaPlatform != existing.IdSocialMediaPlatform))
                .ToList();

            foreach (var deletedSocial in socialMediasToDelete)
            {
                context.PlayerSocialMedias.Remove(deletedSocial);
            }
        }

        private void upsertSocials(Player existingPlayer, ICollection<PlayerSocialMedias> newSocials)
        {
            foreach (var newSocial in newSocials)
            {
                var existingSocial = existingPlayer.PlayerSocialMedias
                    .FirstOrDefault(e => e.IdSocialMediaPlatform == newSocial.IdSocialMediaPlatform);

                if (existingSocial != null)
                {
                    existingSocial.Username = newSocial.Username;
                }
                else
                {
                    existingPlayer.PlayerSocialMedias.Add(new PlayerSocialMedias
                    {
                        IdPlayer = existingPlayer.idPlayer,
                        IdSocialMediaPlatform = newSocial.IdSocialMediaPlatform,
                        Username = newSocial.Username
                    });
                }
            }
        }

        public async Task<Player> getPlayerByUsernameAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            using (var context = contextFactory())
            {
                return await context.Player
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));
            }
        }

        public async Task<Player> getPlayerWithProfileViewDataAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            using (var context = contextFactory())
            {
                return await context.Player
                    .Include(p => p.PlayerStats)
                    .Include(p => p.Achievements)
                    .Include(p => p.Gender)
                    .Include(p => p.PlayerSocialMedias.Select(sm => sm.SocialMediaPlatforms))
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));
            }
        }

        public async Task<List<PlayerSearchResultDto>> searchPlayersAsync(int requesterId, string query, int maxResults = DEFAULT_MAX_SEARCH_RESULTS)
        {
            using (var context = contextFactory())
            {
                var potentialMatchIds = await fetchPotentialPlayerIdsAsync(context, requesterId, query);

                if (!potentialMatchIds.Any())
                {
                    return new List<PlayerSearchResultDto>();
                }

                var existingRelationshipIds = await fetchExistingRelationshipIdsAsync(context, requesterId, potentialMatchIds);

                var validResultIds = potentialMatchIds.Except(existingRelationshipIds).ToList();

                if (!validResultIds.Any())
                {
                    return new List<PlayerSearchResultDto>();
                }

                return await fetchFinalPlayerResultsAsync(context, validResultIds, maxResults);
            }
        }

        private static async Task<List<int>> fetchPotentialPlayerIdsAsync(MindWeaveDBEntities1 context, int requesterId, string query)
        {
            return await context.Player
                .Where(p => p.username.Contains(query) && p.idPlayer != requesterId)
                .Select(p => p.idPlayer)
                .Take(INITIAL_SEARCH_FETCH_LIMIT)
                .ToListAsync();
        }

        private static async Task<List<int>> fetchExistingRelationshipIdsAsync(MindWeaveDBEntities1 context, int requesterId, List<int> potentialMatchIds)
        {
            return await context.Friendships
                .Where(f => (f.requester_id == requesterId && potentialMatchIds.Contains(f.addressee_id)) ||
                            (f.addressee_id == requesterId && potentialMatchIds.Contains(f.requester_id)))
                .Where(f => f.status_id == FriendshipStatusConstants.PENDING || f.status_id == FriendshipStatusConstants.ACCEPTED)
                .Select(f => f.requester_id == requesterId ? f.addressee_id : f.requester_id)
                .Distinct()
                .ToListAsync();
        }

        private static async Task<List<PlayerSearchResultDto>> fetchFinalPlayerResultsAsync(MindWeaveDBEntities1 context, List<int> validResultIds, int maxResults)
        {
            return await context.Player
                .Where(p => validResultIds.Contains(p.idPlayer))
                .OrderBy(p => p.username)
                .Select(p => new PlayerSearchResultDto
                {
                    Username = p.username,
                    AvatarPath = p.avatar_path ?? DEFAULT_AVATAR_PATH
                })
                .Take(maxResults)
                .ToListAsync();
        }

        public async Task<Player> getPlayerByUsernameWithTrackingAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            using (var context = contextFactory())
            {
                return await context.Player
                    .Include(p => p.PlayerSocialMedias.Select(sm => sm.SocialMediaPlatforms))
                    .FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));
            }
        }

        public async Task<List<SocialMediaPlatforms>> getAllSocialMediaPlatformsAsync()
        {
            using (var context = contextFactory())
            {
                return await context.SocialMediaPlatforms.ToListAsync();
            }
        }
    }
}