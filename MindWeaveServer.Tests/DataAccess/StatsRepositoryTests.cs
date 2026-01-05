using MindWeaveServer.Contracts.DataContracts.Stats;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Tests.Utilities;
using Moq;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MindWeaveServer.Tests.DataAccess
{
    public class StatsRepositoryTests
    {
        private readonly Mock<MindWeaveDBEntities1> contextMock;
        private readonly StatsRepository repository;
        private readonly List<PlayerStats> statsData;
        private readonly List<Player> playerData;
        private readonly List<Achievements> achData;

        public StatsRepositoryTests()
        {
            statsData = new List<PlayerStats>
            {
                new PlayerStats { player_id = 1, puzzles_completed = 10, highest_score = 100 }
            };

            playerData = new List<Player>
            {
                new Player { idPlayer = 1, Achievements = new List<Achievements>() { new Achievements { achievements_id = 5 } } },
                new Player { idPlayer = 2, Achievements = new List<Achievements>() }
            };

            achData = new List<Achievements>
            {
                new Achievements { achievements_id = 5 },
                new Achievements { achievements_id = 6 }
            };

            var sSet = SetupMockDbSet(statsData);
            sSet.Setup(m => m.Add(It.IsAny<PlayerStats>())).Callback<PlayerStats>(s => statsData.Add(s));

            var pSet = SetupMockDbSet(playerData);
            var aSet = SetupMockDbSet(achData);
            aSet.Setup(m => m.FindAsync(It.IsAny<object[]>()))
    .Returns<object[]>(ids => Task.FromResult(achData.FirstOrDefault(a => a.achievements_id == (int)ids[0])!));

            contextMock = new Mock<MindWeaveDBEntities1>();
            contextMock.Setup(c => c.PlayerStats).Returns(sSet.Object);
            contextMock.Setup(c => c.Player).Returns(pSet.Object);
            contextMock.Setup(c => c.Achievements).Returns(aSet.Object);

            repository = new StatsRepository(() => contextMock.Object);
        }

        [Fact]
        public async Task getPlayerStatsByIdAsyncReturnsStats()
        {
            var s = await repository.getPlayerStatsByIdAsync(1);
            Assert.NotNull(s);
            Assert.Equal(100, s.highest_score);
        }

        [Fact]
        public async Task updatePlayerStatsAsyncUpdatesExisting()
        {
            var match = new PlayerMatchStatsDto { PlayerId = 1, Score = 200, IsWin = true, PlaytimeMinutes = 5 };
            await repository.updatePlayerStatsAsync(match);

            var s = statsData.First(x => x.player_id == 1);
            Assert.Equal(200, s.highest_score);
            Assert.Equal(11, s.puzzles_completed);
            contextMock.Verify(c => c.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task updatePlayerStatsAsyncCreatesNewIfMissing()
        {
            var match = new PlayerMatchStatsDto { PlayerId = 3, Score = 50, IsWin = false, PlaytimeMinutes = 10 };
            await repository.updatePlayerStatsAsync(match);

            var s = statsData.FirstOrDefault(x => x.player_id == 3);
            Assert.NotNull(s);
            Assert.Equal(50, s.highest_score);
            contextMock.Verify(c => c.PlayerStats.Add(It.IsAny<PlayerStats>()), Times.Once);
        }

        [Fact]
        public async Task updatePlayerStatsAsyncThrowsIfNull()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => repository.updatePlayerStatsAsync(null));
        }

        [Fact]
        public async Task getPlayerAchievementIdsAsyncReturnsList()
        {
            var ids = await repository.getPlayerAchievementIdsAsync(1);
            Assert.Single(ids);
            Assert.Equal(5, ids[0]);
        }

        [Fact]
        public async Task getPlayerAchievementIdsAsyncReturnsEmptyIfNone()
        {
            var ids = await repository.getPlayerAchievementIdsAsync(2);
            Assert.Empty(ids);
        }

        [Fact]
        public async Task unlockAchievementAsyncAddsAchievement()
        {
            await repository.unlockAchievementAsync(1, 6);

            var p = playerData.First(x => x.idPlayer == 1);
            Assert.Equal(2, p.Achievements.Count);
            contextMock.Verify(c => c.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task unlockAchievementAsyncIgnoresIfAlreadyUnlocked()
        {
            await repository.unlockAchievementAsync(1, 5);
            contextMock.Verify(c => c.SaveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task getAllAchievementsAsyncReturnsAll()
        {
            var all = await repository.getAllAchievementsAsync();
            Assert.Equal(2, all.Count);
        }

        [Fact]
        public async Task unlockAchievementsAsyncBulkAdds()
        {
            var ids = new List<int> { 5, 6 };
            var unlocked = await repository.unlockAchievementsAsync(1, ids);

            Assert.Single(unlocked);
            Assert.Equal(6, unlocked[0]);
            contextMock.Verify(c => c.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task unlockAchievementsAsyncReturnsEmptyIfNull()
        {
            var res = await repository.unlockAchievementsAsync(1, null);
            Assert.Empty(res);
        }

        [Fact]
        public async Task addPlaytimeToPlayerAsyncIncrements()
        {
            await repository.addPlaytimeToPlayerAsync(1, 20);
            var s = statsData.First();
            Assert.Equal(20, s.total_playtime_minutes);
            contextMock.Verify(c => c.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task addPlaytimeToPlayerAsyncIgnoresMissing()
        {
            await repository.addPlaytimeToPlayerAsync(99, 10);
            contextMock.Verify(c => c.SaveChangesAsync(), Times.Never);
        }

        [Fact]
        public void constructorThrowsIfFactoryNull()
        {
            Assert.Throws<ArgumentNullException>(() => new StatsRepository(null));
        }

        private static Mock<DbSet<T>> SetupMockDbSet<T>(List<T> sourceList) where T : class
        {
            var mock = new Mock<DbSet<T>>();
            var queryable = sourceList.AsQueryable();
            mock.As<IDbAsyncEnumerable<T>>().Setup(m => m.GetAsyncEnumerator()).Returns(new TestDbAsyncEnumerator<T>(sourceList.GetEnumerator()));
            mock.As<IQueryable<T>>().Setup(m => m.Provider).Returns(new TestDbAsyncQueryProvider<T>(queryable.Provider));
            mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
            mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
            mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(sourceList.GetEnumerator());

            
            mock.Setup(m => m.AsNoTracking()).Returns(mock.Object);
            mock.Setup(m => m.Include(It.IsAny<string>())).Returns(mock.Object);

            return mock;
        }
    }
}