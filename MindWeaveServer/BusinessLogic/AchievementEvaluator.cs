using MindWeaveServer.Contracts.DataContracts.Game;
using System;
using System.Collections.Generic;

namespace MindWeaveServer.BusinessLogic
{
    public static class AchievementEvaluator
    {
        public const int ID_NOVICE_WEAVER = 1;
        public const int ID_PUZZLE_MASTER = 2;
        public const int ID_TIME_WARP = 4;
        public const int ID_FIRST_VICTORY = 5;
        public const int ID_HIGH_SCORER = 6;
        public const int ID_SPEED_DEMON = 7;
        public const int ID_HARDCORE_GAMER = 8;
        public const int ID_SUBJECT_ZERO = 10;
        public const int ID_PARTICIPATION_AWARD = 11;
        public const int ID_JUST_BEGINNING = 12;

        public static List<int> evaluate(AchievementContext ctx)
        {
            List<int> unlockedIds = new List<int>();

            if (ctx.PlayerStats == null || ctx.CurrentMatchStats == null)
            {
                return unlockedIds;
            }

            double currentMatchMinutes = calculateMatchMinutes(ctx);
            bool wonCurrent = ctx.CurrentMatchStats.final_rank == 1;

            checkProgressionAchievements(ctx, currentMatchMinutes, wonCurrent, unlockedIds);
            checkPerformanceAchievements(ctx, currentMatchMinutes, wonCurrent, unlockedIds);
            checkMiscellaneousAchievements(ctx, unlockedIds);

            return unlockedIds;
        }

        private static double calculateMatchMinutes(AchievementContext ctx)
        {
            if (ctx.MatchInfo.start_time.HasValue && ctx.MatchInfo.end_time.HasValue)
            {
                return (ctx.MatchInfo.end_time.Value - ctx.MatchInfo.start_time.Value).TotalMinutes;
            }

            DateTime endTime = ctx.MatchInfo.end_time ?? DateTime.UtcNow;
            DateTime startTime = ctx.MatchInfo.start_time ?? DateTime.UtcNow;
            double minutes = (endTime - startTime).TotalMinutes;

            return minutes < 0 ? 0 : minutes;
        }

        private static void checkProgressionAchievements(AchievementContext ctx, double currentMatchMinutes, bool wonCurrent, List<int> unlockedIds)
        {
            if ((ctx.PlayerStats.puzzles_completed + 1) >= 10)
            {
                unlockedIds.Add(ID_NOVICE_WEAVER);
            }

            int totalWins = (ctx.PlayerStats.puzzles_won ?? 0) + (wonCurrent ? 1 : 0);

            if (totalWins >= 50)
            {
                unlockedIds.Add(ID_PUZZLE_MASTER);
            }

            if ((ctx.PlayerStats.total_playtime_minutes + currentMatchMinutes) >= 600)
            {
                unlockedIds.Add(ID_TIME_WARP);
            }
        }

        private static void checkPerformanceAchievements(AchievementContext ctx, double currentMatchMinutes, bool wonCurrent, List<int> unlockedIds)
        {
            if (wonCurrent && ctx.PlayerStats.puzzles_won == 0)
            {
                unlockedIds.Add(ID_FIRST_VICTORY);
            }

            if (ctx.CurrentMatchStats.score > 1000)
            {
                unlockedIds.Add(ID_HIGH_SCORER);
            }

            if (wonCurrent && currentMatchMinutes < 5 && currentMatchMinutes > 0)
            {
                unlockedIds.Add(ID_SPEED_DEMON);
            }

            if (wonCurrent && ctx.MatchInfo.difficulty_id == 3)
            {
                unlockedIds.Add(ID_HARDCORE_GAMER);
            }
        }

        private static void checkMiscellaneousAchievements(AchievementContext ctx, List<int> unlockedIds)
        {
            if (ctx.PuzzleInfo != null && ctx.PuzzleInfo.player_id == ctx.CurrentMatchStats.player_id)
            {
                unlockedIds.Add(ID_SUBJECT_ZERO);
            }

            if (ctx.CurrentMatchStats.final_rank == ctx.TotalParticipants && ctx.TotalParticipants > 1)
            {
                unlockedIds.Add(ID_PARTICIPATION_AWARD);
            }

            if (ctx.CurrentMatchStats.pieces_placed >= 1)
            {
                unlockedIds.Add(ID_JUST_BEGINNING);
            }
        }
    }
}