using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using Moq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Shared;
using Xunit;

namespace MindWeaveServer.Tests.BusinessLogic
{
    public class PuzzleLogicTests : IDisposable
    {
        private readonly Mock<IPuzzleRepository> puzzleRepositoryMock;
        private readonly Mock<IPlayerRepository> playerRepositoryMock;
        private readonly PuzzleLogic puzzleLogic;
        private readonly string testUploadPath;
        private readonly string testDefaultPath;

        public PuzzleLogicTests()
        {
            puzzleRepositoryMock = new Mock<IPuzzleRepository>();
            playerRepositoryMock = new Mock<IPlayerRepository>();

            puzzleLogic = new PuzzleLogic(puzzleRepositoryMock.Object, playerRepositoryMock.Object);

            testUploadPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UploadedPuzzles");
            testDefaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefaultPuzzles");

            if (!Directory.Exists(testUploadPath)) Directory.CreateDirectory(testUploadPath);
            if (!Directory.Exists(testDefaultPath)) Directory.CreateDirectory(testDefaultPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(testUploadPath)) Directory.Delete(testUploadPath, true);
            if (Directory.Exists(testDefaultPath)) Directory.Delete(testDefaultPath, true);
            GC.SuppressFinalize(this);
        }

        private static byte[] getValidImageBytes()
        {
            using (var bmp = new Bitmap(100, 100))
            using (var stream = new MemoryStream())
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.FillRectangle(Brushes.Red, 0, 0, 100, 100);
                }
                bmp.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        [Fact]
        public async Task getAvailablePuzzlesAsyncReturnsEmptyListWhenRepoIsEmpty()
        {
            puzzleRepositoryMock.Setup(x => x.getAvailablePuzzlesAsync())
                .ReturnsAsync(new List<Puzzles>());

            var result = await puzzleLogic.getAvailablePuzzlesAsync();

            Assert.Empty(result);
        }

        [Fact]
        public async Task getAvailablePuzzlesAsyncReturnsPopulatedDtos()
        {
            var puzzles = new List<Puzzles>
            {
                new Puzzles { puzzle_id = 1, image_path = "puzzleDefault_1.png" },
                new Puzzles { puzzle_id = 2, image_path = "custom_puzzle.png" }
            };

            puzzleRepositoryMock.Setup(x => x.getAvailablePuzzlesAsync())
                .ReturnsAsync(puzzles);

            string customFilePath = Path.Combine(testUploadPath, "custom_puzzle.png");
            File.WriteAllBytes(customFilePath, getValidImageBytes());

            var result = await puzzleLogic.getAvailablePuzzlesAsync();

            Assert.Equal(2, result.Count);
            Assert.Contains(result, p => p.PuzzleId == 1 && !p.IsUploaded);
            Assert.Contains(result, p => p.PuzzleId == 2 && p.IsUploaded);
        }

        [Fact]
        public async Task uploadPuzzleImageAsyncReturnsErrorWhenInputsAreInvalid()
        {
            var result = await puzzleLogic.uploadPuzzleImageAsync("user1", null, "file.png");

            Assert.False(result.Success);
            Assert.Equal(MessageCodes.PUZZLE_UPLOAD_FAILED, result.MessageCode);
        }

        [Fact]
        public async Task uploadPuzzleImageAsyncReturnsErrorWhenPlayerNotFound()
        {
            playerRepositoryMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>()))
                .ReturnsAsync((Player)null!);

            var result = await puzzleLogic.uploadPuzzleImageAsync("unknownUser", getValidImageBytes(), "file.png");

