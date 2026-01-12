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
        public void CalculatePointsForPlacement_NullContext_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                scoreCalculator.calculatePointsForPlacement(null));
        }

        [Fact]
        public void CalculatePointsForPlacement_NullPlayer_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                scoreCalculator.calculatePointsForPlacement(new ScoreCalculationContext()));
        }

        [Fact]
        public void CalculatePointsForPlacement_CenterPiece_ReturnsBaseScore()
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
        public void CalculatePointsForPlacement_EdgePiece_ReturnsBaseScore()
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
        public void CalculatePointsForPlacement_FirstBloodAvailable_AddsBonus()
        {
            var context = new ScoreCalculationContext
            {
                Player = new PlayerSessionData(),
                IsEdgePiece = false,
                IsFirstBloodAvailable = true
            };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(35, result.Points);
        }

        [Fact]
        public void CalculatePointsForPlacement_StreakEvery3_AddsBonus()
        {
            var player = new PlayerSessionData { CurrentStreak = 2 };
            var context = new ScoreCalculationContext
            {
                Player = player,
                IsEdgePiece = false
            };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(20, result.Points);
        }

        [Fact]
        public void CalculatePointsForPlacement_StreakAt6_AddsBonus()
        {
            var player = new PlayerSessionData { CurrentStreak = 5 };
            var context = new ScoreCalculationContext { Player = player, IsEdgePiece = false };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Contains("STREAK", result.BonusType);
        }

        [Fact]
        public void CalculatePointsForPlacement_IntermediateStreak_NoBonus()
        {
            var player = new PlayerSessionData { CurrentStreak = 0 };
            var context = new ScoreCalculationContext { Player = player, IsEdgePiece = false };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(10, result.Points);
        }

        [Fact]
        public void CalculatePointsForPlacement_FrenzyQuick5_AddsBonus()
        {
            var player = new PlayerSessionData();
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < 4; i++) player.RecentPlacementTimestamps.Add(now);

            var context = new ScoreCalculationContext { Player = player, IsEdgePiece = false };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(50, result.Points);
        }

        [Fact]
        public void CalculatePointsForPlacement_FrenzyTooSlow_NoBonus()
        {
            var player = new PlayerSessionData();
            DateTime past = DateTime.UtcNow.AddMinutes(-5);
            for (int i = 0; i < 4; i++) player.RecentPlacementTimestamps.Add(past);

            var context = new ScoreCalculationContext { Player = player, IsEdgePiece = false };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(10, result.Points);
        }

        [Fact]
        public void CalculatePointsForPlacement_PuzzleComplete_AddsLastHitBonus()
        {
            var context = new ScoreCalculationContext
            {
                Player = new PlayerSessionData(),
                IsEdgePiece = false,
                IsPuzzleComplete = true
            };

            var result = scoreCalculator.calculatePointsForPlacement(context);

            Assert.Equal(60, result.Points);
        }

        [Fact]
        public void CalculatePointsForPlacement_MultipleBonuses_Stacks()
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
        }

        [Fact]
        public void CalculatePenaltyPoints_ValidDifficulty_ReturnsCorrectValue()
        {
            int penalty = scoreCalculator.calculatePenaltyPoints(2);
            Assert.Equal(10, penalty);
        }

        [Fact]
        public void CalculatePenaltyPoints_ZeroDifficulty_ReturnsMinimum()
        {
            int penalty = scoreCalculator.calculatePenaltyPoints(0);
            Assert.Equal(5, penalty);
        }

        [Fact]
        public void CalculatePenaltyPoints_HighDifficulty_ScalesLinearly()
        {
            int penalty = scoreCalculator.calculatePenaltyPoints(10);
            Assert.Equal(50, penalty);
        }
    }
}
