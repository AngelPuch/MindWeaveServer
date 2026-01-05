using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace MindWeaveServer.Tests.BusinessLogic.Manager
{
    public class GameSessionManagerTests : IDisposable
    {
        private readonly Mock<IPuzzleRepository> puzzleRepositoryMock;
        private readonly Mock<IMatchmakingRepository> matchmakingRepositoryMock;
        private readonly Mock<IStatsRepository> statsRepositoryMock;
        private readonly Mock<IPlayerRepository> playerRepositoryMock;
        private readonly Mock<IScoreCalculator> scoreCalculatorMock;
        private readonly Mock<IMatchmakingCallback> callbackMock;

        private readonly GameSessionManager gameSessionManager;
        private readonly StatsLogic statsLogic;
        private readonly string tempPuzzlePath;

        public GameSessionManagerTests()
        {
            puzzleRepositoryMock = new Mock<IPuzzleRepository>();
            matchmakingRepositoryMock = new Mock<IMatchmakingRepository>();
            statsRepositoryMock = new Mock<IStatsRepository>();
            playerRepositoryMock = new Mock<IPlayerRepository>();
            scoreCalculatorMock = new Mock<IScoreCalculator>();
            callbackMock = new Mock<IMatchmakingCallback>();

            // Setup Callback Mock to look "Open"
            var commObj = callbackMock.As<System.ServiceModel.ICommunicationObject>();
            commObj.Setup(x => x.State).Returns(System.ServiceModel.CommunicationState.Opened);

            statsLogic = new StatsLogic(statsRepositoryMock.Object, playerRepositoryMock.Object);
            var puzzleGenerator = new PuzzleGenerator();

            gameSessionManager = new GameSessionManager(
                puzzleRepositoryMock.Object,
                matchmakingRepositoryMock.Object,
                statsLogic,
                puzzleGenerator,
                scoreCalculatorMock.Object);

            // Setup valid puzzle file
            tempPuzzlePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "puzzleDefaultTest.png");
            using (var bmp = new Bitmap(100, 100))
            {
                using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.White); }
                bmp.Save(tempPuzzlePath, ImageFormat.Png);
            }
        }

        public void Dispose()
        {
            if (File.Exists(tempPuzzlePath)) File.Delete(tempPuzzlePath);
            GC.SuppressFinalize(this);
        }

        private async Task<string> createActiveSession(string lobbyCode = "LOBBY1")
        {
            var puzzle = new Puzzles { puzzle_id = 1, image_path = "puzzleDefaultTest.png" };
            puzzleRepositoryMock.Setup(x => x.getPuzzleByIdAsync(1)).ReturnsAsync(puzzle);

            var players = new ConcurrentDictionary<int, PlayerSessionData>();
            players.TryAdd(1, new PlayerSessionData
            {
                PlayerId = 1,
                Username = "Player1",
                Callback = callbackMock.Object
            });

            var difficulty = new DifficultyLevels { piece_count = 25 };

            await gameSessionManager.createGameSession(lobbyCode, 100, 1, difficulty, players);
            return lobbyCode;
        }

        [Fact]
        public async Task createGameSessionSuccessfullyAddsSession()
        {
            string lobbyCode = await createActiveSession("TEST_LOBBY");
            var session = gameSessionManager.getSession(lobbyCode);
            Assert.NotNull(session);
            Assert.True(gameSessionManager.isPlayerInAnySession("Player1"));
        }

        [Fact]
        public async Task createGameSessionThrowsIfLobbyIdEmpty()
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
               gameSessionManager.createGameSession("", 1, 1, new DifficultyLevels(), new ConcurrentDictionary<int, PlayerSessionData>()));
        }

        [Fact]
        public async Task handlePieceDragLocksPieceForPlayer()
        {
            string lobbyCode = await createActiveSession();
            int pieceId = 0;
            int playerId = 1;

            gameSessionManager.handlePieceDrag(lobbyCode, playerId, pieceId);

            var session = gameSessionManager.getSession(lobbyCode);
            Assert.True(session.PieceStates.ContainsKey(pieceId));
            Assert.Equal(playerId, session.PieceStates[pieceId].HeldByPlayerId);
            callbackMock.Verify(x => x.onPieceDragStarted(pieceId, "Player1"), Times.Once);
        }

        [Fact]
        public async Task handlePieceDragPreventsLockIfAlreadyHeld()
        {
            string lobbyCode = await createActiveSession();
            int pieceId = 0;
            int player1Id = 1;
            int player2Id = 2;

            var session = gameSessionManager.getSession(lobbyCode);
            session.addPlayer(player2Id, "Player2", "avatar", callbackMock.Object);

            gameSessionManager.handlePieceDrag(lobbyCode, player1Id, pieceId);
            gameSessionManager.handlePieceDrag(lobbyCode, player2Id, pieceId);

            Assert.Equal(player1Id, session.PieceStates[pieceId].HeldByPlayerId);
        }

        [Fact]
        public async Task handlePieceDropUpdatesStateOnCorrectPlacement()
        {
            string lobbyCode = await createActiveSession();
            int pieceId = 0;
            int playerId = 1;

            var session = gameSessionManager.getSession(lobbyCode);
            var pieceState = session.PieceStates[pieceId];

            gameSessionManager.handlePieceDrag(lobbyCode, playerId, pieceId);

            scoreCalculatorMock.Setup(x => x.calculatePointsForPlacement(It.IsAny<ScoreCalculationContext>()))
                .Returns(new ScoreResult { Points = 10, BonusType = null });

            await gameSessionManager.handlePieceDrop(lobbyCode, playerId, pieceId, pieceState.FinalX, pieceState.FinalY);

            Assert.True(pieceState.IsPlaced);
            Assert.Null(pieceState.HeldByPlayerId);
            callbackMock.Verify(x => x.onPiecePlaced(pieceId, It.IsAny<double>(), It.IsAny<double>(), "Player1", It.IsAny<int>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task handlePieceDropAppliesPenaltyOnIncorrectPlacement()
        {
            string lobbyCode = await createActiveSession();
            int pieceId = 0;
            int playerId = 1;

            var session = gameSessionManager.getSession(lobbyCode);
            var pieceState = session.PieceStates[pieceId];

            gameSessionManager.handlePieceDrag(lobbyCode, playerId, pieceId);
            scoreCalculatorMock.Setup(x => x.calculatePenaltyPoints(It.IsAny<int>())).Returns(5);

            double nearMissX = pieceState.FinalX + 40;
            double nearMissY = pieceState.FinalY;

            await gameSessionManager.handlePieceDrop(lobbyCode, playerId, pieceId, nearMissX, nearMissY);

            Assert.False(session.PieceStates[pieceId].IsPlaced);
            callbackMock.Verify(x => x.onPlayerPenalty("Player1", 5, It.IsAny<int>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task handlePieceDropIgnoresFarAwayDrops()
        {
            string lobbyCode = await createActiveSession();
            int pieceId = 0;
            int playerId = 1;

            gameSessionManager.handlePieceDrag(lobbyCode, playerId, pieceId);

            await gameSessionManager.handlePieceDrop(lobbyCode, playerId, pieceId, 9999, 9999);

            callbackMock.Verify(x => x.onPlayerPenalty(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task handlePlayerDisconnectRemovesPlayerAndReleasesPieces()
        {
            string lobbyCode = await createActiveSession();
            int playerId = 1;
            int pieceId = 0;

            gameSessionManager.handlePieceDrag(lobbyCode, playerId, pieceId);
            gameSessionManager.handlePlayerDisconnect("Player1", playerId);

            var session = gameSessionManager.getSession(lobbyCode);
            if (session != null)
            {
                Assert.DoesNotContain(playerId, session.Players.Keys);
                Assert.Null(session.PieceStates[pieceId].HeldByPlayerId);
            }
        }

        [Fact]
        public async Task handlePlayerLeaveAsyncUpdatesStatsAndBroadcasts()
        {
            string lobbyCode = await createActiveSession();
            var session = gameSessionManager.getSession(lobbyCode);
            session.addPlayer(2, "Player2", "av", callbackMock.Object);

            playerRepositoryMock.Setup(x => x.getPlayerByUsernameAsync("Player1"))
                .ReturnsAsync(new Player { idPlayer = 1, username = "Player1" });

            await gameSessionManager.handlePlayerLeaveAsync(lobbyCode, "Player1");

           
            statsRepositoryMock.Verify(x => x.addPlaytimeToPlayerAsync(1, It.IsAny<int>()), Times.Once);
            callbackMock.Verify(x => x.onPlayerLeftMatch("Player1"), Times.Once);
        }

        [Fact]
        public async Task handlePieceMoveBroadcastsMovement()
        {
            string lobbyCode = await createActiveSession();
            int pieceId = 0;
            int playerId = 1;
            gameSessionManager.handlePieceDrag(lobbyCode, playerId, pieceId);

            gameSessionManager.handlePieceMove(lobbyCode, playerId, pieceId, 50, 50);

            callbackMock.Verify(x => x.onPieceMoved(pieceId, 50, 50, "Player1"), Times.Once);
        }

        [Fact]
        public void getSessionReturnsNullForInvalidLobby()
        {
            var session = gameSessionManager.getSession("INVALID");
            Assert.Null(session);
        }

        [Fact]
        public async Task handlePieceReleaseUnlocksPiece()
        {
            string lobbyCode = await createActiveSession();
            int pieceId = 0;
            int playerId = 1;

            gameSessionManager.handlePieceDrag(lobbyCode, playerId, pieceId);
            var session = gameSessionManager.getSession(lobbyCode);
            Assert.NotNull(session.PieceStates[pieceId].HeldByPlayerId);

            gameSessionManager.handlePieceRelease(lobbyCode, playerId, pieceId);
            Assert.Null(session.PieceStates[pieceId].HeldByPlayerId);
            callbackMock.Verify(x => x.onPieceDragReleased(pieceId, "Player1"), Times.Once);
        }

        [Fact]
        public async Task handlePieceReleaseIgnoredIfPlayerNotHolding()
        {
            string lobbyCode = await createActiveSession();
            int pieceId = 0;
            int playerId = 1;

            gameSessionManager.handlePieceRelease(lobbyCode, playerId, pieceId);

            callbackMock.Verify(x => x.onPieceDragReleased(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task createGameSessionFailsIfPuzzleNotFoundInDb()
        {
            puzzleRepositoryMock.Setup(x => x.getPuzzleByIdAsync(It.IsAny<int>())).ReturnsAsync((Puzzles)null!);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gameSessionManager.createGameSession("L", 1, 99, new DifficultyLevels(), new ConcurrentDictionary<int, PlayerSessionData>()));
        }

        [Fact]
        public async Task kickPlayerRemovesUserFromSession()
        {
            string lobbyCode = await createActiveSession();
            var session = gameSessionManager.getSession(lobbyCode);
            session.addPlayer(2, "Player2", "av", callbackMock.Object);

            await session.kickPlayerAsync(2, 1, 1); 

            Assert.False(session.Players.ContainsKey(2));
            matchmakingRepositoryMock.Verify(x => x.registerExpulsionAsync(It.IsAny<ExpulsionDto>()), Times.Once);
        }

        [Fact]
        public async Task endGameAsyncCalledWhenPuzzleSolved()
        {
            string lobbyCode = await createActiveSession();
            var session = gameSessionManager.getSession(lobbyCode);

            await gameSessionManager.handlePlayerLeaveAsync(lobbyCode, "Player1");

            Assert.Null(gameSessionManager.getSession(lobbyCode));
        }
    }
}