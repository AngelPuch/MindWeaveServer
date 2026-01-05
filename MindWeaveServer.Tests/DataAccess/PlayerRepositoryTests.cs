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
    public class PlayerRepositoryTests
    {
        private readonly Mock<MindWeaveDBEntities1> contextMock;
        private readonly PlayerRepository repository;
        private readonly List<Player> data;

        public PlayerRepositoryTests()
        {
            data = new List<Player>
            {
                new Player { idPlayer = 1, username = "UserA", email = "a@test.com", password_hash = "hash1" },
                new Player { idPlayer = 2, username = "UserB", email = "b@test.com", password_hash = "hash2" }
            };

            var dbSetMock = SetupMockDbSet(data);
            dbSetMock.Setup(m => m.Add(It.IsAny<Player>())).Callback<Player>(p => data.Add(p));

            contextMock = new Mock<MindWeaveDBEntities1>();
            contextMock.Setup(c => c.Player).Returns(dbSetMock.Object);

            var friendsMock = SetupMockDbSet(new List<Friendships>());
            contextMock.Setup(c => c.Friendships).Returns(friendsMock.Object);

            repository = new PlayerRepository(() => contextMock.Object);
        }

        [Fact]
        public async Task getPlayerByEmailAsyncReturnsPlayerIfFound()
        {
            var result = await repository.getPlayerByEmailAsync("a@test.com");
            Assert.NotNull(result);
            Assert.Equal("UserA", result.username);
        }

        [Fact]
        public async Task getPlayerByEmailAsyncReturnsNullIfNotFound()
        {
            var result = await repository.getPlayerByEmailAsync("z@test.com");
            Assert.Null(result);
        }

        [Fact]
        public async Task getPlayerByEmailAsyncReturnsNullIfInputEmpty()
        {
            var result = await repository.getPlayerByEmailAsync("");
            Assert.Null(result);
        }

        [Fact]
        public void addPlayerAddsToContextAndSaves()
        {
            var newPlayer = new Player { username = "New", email = "new@test.com" };
            repository.addPlayer(newPlayer);

            contextMock.Verify(c => c.Player.Add(newPlayer), Times.Once);
            contextMock.Verify(c => c.SaveChanges(), Times.Once);
        }

        [Fact]
        public void addPlayerThrowsIfNull()
        {
            Assert.Throws<ArgumentNullException>(() => repository.addPlayer(null));
        }

        [Fact]
        public async Task updatePlayerAsyncSetsStateModifiedAndSaves()
        {
            var player = data.First();
            await repository.updatePlayerAsync(player);
            contextMock.Verify(c => c.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task updatePlayerAsyncThrowsIfNull()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => repository.updatePlayerAsync(null));
        }

        [Fact]
        public async Task getPlayerByUsernameAsyncReturnsPlayerIgnoringCase()
        {
            var result = await repository.getPlayerByUsernameAsync("USERA");
            Assert.NotNull(result);
            Assert.Equal(1, result.idPlayer);
        }

        [Fact]
        public async Task getPlayerByUsernameAsyncReturnsNullIfEmpty()
        {
            var result = await repository.getPlayerByUsernameAsync(" ");
            Assert.Null(result);
        }

        [Fact]
        public async Task getPlayerWithProfileViewDataAsyncReturnsNullIfUserMissing()
        {
            var result = await repository.getPlayerWithProfileViewDataAsync("Ghost");
            Assert.Null(result);
        }

        [Fact]
        public async Task getPlayerWithProfileViewDataAsyncReturnsPlayer()
        {
            var result = await repository.getPlayerWithProfileViewDataAsync("UserB");
            Assert.NotNull(result);
            Assert.Equal(2, result.idPlayer);
        }

        [Fact]
        public async Task searchPlayersAsyncReturnsMatches()
        {
            var results = await repository.searchPlayersAsync(99, "User");
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public async Task searchPlayersAsyncExcludesSelf()
        {
            var results = await repository.searchPlayersAsync(1, "User");
            Assert.Single(results);
            Assert.Equal("UserB", results[0].Username);
        }

        [Fact]
        public async Task getPlayerByUsernameWithTrackingAsyncReturnsResult()
        {
            var p = await repository.getPlayerByUsernameWithTrackingAsync("UserA");
            Assert.NotNull(p);
        }

        [Fact]
        public async Task saveChangesAsyncReturnsZero()
        {
            var res = await repository.saveChangesAsync();
            Assert.Equal(0, res);
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