using MindWeaveServer.BusinessLogic.Abstractions;
using System;
using System.Collections.Generic;
using MindWeaveServer.BusinessLogic.Models;

namespace MindWeaveServer.BusinessLogic
{
    public class ScoreCalculator : IScoreCalculator
    {
        private const int SCORE_EDGE_PIECE = 5;
        private const int SCORE_CENTER_PIECE = 10;
        private const int SCORE_STREAK_BONUS = 10;
        private const int SCORE_FRENZY_BONUS = 40;
        private const int SCORE_FIRST_BLOOD = 25;
        private const int SCORE_LAST_HIT = 50;
        private const int PENALTY_BASE_MISS = 5;

        private const int STREAK_THRESHOLD = 3;
        private const int FRENZY_COUNT = 5;
        private const int FRENZY_TIME_WINDOW_SECONDS = 60;

        private const string BONUS_FIRST_BLOOD = "FIRST_BLOOD";
        private const string BONUS_LAST_HIT = "LAST_HIT";
        private const string BONUS_STREAK = "STREAK";
        private const string BONUS_FRENZY = "FRENZY";

        public ScoreResult calculatePointsForPlacement(ScoreCalculationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.Player == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            int points = calculateBasePoints(context.IsEdgePiece);
            var bonuses = new List<string>();
            bool claimedFirstBlood = false;

            if (context.IsFirstBloodAvailable)
            {
                points += SCORE_FIRST_BLOOD;
                bonuses.Add(BONUS_FIRST_BLOOD);
                claimedFirstBlood = true;
            }

            points += calculateStreakBonus(context.Player, bonuses);
            points += calculateFrenzyBonus(context.Player, bonuses);

            if (context.IsPuzzleComplete)
            {
                points += SCORE_LAST_HIT;
                bonuses.Add(BONUS_LAST_HIT);
            }

            string bonusString = bonuses.Count > 0 ? string.Join(",", bonuses) : null;

            return new ScoreResult
            {
                Points = points,
                BonusType = bonusString,
                ClaimedFirstBlood = claimedFirstBlood
            };
        }

        public int calculatePenaltyPoints(int negativeStreak)
        {
            return PENALTY_BASE_MISS * Math.Max(1, negativeStreak);
        }

        private static int calculateBasePoints(bool isEdgePiece)
        {
            return isEdgePiece ? SCORE_EDGE_PIECE : SCORE_CENTER_PIECE;
        }

        private static int calculateStreakBonus(PlayerSessionData player, List<string> bonuses)
        {
            player.CurrentStreak++;

            if (player.CurrentStreak % STREAK_THRESHOLD == 0)
            {
                bonuses.Add(BONUS_STREAK);
                return SCORE_STREAK_BONUS;
            }

            return 0;
        }

        private static int calculateFrenzyBonus(PlayerSessionData player, List<string> bonuses)
        {
            DateTime now = DateTime.UtcNow;
            player.RecentPlacementTimestamps.Add(now);

            if (player.RecentPlacementTimestamps.Count < FRENZY_COUNT)
            {
                return 0;
            }

            int count = player.RecentPlacementTimestamps.Count;
            DateTime windowStart = player.RecentPlacementTimestamps[count - FRENZY_COUNT];

            if ((now - windowStart).TotalSeconds <= FRENZY_TIME_WINDOW_SECONDS)
            {
                bonuses.Add(BONUS_FRENZY);
                player.RecentPlacementTimestamps.Clear();
                return SCORE_FRENZY_BONUS;
            }

            return 0;
        }
    }
}