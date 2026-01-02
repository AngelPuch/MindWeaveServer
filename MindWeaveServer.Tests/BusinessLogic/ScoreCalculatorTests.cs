using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace MindWeaveServer.Tests.BusinessLogic
{
    public class ScoreCalculatorTests
    {
        private readonly ScoreCalculator scoreCalculator;

        public ScoreCalculatorTests()
        {
            scoreCalculator = new ScoreCalculator();
        }

        [Fact]
        public void calculatePointsForPlacementThrowsArgumentNullIfContextNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                scoreCalculator.calculatePointsForPlacement(null));
        }

        [Fact]
        public void calculatePointsForPlacementThrowsIfPlayerNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                scoreCalculator.calculatePointsForPlacement(new ScoreCalculationContext()));
        }

        [Fact]
        public void calculatePointsForPlacementReturnsBaseScoreForCenterPiece()
        {
            var context = new ScoreCalculationContext
            {
                Player = new PlayerSessionData { Score = 0 },
                IsEdgePiece = false
            };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(10, result.Points);
        }

        [Fact]
        public void calculatePointsForPlacementReturnsBaseScoreForEdgePiece()
        {
            var context = new ScoreCalculationContext
            {
                Player = new PlayerSessionData { Score = 0 },
                IsEdgePiece = true
            };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(5, result.Points);
        }

        [Fact]
        public void calculatePointsForPlacementAddsFirstBloodBonus()
        {
            var context = new ScoreCalculationContext
            {
                Player = new PlayerSessionData(),
                IsEdgePiece = false,
                IsFirstBloodAvailable = true
            };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(35, result.Points); 
            Assert.Contains("FIRST_BLOOD", result.BonusType);
            Assert.True(result.ClaimedFirstBlood);
        }

        [Fact]
        public void calculatePointsForPlacementAddsStreakBonusEvery3Pieces()
        {
            var player = new PlayerSessionData { CurrentStreak = 2 }; 
            var context = new ScoreCalculationContext
            {
                Player = player,
                IsEdgePiece = false
            };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(20, result.Points); 
            Assert.Contains("STREAK", result.BonusType);
            Assert.Equal(3, player.CurrentStreak);
        }

        [Fact]
        public void calculatePointsForPlacementAddsStreakBonusAt6Pieces()
        {
            var player = new PlayerSessionData { CurrentStreak = 5 };
            var context = new ScoreCalculationContext { Player = player, IsEdgePiece = false };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Contains("STREAK", result.BonusType);
            Assert.Equal(6, player.CurrentStreak);
        }

        [Fact]
        public void calculatePointsForPlacementDoesNotAddStreakBonusAtIntermediateSteps()
        {
            var player = new PlayerSessionData { CurrentStreak = 0 };
            var context = new ScoreCalculationContext { Player = player, IsEdgePiece = false };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.DoesNotContain("STREAK", result.BonusType ?? "");
            Assert.Equal(10, result.Points);
        }

        [Fact]
        public void calculatePointsForPlacementAddsFrenzyBonusWhenQuicklyPlacing5Pieces()
        {
            var player = new PlayerSessionData();
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < 4; i++) player.RecentPlacementTimestamps.Add(now);

            var context = new ScoreCalculationContext { Player = player, IsEdgePiece = false };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(50, result.Points); 
            Assert.Contains("FRENZY", result.BonusType);
            Assert.Empty(player.RecentPlacementTimestamps); 
        }

        [Fact]
        public void calculatePointsForPlacementDoesNotAddFrenzyIfTooSlow()
        {
            var player = new PlayerSessionData();
            DateTime past = DateTime.UtcNow.AddMinutes(-5);
            for (int i = 0; i < 4; i++) player.RecentPlacementTimestamps.Add(past);

            var context = new ScoreCalculationContext { Player = player, IsEdgePiece = false };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.DoesNotContain("FRENZY", result.BonusType ?? "");
            Assert.Equal(10, result.Points);
        }

        [Fact]
        public void calculatePointsForPlacementAddsLastHitBonus()
        {
            var context = new ScoreCalculationContext
            {
                Player = new PlayerSessionData(),
                IsEdgePiece = false,
                IsPuzzleComplete = true
            };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(60, result.Points); 
            Assert.Contains("LAST_HIT", result.BonusType);
        }

        [Fact]
        public void calculatePointsForPlacementHandlesMultipleBonuses()
        {
            var player = new PlayerSessionData { CurrentStreak = 2 };
            var context = new ScoreCalculationContext
            {
                Player = player,
                IsEdgePiece = true,
                IsFirstBloodAvailable = true
            };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(40, result.Points);
            Assert.Contains("FIRST_BLOOD", result.BonusType);
            Assert.Contains("STREAK", result.BonusType);
        }

        [Fact]
        public void calculatePenaltyPointsReturnsCorrectValue()
        {
            int penalty = scoreCalculator.calculatePenaltyPoints(2);
            Assert.Equal(10, penalty); 
        }

        [Fact]
        public void calculatePenaltyPointsCapsMinimumAt1()
        {
            int penalty = scoreCalculator.calculatePenaltyPoints(0);
            Assert.Equal(5, penalty); 
        }

        [Fact]
        public void calculatePenaltyPointsScalesLinearly()
        {
            int penalty = scoreCalculator.calculatePenaltyPoints(10);
            Assert.Equal(50, penalty);
        }
    }
}