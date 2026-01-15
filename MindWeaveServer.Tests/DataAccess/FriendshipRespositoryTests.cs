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
        public async Task GetAcceptedFriendshipsAsync_ValidPlayerId_ReturnsAccepted()
        {
            var res = await repository.getAcceptedFriendshipsAsync(1);
            Assert.Equal(1, res[0].friendships_id);
        }

        [Fact]
        public async Task GetPendingFriendRequestsAsync_ValidPlayerId_ReturnsIncoming()
        {
            var res = await repository.getPendingFriendRequestsAsync(1);
            Assert.Equal(2, res[0].friendships_id);
        }

        [Fact]
        public async Task GetPendingFriendRequestsAsync_NoRequests_ReturnsEmpty()
        {
            var res = await repository.getPendingFriendRequestsAsync(2);
            Assert.Empty(res);
        }

        [Fact]
        public async Task FindFriendshipAsync_DirectMatch_ReturnsMatch()
        {
            var res = await repository.findFriendshipAsync(1, 2);
            Assert.NotNull(res);
        }

        [Fact]
        public async Task FindFriendshipAsync_ReverseMatch_ReturnsMatch()
        {
            var res = await repository.findFriendshipAsync(2, 1);
            Assert.NotNull(res);
        }

        [Fact]
        public async Task FindFriendshipAsync_NoMatch_ReturnsNull()
        {
            var res = await repository.findFriendshipAsync(1, 99);
            Assert.Null(res);
        }

        [Fact]
        public void AddFriendship_ValidFriendship_CallsAdd()
        {
            var f = new Friendships { requester_id = 4, addressee_id = 5 };
            repository.addFriendship(f);

            contextMock.Verify(c => c.Friendships.Add(f), Times.Once);
        }

        [Fact]
        public void AddFriendship_NullFriendship_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => repository.addFriendship(null));
        }


        [Fact]
        public void UpdateFriendship_NullFriendship_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => repository.updateFriendship(null));
        }

        [Fact]
        public void RemoveFriendship_NullFriendship_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => repository.removeFriendship(null));
        }

        [Fact]
        public void Constructor_NullFactory_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => new FriendshipRepository(null));
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
