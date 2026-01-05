using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Tests.Utilities;
using MindWeaveServer.Utilities;
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
    public class FriendshipRepositoryTests
    {
        private readonly Mock<MindWeaveDBEntities1> contextMock;
        private readonly FriendshipRepository repository;
        private readonly List<Friendships> data;

        public FriendshipRepositoryTests()
        {
            data = new List<Friendships>
            {
                new Friendships { friendships_id = 1, requester_id = 1, addressee_id = 2, status_id = FriendshipStatusConstants.ACCEPTED },
                new Friendships { friendships_id = 2, requester_id = 3, addressee_id = 1, status_id = FriendshipStatusConstants.PENDING },
                new Friendships { friendships_id = 3, requester_id = 1, addressee_id = 4, status_id = FriendshipStatusConstants.PENDING }
            };

            var dbSetMock = SetupMockDbSet(data);
            dbSetMock.Setup(m => m.Add(It.IsAny<Friendships>())).Callback<Friendships>(f => data.Add(f));

            contextMock = new Mock<MindWeaveDBEntities1>();
            contextMock.Setup(c => c.Friendships).Returns(dbSetMock.Object);

            repository = new FriendshipRepository(() => contextMock.Object);
        }

        [Fact]
        public async Task getAcceptedFriendshipsAsyncReturnsOnlyAccepted()
        {
            var res = await repository.getAcceptedFriendshipsAsync(1);
            Assert.Single(res);
            Assert.Equal(1, res[0].friendships_id);
        }

        [Fact]
        public async Task getPendingFriendRequestsAsyncReturnsIncomingOnly()
        {
            var res = await repository.getPendingFriendRequestsAsync(1);
            Assert.Single(res);
            Assert.Equal(2, res[0].friendships_id);
        }

        [Fact]
        public async Task getPendingFriendRequestsAsyncReturnsEmptyIfNone()
        {
            var res = await repository.getPendingFriendRequestsAsync(2);
            Assert.Empty(res);
        }

        [Fact]
        public async Task findFriendshipAsyncReturnsMatchDirect()
        {
            var res = await repository.findFriendshipAsync(1, 2);
            Assert.NotNull(res);
        }

        [Fact]
        public async Task findFriendshipAsyncReturnsMatchReverse()
        {
            var res = await repository.findFriendshipAsync(2, 1);
            Assert.NotNull(res);
        }

        [Fact]
        public async Task findFriendshipAsyncReturnsNullIfNone()
        {
            var res = await repository.findFriendshipAsync(1, 99);
            Assert.Null(res);
        }

        [Fact]
        public void addFriendshipAddsAndSaves()
        {
            var f = new Friendships { requester_id = 4, addressee_id = 5 };
            repository.addFriendship(f);

            contextMock.Verify(c => c.Friendships.Add(f), Times.Once);
            contextMock.Verify(c => c.SaveChanges(), Times.Once);
            Assert.Contains(f, data);
        }

        [Fact]
        public void addFriendshipThrowsIfNull()
        {
            Assert.Throws<ArgumentNullException>(() => repository.addFriendship(null));
        }

        [Fact]
        public void updateFriendshipSavesChanges()
        {
            var f = data.First();
            repository.updateFriendship(f);
            contextMock.Verify(c => c.SaveChanges(), Times.Once);
        }

        [Fact]
        public void updateFriendshipThrowsIfNull()
        {
            Assert.Throws<ArgumentNullException>(() => repository.updateFriendship(null));
        }

        [Fact]
        public void removeFriendshipSavesChanges()
        {
            var f = data.First();
            repository.removeFriendship(f);
            contextMock.Verify(c => c.SaveChanges(), Times.Once);
        }

        [Fact]
        public void removeFriendshipThrowsIfNull()
        {
            Assert.Throws<ArgumentNullException>(() => repository.removeFriendship(null));
        }

        [Fact]
        public void constructorThrowsIfFactoryNull()
        {
            Assert.Throws<ArgumentNullException>(() => new FriendshipRepository(null));
        }

        [Fact]
        public async Task saveChangesAsyncReturnsZero()
        {
            Assert.Equal(0, await repository.saveChangesAsync());
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

            // BLINDAJE CONTRA EXCEPCIONES EN INCLUDE/ASNOTRACKING
            mock.Setup(m => m.AsNoTracking()).Returns(mock.Object);
            mock.Setup(m => m.Include(It.IsAny<string>())).Returns(mock.Object);

            return mock;
        }
    }
}