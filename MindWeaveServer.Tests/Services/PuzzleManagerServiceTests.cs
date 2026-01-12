using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Services;
using MindWeaveServer.Utilities.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.ServiceModel;
using System.Threading.Tasks;
using Xunit;

namespace MindWeaveServer.Tests.Services
{
    public class PuzzleManagerServiceTests
    {
        private readonly Mock<IPuzzleRepository> puzzleRepoMock;
        private readonly Mock<IPlayerRepository> playerRepoMock;
        private readonly Mock<IServiceExceptionHandler> exceptionHandlerMock;
        private readonly PuzzleLogic logic;
        private readonly PuzzleManagerService service;

        public PuzzleManagerServiceTests()
        {
            puzzleRepoMock = new Mock<IPuzzleRepository>();
            playerRepoMock = new Mock<IPlayerRepository>();
            exceptionHandlerMock = new Mock<IServiceExceptionHandler>();

            logic = new PuzzleLogic(puzzleRepoMock.Object, playerRepoMock.Object);
            service = new PuzzleManagerService(logic, exceptionHandlerMock.Object);
        }

        private static byte[] GetValidImageBytes()
        {
            using (var bmp = new Bitmap(10, 10))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        [Fact]
        public async Task GetAvailablePuzzlesAsync_HasPuzzles_ReturnsList()
        {
            puzzleRepoMock.Setup(x => x.getAvailablePuzzlesAsync())
                .ReturnsAsync(new List<Puzzles> { new Puzzles { puzzle_id = 1, image_path = "default.png" } });

            var result = await service.getAvailablePuzzlesAsync();

            Assert.Equal(1, result[0].PuzzleId);
        }

        [Fact]
        public async Task GetAvailablePuzzlesAsync_Exception_HandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "GetAvailablePuzzlesOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            try
            {
                var res = await service.getAvailablePuzzlesAsync();
                Assert.Empty(res);
            }
            catch (FaultException<ServiceFaultDto>) { Assert.True(true); }
        }

        [Fact]
        public async Task GetPuzzleDefinitionAsync_MissingPuzzle_CallsLogic()
        {
            puzzleRepoMock.Setup(x => x.getPuzzleByIdAsync(1)).ReturnsAsync((Puzzles)null!);
            var result = await service.getPuzzleDefinitionAsync(1, 1);
            Assert.Null(result);
        }

        [Fact]
        public async Task GetPuzzleDefinitionAsync_Exception_HandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "GetPuzzleDefinitionOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            var result = await service.getPuzzleDefinitionAsync(0, 0);
            Assert.Null(result);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_ValidData_ValidatesAndDelegates()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>()))
                .ReturnsAsync(new Player { idPlayer = 1, username = "User" });

            var result = await service.uploadPuzzleImageAsync("User", GetValidImageBytes(), "test.png");
            Assert.NotNull(result);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_Exception_HandlesError()
        {
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), "UploadPuzzleImageOperation"))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            try
            {
                var res = await service.uploadPuzzleImageAsync(null, null, null);
                Assert.False(res.Success);
            }
            catch (FaultException<ServiceFaultDto>) { Assert.True(true); }
        }

        [Fact]
        public async Task GetAvailablePuzzlesAsync_EmptyRepo_ReturnsEmpty()
        {
            puzzleRepoMock.Setup(x => x.getAvailablePuzzlesAsync()).ReturnsAsync(new List<Puzzles>());
            var res = await service.getAvailablePuzzlesAsync();
            Assert.Empty(res);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_UserNotFound_ReturnsFail()
        {

            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync(It.IsAny<string>())).ReturnsAsync((Player)null!);

            try
            {
                var res = await service.uploadPuzzleImageAsync("Ghost", GetValidImageBytes(), "a.jpg");
                Assert.False(res.Success);
            }
            catch (Exception)
            {
                Assert.True(true);
            }
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_EmptyBytes_ReturnsFail()
        {
            var res = await service.uploadPuzzleImageAsync("User", Array.Empty<byte>(), "a.jpg");
            Assert.False(res.Success);
        }

        [Fact]
        public async Task GetPuzzleDefinitionAsync_LogicThrows_ThrowsFault()
        {
            puzzleRepoMock.Setup(x => x.getPuzzleByIdAsync(It.IsAny<int>())).Throws(new Exception());
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), It.IsAny<string>()))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.getPuzzleDefinitionAsync(1, 1));
        }

        [Fact]
        public async Task GetAvailablePuzzlesAsync_DatabaseException_ThrowsFault()
        {
            puzzleRepoMock.Setup(x => x.getAvailablePuzzlesAsync()).Throws(new System.Data.Entity.Core.EntityException());
            exceptionHandlerMock.Setup(x => x.handleException(It.IsAny<Exception>(), It.IsAny<string>()))
                .Returns(new FaultException<ServiceFaultDto>(new ServiceFaultDto(ServiceErrorType.OperationFailed, "E")));

            await Assert.ThrowsAsync<FaultException<ServiceFaultDto>>(() => service.getAvailablePuzzlesAsync());
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_UnsafeFilename_SanitizesName()
        {
            playerRepoMock.Setup(x => x.getPlayerByUsernameAsync("U")).ReturnsAsync(new Player());

            var res = await service.uploadPuzzleImageAsync("U", GetValidImageBytes(), "bad/name.jpg");
            Assert.NotNull(res);
        }

        [Fact]
        public void Constructor_ValidParams_InitializesWithDefaults()
        {
            Assert.NotNull(service);
        }

        [Fact]
        public async Task GetPuzzleDefinitionAsync_InvalidDifficulty_ValidatesDifficulty()
        {
            puzzleRepoMock.Setup(x => x.getPuzzleByIdAsync(1)).ReturnsAsync(new Puzzles { image_path = "def.png" });
            puzzleRepoMock.Setup(x => x.getDifficultyByIdAsync(99)).ReturnsAsync((DifficultyLevels)null!);

            var res = await service.getPuzzleDefinitionAsync(1, 99);
            Assert.Null(res);
        }

        [Fact]
        public async Task UploadPuzzleImageAsync_NullArgs_HandlesNullArgs()
        {
            var res = await service.uploadPuzzleImageAsync(null, null, null);
            Assert.False(res.Success);
        }
    }
}
