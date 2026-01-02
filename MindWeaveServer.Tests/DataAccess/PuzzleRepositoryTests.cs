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
    public class PuzzleRepositoryTests
    {
        private readonly Mock<MindWeaveDBEntities1> contextMock;
        private readonly PuzzleRepository repository;
        private readonly List<Puzzles> puzzlesData;
        private readonly List<DifficultyLevels> diffData;

        public PuzzleRepositoryTests()
        {
            puzzlesData = new List<Puzzles>
            {
                new Puzzles { puzzle_id = 10, image_path = "b.png" },
                new Puzzles { puzzle_id = 5, image_path = "a.png" }
            };

            diffData = new List<DifficultyLevels>
            {
                new DifficultyLevels { idDifficulty = 1, piece_count = 50 }
            };

            var pSet = SetupMockDbSet(puzzlesData);
            pSet.Setup(m => m.Add(It.IsAny<Puzzles>())).Callback<Puzzles>(p => puzzlesData.Add(p));

            var dSet = SetupMockDbSet(diffData);
            dSet.Setup(m => m.FindAsync(It.IsAny<object[]>()))
                .Returns<object[]>(ids => Task.FromResult(diffData.FirstOrDefault(d => d.idDifficulty == (int)ids[0])));

            contextMock = new Mock<MindWeaveDBEntities1>();
            contextMock.Setup(c => c.Puzzles).Returns(pSet.Object);
            contextMock.Setup(c => c.DifficultyLevels).Returns(dSet.Object);

            repository = new PuzzleRepository(() => contextMock.Object);
        }

        [Fact]
        public async Task getAvailablePuzzlesAsyncReturnsAllOrderedById()
        {
            var res = await repository.getAvailablePuzzlesAsync();
            Assert.Equal(2, res.Count);
            Assert.Equal(5, res[0].puzzle_id);
            Assert.Equal(10, res[1].puzzle_id);
        }

        [Fact]
        public void addPuzzleAddsAndSaves()
        {
            var p = new Puzzles { puzzle_id = 99 };
            repository.addPuzzle(p);
            contextMock.Verify(c => c.Puzzles.Add(p), Times.Once);
            contextMock.Verify(c => c.SaveChanges(), Times.Once);
        }

        [Fact]
        public void addPuzzleThrowsIfNull()
        {
            Assert.Throws<ArgumentNullException>(() => repository.addPuzzle(null));
        }

        [Fact]
        public async Task getPuzzleByIdAsyncReturnsCorrectPuzzle()
        {
            var p = await repository.getPuzzleByIdAsync(10);
            Assert.NotNull(p);
            Assert.Equal("b.png", p.image_path);
        }

        [Fact]
        public async Task getPuzzleByIdAsyncReturnsNullIfMissing()
        {
            var p = await repository.getPuzzleByIdAsync(999);
            Assert.Null(p);
        }

        [Fact]
        public async Task getDifficultyByIdAsyncReturnsDifficulty()
        {
            var d = await repository.getDifficultyByIdAsync(1);
            Assert.NotNull(d);
            Assert.Equal(50, d.piece_count);
        }

        [Fact]
        public async Task getDifficultyByIdAsyncReturnsNullIfMissing()
        {
            var d = await repository.getDifficultyByIdAsync(99);
            Assert.Null(d);
        }

        [Fact]
        public void constructorThrowsIfFactoryNull()
        {
            Assert.Throws<ArgumentNullException>(() => new PuzzleRepository(null));
        }

        [Fact]
        public async Task saveChangesAsyncReturnsZero()
        {
            Assert.Equal(0, await repository.saveChangesAsync());
        }

        [Fact]
        public async Task getAvailablePuzzlesAsyncReturnsEmptyIfNone()
        {
            puzzlesData.Clear();
            var res = await repository.getAvailablePuzzlesAsync();
            Assert.Empty(res);
        }

        [Fact]
        public async Task getPuzzleByIdHandlesZero()
        {
            var p = await repository.getPuzzleByIdAsync(0);
            Assert.Null(p);
        }

        private Mock<DbSet<T>> SetupMockDbSet<T>(List<T> sourceList) where T : class
        {
            var mock = new Mock<DbSet<T>>();
            var queryable = sourceList.AsQueryable();
            mock.As<IDbAsyncEnumerable<T>>().Setup(m => m.GetAsyncEnumerator()).Returns(new TestDbAsyncEnumerator<T>(sourceList.GetEnumerator()));
            mock.As<IQueryable<T>>().Setup(m => m.Provider).Returns(new TestDbAsyncQueryProvider<T>(queryable.Provider));
            mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
            mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
            mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(sourceList.GetEnumerator());

            // BLINDAJE
            mock.Setup(m => m.AsNoTracking()).Returns(mock.Object);
            mock.Setup(m => m.Include(It.IsAny<string>())).Returns(mock.Object);

            return mock;
        }
    }
}