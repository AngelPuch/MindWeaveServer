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

        private const int PUZZLES_REQUIRED_NOVICE_WEAVER = 10;
        private const int WINS_REQUIRED_PUZZLE_MASTER = 50;
        private const int MINUTES_REQUIRED_TIME_WARP = 600;
        private const int SCORE_REQUIRED_HIGH_SCORER = 1000;
        private const int MINUTES_REQUIRED_SPEED_DEMON = 5;
        private const int PIECES_REQUIRED_JUST_BEGINNING = 1;

        private const int FIRST_PLACE_RANK = 1;
        private const int HARDCORE_DIFFICULTY_ID = 3;
        private const int MINIMUM_VALID_TIME = 0;
        private const int MINIMUM_PARTICIPANTS_FOR_LAST_PLACE = 1;
        private const int INCREMENT_CURRENT_PUZZLE = 1;
        private const int INCREMENT_CURRENT_WIN = 1;

        private class EvaluationData
        {
            public AchievementContext Context { get; set; }
            public double CurrentMatchMinutes { get; set; }
            public bool WonCurrent { get; set; }
            public List<int> UnlockedIds { get; set; }
        }

        public static List<int> evaluate(AchievementContext ctx)
        {
            List<int> unlockedIds = new List<int>();

            if (ctx.PlayerStats == null || ctx.CurrentMatchStats == null)
            {
                return unlockedIds;
            }

            double currentMatchMinutes = calculateMatchMinutes(ctx);
            bool wonCurrent = ctx.CurrentMatchStats.final_rank == FIRST_PLACE_RANK;

            var evaluationData = new EvaluationData
            {
                Context = ctx,
                CurrentMatchMinutes = currentMatchMinutes,
                WonCurrent = wonCurrent,
                UnlockedIds = unlockedIds
            };

            checkProgressionAchievements(evaluationData);
            checkPerformanceAchievements(evaluationData);
            checkMiscellaneousAchievements(evaluationData);

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

            return minutes < MINIMUM_VALID_TIME ? MINIMUM_VALID_TIME : minutes;
        }

        private static void checkProgressionAchievements(EvaluationData data)
        {
            if ((data.Context.PlayerStats.puzzles_completed + INCREMENT_CURRENT_PUZZLE) >= PUZZLES_REQUIRED_NOVICE_WEAVER)
            {
                data.UnlockedIds.Add(ID_NOVICE_WEAVER);
            }

            int totalWins = (data.Context.PlayerStats.puzzles_won ?? MINIMUM_VALID_TIME) + (data.WonCurrent ? INCREMENT_CURRENT_WIN : MINIMUM_VALID_TIME);

            if (totalWins >= WINS_REQUIRED_PUZZLE_MASTER)
            {
                data.UnlockedIds.Add(ID_PUZZLE_MASTER);
            }

            if ((data.Context.PlayerStats.total_playtime_minutes + data.CurrentMatchMinutes) >= MINUTES_REQUIRED_TIME_WARP)
            {
                data.UnlockedIds.Add(ID_TIME_WARP);
            }
        }

        private static void checkPerformanceAchievements(EvaluationData data)
        {
            if (data.WonCurrent && data.Context.PlayerStats.puzzles_won == MINIMUM_VALID_TIME)
            {
                data.UnlockedIds.Add(ID_FIRST_VICTORY);
            }

            if (data.Context.CurrentMatchStats.score > SCORE_REQUIRED_HIGH_SCORER)
            {
                data.UnlockedIds.Add(ID_HIGH_SCORER);
            }

            if (data.WonCurrent && data.CurrentMatchMinutes < MINUTES_REQUIRED_SPEED_DEMON && data.CurrentMatchMinutes > MINIMUM_VALID_TIME)
            {
                data.UnlockedIds.Add(ID_SPEED_DEMON);
            }

            if (data.WonCurrent && data.Context.MatchInfo.difficulty_id == HARDCORE_DIFFICULTY_ID)
            {
                data.UnlockedIds.Add(ID_HARDCORE_GAMER);
            }
        }

        private static void checkMiscellaneousAchievements(EvaluationData data)
        {
            if (data.Context.PuzzleInfo != null && data.Context.PuzzleInfo.player_id == data.Context.CurrentMatchStats.player_id)
            {
                data.UnlockedIds.Add(ID_SUBJECT_ZERO);
            }

            if (data.Context.CurrentMatchStats.final_rank == data.Context.TotalParticipants && data.Context.TotalParticipants > MINIMUM_PARTICIPANTS_FOR_LAST_PLACE)
            {
                data.UnlockedIds.Add(ID_PARTICIPATION_AWARD);
            }

            if (data.Context.CurrentMatchStats.pieces_placed >= PIECES_REQUIRED_JUST_BEGINNING)
            {
                data.UnlockedIds.Add(ID_JUST_BEGINNING);
            }
        }
    }
}