            Assert.False(result.Success);
            Assert.Equal(MessageCodes.AUTH_USER_NOT_FOUND, result.MessageCode);
        }

        [Fact]
        public async Task uploadPuzzleImageAsyncSavesFileAndReturnsSuccess()
        {
            var player = new Player { idPlayer = 10, username = "validUser" };
            playerRepositoryMock.Setup(x => x.getPlayerByUsernameAsync("validUser"))
                .ReturnsAsync(player);

            puzzleRepositoryMock.Setup(x => x.addPuzzle(It.IsAny<Puzzles>()))
                .Callback<Puzzles>(p => p.puzzle_id = 100);

            var result = await puzzleLogic.uploadPuzzleImageAsync("validUser", getValidImageBytes(), "image.jpg");

            Assert.True(result.Success);
            Assert.Equal(100, result.NewPuzzleId);
            Assert.NotEmpty(Directory.GetFiles(testUploadPath));
        }

        [Fact]
        public async Task getPuzzleDefinitionAsyncThrowsFileNotFoundWhenImageMissingOnDisk()
        {
            var puzzle = new Puzzles { puzzle_id = 1, image_path = "missing_file.png" };
            var difficulty = new DifficultyLevels { idDifficulty = 1, piece_count = 25 };
            puzzleRepositoryMock.Setup(x => x.getPuzzleByIdAsync(1))
                .ReturnsAsync(puzzle);
            puzzleRepositoryMock.Setup(x => x.getDifficultyByIdAsync(1))
                .ReturnsAsync(difficulty);

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                puzzleLogic.getPuzzleDefinitionAsync(1, 1));

            Assert.Contains("puzzle 1", exception.Message);
            Assert.Equal("missing_file.png", exception.FileName);
        }

        [Fact]
        public async Task getPuzzleDefinitionAsyncReturnsDefinitionWhenValid()
        {
            string fileName = "valid_puzzle.png";
            string fullPath = Path.Combine(testUploadPath, fileName);
            File.WriteAllBytes(fullPath, getValidImageBytes());

            var puzzle = new Puzzles { puzzle_id = 1, image_path = fileName };
            var difficulty = new DifficultyLevels { idDifficulty = 1, piece_count = 25 };

            puzzleRepositoryMock.Setup(x => x.getPuzzleByIdAsync(1)).ReturnsAsync(puzzle);
            puzzleRepositoryMock.Setup(x => x.getDifficultyByIdAsync(1)).ReturnsAsync(difficulty);

            var result = await puzzleLogic.getPuzzleDefinitionAsync(1, 1);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Pieces);
        }

        [Fact]
        public async Task getPuzzleDefinitionAsyncReturnsNullWhenPuzzleNotFoundInDb()
        {
            puzzleRepositoryMock.Setup(x => x.getPuzzleByIdAsync(99)).ReturnsAsync((Puzzles)null!);
            var result = await puzzleLogic.getPuzzleDefinitionAsync(99, 1);
            Assert.Null(result);
        }

        [Fact]
        public async Task getPuzzleDefinitionAsyncReturnsNullWhenDifficultyNotFound()
        {
            string fileName = "valid_puzzle_diff.png";
            string fullPath = Path.Combine(testUploadPath, fileName);
            File.WriteAllBytes(fullPath, getValidImageBytes());

            var puzzle = new Puzzles { puzzle_id = 1, image_path = fileName };
            puzzleRepositoryMock.Setup(x => x.getPuzzleByIdAsync(1)).ReturnsAsync(puzzle);
            puzzleRepositoryMock.Setup(x => x.getDifficultyByIdAsync(99)).ReturnsAsync((DifficultyLevels)null!);

            var result = await puzzleLogic.getPuzzleDefinitionAsync(1, 99);
            Assert.Null(result);
        }

        [Fact]
        public async Task uploadPuzzleImageAsyncSanitizesFileName()
        {
            var player = new Player { idPlayer = 1, username = "u" };
            playerRepositoryMock.Setup(x => x.getPlayerByUsernameAsync("u")).ReturnsAsync(player);

            string unsafeName = "image/with:invalid*chars.png";
            await puzzleLogic.uploadPuzzleImageAsync("u", getValidImageBytes(), unsafeName);

            string[] files = Directory.GetFiles(testUploadPath);
            Assert.Single(files);
            Assert.DoesNotContain(":", Path.GetFileName(files[0]));
        }

        [Fact]
        public async Task uploadPuzzleImageAsyncHandlesEmptyFileName()
        {
            var result = await puzzleLogic.uploadPuzzleImageAsync("u", getValidImageBytes(), "");
            Assert.False(result.Success);
        }
    }
}