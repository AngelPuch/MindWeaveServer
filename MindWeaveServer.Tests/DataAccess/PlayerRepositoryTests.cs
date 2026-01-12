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
        public async Task GetPlayerByEmailAsync_ValidEmail_ReturnsPlayer()
        {
            var result = await repository.getPlayerByEmailAsync("a@test.com");
            Assert.Equal("UserA", result.username);
        }

        [Fact]
        public async Task GetPlayerByEmailAsync_InvalidEmail_ReturnsNull()
        {
            var result = await repository.getPlayerByEmailAsync("z@test.com");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetPlayerByEmailAsync_EmptyEmail_ReturnsNull()
        {
            var result = await repository.getPlayerByEmailAsync("");
            Assert.Null(result);
        }


        [Fact]
        public void AddPlayer_ValidPlayer_CallsAdd()
        {
            var newPlayer = new Player { username = "New", email = "new@test.com" };
            repository.addPlayer(newPlayer);

            contextMock.Verify(c => c.Player.Add(newPlayer), Times.Once);
        }

        [Fact]
        public void AddPlayer_ValidPlayer_CallsSaveChanges()
        {
            var newPlayer = new Player { username = "New", email = "new@test.com" };
            repository.addPlayer(newPlayer);

            contextMock.Verify(c => c.SaveChanges(), Times.Once);
        }

        [Fact]
        public void AddPlayer_NullPlayer_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => repository.addPlayer(null));
        }


        [Fact]
        public async Task UpdatePlayerAsync_NullPlayer_ThrowsException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => repository.updatePlayerAsync(null));
        }

        [Fact]
        public async Task GetPlayerByUsernameAsync_ValidUsername_ReturnsPlayer()
        {
            var result = await repository.getPlayerByUsernameAsync("USERA");
            Assert.Equal(1, result.idPlayer);
        }

        [Fact]
        public async Task GetPlayerByUsernameAsync_EmptyUsername_ReturnsNull()
        {
            var result = await repository.getPlayerByUsernameAsync(" ");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetPlayerWithProfileViewDataAsync_InvalidUser_ReturnsNull()
        {
            var result = await repository.getPlayerWithProfileViewDataAsync("Ghost");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetPlayerWithProfileViewDataAsync_ValidUser_ReturnsPlayer()
        {
            var result = await repository.getPlayerWithProfileViewDataAsync("UserB");
            Assert.Equal(2, result.idPlayer);
        }

        [Fact]
        public async Task SearchPlayersAsync_ValidQuery_ReturnsMatches()
        {
            var results = await repository.searchPlayersAsync(99, "User");
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public async Task SearchPlayersAsync_ExcludeSelf_FiltersOutSelf()
        {
            var results = await repository.searchPlayersAsync(1, "User");
            Assert.Single(results, r => r.Username == "UserB");
        }

        [Fact]
        public async Task GetPlayerByUsernameWithTrackingAsync_ValidUsername_ReturnsPlayer()
        {
            var p = await repository.getPlayerByUsernameWithTrackingAsync("UserA");
            Assert.NotNull(p);
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
