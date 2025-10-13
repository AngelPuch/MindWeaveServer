// MindWeaveServer/BusinessLogic/ProfileLogic.cs

using MindWeaveServer.Contracts.DataContracts; // <-- CORREGIDO: Usamos el namespace correcto
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Resources;
using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class ProfileLogic
    {
        // ... (getPlayerProfileViewAsync y getPlayerProfileForEditAsync se quedan como estaban) ...
        public async Task<PlayerProfileViewDto> getPlayerProfileViewAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            using (var context = new MindWeaveDBEntities1())
            {
                var player = await context.Player.Include(p => p.PlayerStats).Include(p => p.Achievements).Include(p => p.Gender).AsNoTracking().FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));
                if (player == null) return null;
                var profileViewDto = new PlayerProfileViewDto
                {
                    username = player.username,
                    avatarPath = player.avatar_path,
                    firstName = player.first_name,
                    lastName = player.last_name,
                    dateOfBirth = player.date_of_birth,
                    gender = player.Gender?.gender1,
                    stats = new PlayerStatsDto
                    {
                        puzzlesCompleted = player.PlayerStats?.puzzles_completed ?? 0,
                        puzzlesWon = player.PlayerStats?.puzzles_won ?? 0,
                        totalPlaytime = TimeSpan.FromMinutes(player.PlayerStats?.total_playtime_minutes ?? 0),
                        highestScore = player.PlayerStats?.highest_score ?? 0
                    },
                    achievements = player.Achievements.Select(ach => new AchievementDto
                    {
                        name = ach.name,
                        description = ach.description,
                        iconPath = ach.icon_path
                    }).ToList()
                };
                return profileViewDto;
            }
        }

        public async Task<UserProfileForEditDto> getPlayerProfileForEditAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            using (var context = new MindWeaveDBEntities1())
            {
                var player = await context.Player.AsNoTracking().FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));
                if (player == null) return null;
                var allGenders = await context.Gender.AsNoTracking().Select(g => new GenderDto { idGender = g.idGender, name = g.gender1 }).ToListAsync();
                var userProfile = new UserProfileForEditDto
                {
                    firstName = player.first_name,
                    lastName = player.last_name,
                    dateOfBirth = player.date_of_birth,
                    idGender = player.gender_id ?? 0,
                    availableGenders = allGenders
                };
                return userProfile;
            }
        }

        /// <summary>
        /// Recibe los datos modificados del cliente y los GUARDA en la base de datos. (Fase 3 - Guardado)
        /// </summary>
        public async Task<OperationResultDto> updateProfileAsync(string username, UserProfileForEditDto updatedProfile)
        {
            var result = new OperationResultDto();

            if (updatedProfile == null || string.IsNullOrWhiteSpace(updatedProfile.firstName) || string.IsNullOrWhiteSpace(updatedProfile.lastName))
            {
                result.success = false; // <-- CORREGIDO: Usamos 'success'
                result.message = "First name and last name cannot be empty.";
                return result;
            }

            try
            {
                using (var context = new MindWeaveDBEntities1())
                {
                    var playerToUpdate = await context.Player.FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));

                    if (playerToUpdate == null)
                    {
                        result.success = false; // <-- CORREGIDO: Usamos 'success'
                        result.message = "Player not found.";
                        return result;
                    }

                    playerToUpdate.first_name = updatedProfile.firstName;
                    playerToUpdate.last_name = updatedProfile.lastName;
                    playerToUpdate.date_of_birth = updatedProfile.dateOfBirth;
                    playerToUpdate.gender_id = updatedProfile.idGender;

                    await context.SaveChangesAsync();

                    result.success = true; // <-- CORREGIDO: Usamos 'success'
                    result.message = "Profile updated successfully.";
                }
            }
            catch (Exception ex)
            {
                // TO-DO: Registrar el error real en un sistema de logs (Console.WriteLine(ex.Message);)
                result.success = false; // <-- CORREGIDO: Usamos 'success'
                result.message = "An unexpected error occurred while updating the profile.";
            }

            return result;
        }

        // <summary>
        /// Actualiza únicamente la ruta del avatar de un jugador en la base de datos.
        /// Se usa para seleccionar avatares precargados.
        /// </summary>
        public async Task<OperationResultDto> updateAvatarPathAsync(string username, string newAvatarPath)
        {
            var result = new OperationResultDto { success = false };

            if (string.IsNullOrWhiteSpace(newAvatarPath))
            {
                result.message = Lang.ErrorAvatarPathCannotBeEmpty;
                return result;
            }

            try
            {
                using (var context = new MindWeaveDBEntities1())
                {
                    var playerToUpdate = await context.Player
                        .FirstOrDefaultAsync(p => p.username.Equals(username, StringComparison.OrdinalIgnoreCase));

                    if (playerToUpdate == null)
                    {
                        result.message = Lang.ErrorPlayerNotFound;
                        return result;
                    }

                    // Actualizamos únicamente el campo del avatar
                    playerToUpdate.avatar_path = newAvatarPath;

                    await context.SaveChangesAsync();

                    result.success = true;
                    result.message = Lang.SuccessAvatarUpdated;
                }
            }
            catch (Exception)
            {
                result.message = Lang.ErrorAvatarUpdateFailed;
            }

            return result;
        }


    }
}