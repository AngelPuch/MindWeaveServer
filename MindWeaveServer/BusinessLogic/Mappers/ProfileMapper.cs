using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MindWeaveServer.BusinessLogic.Mappers
{
    public class ProfileMapper
    {
        public PlayerProfileViewDto mapToProfileViewDto(Player player)
        {
            if (player == null) return null;

            return new PlayerProfileViewDto
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
        }

        public UserProfileForEditDto mapToProfileForEditDto(Player player, List<Gender> allGenders)
        {
            if (player == null) return null;

            var availableGenders = allGenders.Select(mapToGenderDto).ToList();

            return new UserProfileForEditDto
            {
                firstName = player.first_name,
                lastName = player.last_name,
                dateOfBirth = player.date_of_birth,
                idGender = player.gender_id ?? 0,
                availableGenders = availableGenders
            };
        }

        public GenderDto mapToGenderDto(Gender gender)
        {
            if (gender == null) return null;

            return new GenderDto
            {
                idGender = gender.idGender,
                name = gender.gender1
            };
        }
    }
}