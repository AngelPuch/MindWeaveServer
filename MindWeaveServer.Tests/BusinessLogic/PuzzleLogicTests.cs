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
        public async Task GetAvailablePuzzlesAsync_EmptyRepo_ReturnsEmptyList()
        {
            puzzleRepositoryMock.Setup(x => x.getAvailablePuzzlesAsync())
                .ReturnsAsync(new List<Puzzles>());

            var result = await puzzleLogic.getAvailablePuzzlesAsync();

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetAvailablePuzzlesAsync_MultiplePuzzles_ReturnsCorrectCount()
        {
            var puzzles = new List<Puzzles>
    {
        new Puzzles { puzzle_id = 1, image_path = "puzzleDefault_1.png" },
        new Puzzles { puzzle_id = 2, image_path = "custom_puzzle.png" }
    };

            puzzleRepositoryMock.Setup(x => x.getAvailablePuzzlesAsync())
                .ReturnsAsync(puzzles);

            var result = await puzzleLogic.getAvailablePuzzlesAsync();

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task GetAvailablePuzzlesAsync_DefaultPuzzle_MarkedAsNotUploaded()
        {
            var puzzles = new List<Puzzles>
    {
        new Puzzles { puzzle_id = 1, image_path = "puzzleDefault_1.png" }
    };
            puzzleRepositoryMock.Setup(x => x.getAvailablePuzzlesAsync()).ReturnsAsync(puzzles);

            var result = await puzzleLogic.getAvailablePuzzlesAsync();

            Assert.Contains(result, p => p.PuzzleId == 1 && !p.IsUploaded);
        }

        [Fact]
        public async Task GetAvailablePuzzlesAsync_CustomPuzzle_MarkedAsUploaded()
        {
            var puzzles = new List<Puzzles>
    {
        new Puzzles { puzzle_id = 2, image_path = "custom_puzzle.png" }
    };
            puzzleRepositoryMock.Setup(x => x.getAvailablePuzzlesAsync()).ReturnsAsync(puzzles);

            string customFilePath = Path.Combine(testUploadPath, "custom_puzzle.png");
            File.WriteAllBytes(customFilePath, getValidImageBytes());

            var result = await puzzleLogic.getAvailablePuzzlesAsync();

            Assert.Contains(result, p => p.PuzzleId == 2 && p.IsUploaded);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_NullInput_ReturnsFailure()
        {
            var result = await puzzleLogic.uploadPuzzleImageAsync("user1", null, "file.png");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_NullInput_ReturnsUploadFailedCode()
        {
            var result = await puzzleLogic.uploadPuzzleImageAsync("user1", null, "file.png");

            Assert.Equal(MessageCodes.PUZZLE_UPLOAD_FAILED, result.MessageCode);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_PlayerNotFound_ReturnsFailure()
        {
            playerRepositoryMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>()))
                .ReturnsAsync((Player)null!);

            var result = await puzzleLogic.uploadPuzzleImageAsync("unknownUser", getValidImageBytes(), "file.png");

            Assert.False(result.Success);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_PlayerNotFound_ReturnsUserNotFoundCode()
        {
            playerRepositoryMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>()))
                .ReturnsAsync((Player)null!);

            var result = await puzzleLogic.uploadPuzzleImageAsync("unknownUser", getValidImageBytes(), "file.png");

            Assert.Equal(MessageCodes.AUTH_USER_NOT_FOUND, result.MessageCode);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_ValidData_ReturnsSuccess()
        {
            var player = new Player { idPlayer = 10, username = "validUser" };
            playerRepositoryMock.Setup(x => x.getPlayerByUsernameAsync("validUser")).ReturnsAsync(player);

            var result = await puzzleLogic.uploadPuzzleImageAsync("validUser", getValidImageBytes(), "image.jpg");

            Assert.True(result.Success);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_ValidData_ReturnsPuzzleId()
        {
            var player = new Player { idPlayer = 10, username = "validUser" };
            playerRepositoryMock.Setup(x => x.getPlayerByUsernameAsync("validUser")).ReturnsAsync(player);

            puzzleRepositoryMock.Setup(x => x.addPuzzle(It.IsAny<Puzzles>()))
                .Callback<Puzzles>(p => p.puzzle_id = 100);

            var result = await puzzleLogic.uploadPuzzleImageAsync("validUser", getValidImageBytes(), "image.jpg");

            Assert.Equal(100, result.NewPuzzleId);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_ValidData_CreatesFile()
        {
            var player = new Player { idPlayer = 10, username = "validUser" };
            playerRepositoryMock.Setup(x => x.getPlayerByUsernameAsync("validUser")).ReturnsAsync(player);

            await puzzleLogic.uploadPuzzleImageAsync("validUser", getValidImageBytes(), "image.jpg");

            Assert.NotEmpty(Directory.GetFiles(testUploadPath));
        }

        [Fact]
        public async Task GetPuzzleDefinitionAsync_MissingImage_ThrowsException()
        {
            var puzzle = new Puzzles { puzzle_id = 1, image_path = "missing_file.png" };
            var difficulty = new DifficultyLevels { idDifficulty = 1, piece_count = 25 };
            puzzleRepositoryMock.Setup(x => x.getPuzzleByIdAsync(1))
                .ReturnsAsync(puzzle);
            puzzleRepositoryMock.Setup(x => x.getDifficultyByIdAsync(1))
                .ReturnsAsync(difficulty);

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                puzzleLogic.getPuzzleDefinitionAsync(1, 1));

            Assert.Contains("PuzzleId: 1", exception.Message);
        }

        [Fact]
        public async Task GetPuzzleDefinitionAsync_ValidData_ReturnsDefinition()
        {
            string fileName = "valid_puzzle.png";
            string fullPath = Path.Combine(testUploadPath, fileName);
            File.WriteAllBytes(fullPath, getValidImageBytes());

            var puzzle = new Puzzles { puzzle_id = 1, image_path = fileName };
            var difficulty = new DifficultyLevels { idDifficulty = 1, piece_count = 25 };

            puzzleRepositoryMock.Setup(x => x.getPuzzleByIdAsync(1)).ReturnsAsync(puzzle);
            puzzleRepositoryMock.Setup(x => x.getDifficultyByIdAsync(1)).ReturnsAsync(difficulty);

            var result = await puzzleLogic.getPuzzleDefinitionAsync(1, 1);

            Assert.NotEmpty(result.Pieces);
        }

        [Fact]
        public async Task GetPuzzleDefinitionAsync_PuzzleNotFound_ReturnsNull()
        {
            puzzleRepositoryMock.Setup(x => x.getPuzzleByIdAsync(99)).ReturnsAsync((Puzzles)null!);
            var result = await puzzleLogic.getPuzzleDefinitionAsync(99, 1);
            Assert.Null(result);
        }

        [Fact]
        public async Task GetPuzzleDefinitionAsync_DifficultyNotFound_ReturnsNull()
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
        public async Task UploadPuzzleImageAsync_UnsafeName_CreatesSingleFile()
        {
            var player = new Player { idPlayer = 1, username = "u" };
            playerRepositoryMock.Setup(x => x.getPlayerByUsernameAsync("u")).ReturnsAsync(player);

            string unsafeName = "image/with:invalid*chars.png";

            await puzzleLogic.uploadPuzzleImageAsync("u", getValidImageBytes(), unsafeName);

            Assert.Single(Directory.GetFiles(testUploadPath));
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_UnsafeName_RemovesInvalidChars()
        {
            var player = new Player { idPlayer = 1, username = "u" };
            playerRepositoryMock.Setup(x => x.getPlayerByUsernameAsync("u")).ReturnsAsync(player);

            string unsafeName = "image:invalid*chars.png";

            await puzzleLogic.uploadPuzzleImageAsync("u", getValidImageBytes(), unsafeName);

            string createdFilePath = Directory.GetFiles(testUploadPath)[0];
            string createdFileName = Path.GetFileName(createdFilePath);

            Assert.DoesNotContain(":", createdFileName);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_EmptyFileName_ReturnsFailure()
        {
            var result = await puzzleLogic.uploadPuzzleImageAsync("u", getValidImageBytes(), "");
            Assert.False(result.Success);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_EmptyFileName_ReturnsUploadFailedCode()
        {
            var result = await puzzleLogic.uploadPuzzleImageAsync("u", getValidImageBytes(), "");

            Assert.Equal(MessageCodes.PUZZLE_UPLOAD_FAILED, result.MessageCode);
        }
    }
}
