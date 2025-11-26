using MindWeaveServer.Contracts.DataContracts.Game;
using System;
using System.Collections.Generic;

namespace MindWeaveServer.BusinessLogic
{
    public static class AchievementEvaluator
    {
        public const int ID_NOVICE_WEAVER = 1;
        public const int ID_PUZZLE_MASTER = 2;
        public const int ID_PIECE_COLLECTOR = 3;
        public const int ID_TIME_WARP = 4;
        public const int ID_FIRST_VICTORY = 5;
        public const int ID_HIGH_SCORER = 6;
        public const int ID_SPEED_DEMON = 7;
        public const int ID_HARDCORE_GAMER = 8;
        public const int ID_HOST_WITH_THE_MOST = 9;
        public const int ID_SUBJECT_ZERO = 10;
        public const int ID_PARTICIPATION_AWARD = 11;
        public const int ID_JUST_BEGINNING = 12;

        public static List<int> Evaluate(AchievementContext ctx)
        {
            List<int> unlockedIds = new List<int>();

            if (ctx.PlayerStats == null || ctx.CurrentMatchStats == null) { return unlockedIds; }
            int historicalGames = ctx.PlayerStats.puzzles_completed ?? 0;
            int historicalTime = ctx.PlayerStats.total_playtime_minutes ?? 0;

            DateTime endTime = ctx.MatchInfo.end_time ?? DateTime.UtcNow;
            DateTime startTime = ctx.MatchInfo.start_time ?? DateTime.UtcNow;
            double currentMatchMinutes = (endTime - startTime).TotalMinutes;
            if (currentMatchMinutes < 0) { currentMatchMinutes = 0;}
            if ((ctx.PlayerStats.puzzles_completed + 1) >= 10)
            {
                unlockedIds.Add(ID_NOVICE_WEAVER);
            }

            bool wonCurrent = ctx.CurrentMatchStats.final_rank == 1;
            int totalWins = (ctx.PlayerStats.puzzles_won ?? 0) + (wonCurrent ? 1 : 0);

            if (totalWins >= 50)
            {
                unlockedIds.Add(ID_PUZZLE_MASTER);
            }

            
            if (ctx.MatchInfo.start_time.HasValue && ctx.MatchInfo.end_time.HasValue)
            {
                currentMatchMinutes = (ctx.MatchInfo.end_time.Value - ctx.MatchInfo.start_time.Value).TotalMinutes;
            }

            if ((ctx.PlayerStats.total_playtime_minutes + currentMatchMinutes) >= 600)
            {
                unlockedIds.Add(ID_TIME_WARP);
            }

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

            return unlockedIds;
        }
    }
}