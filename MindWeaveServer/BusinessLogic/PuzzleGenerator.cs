using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Utilities;
using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace MindWeaveServer.BusinessLogic
{
    public class PuzzleGenerator
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private static readonly ThreadLocal<Random> threadSafeRandom = new ThreadLocal<Random>(() =>
            new Random(Guid.NewGuid().GetHashCode()));

        private const int SIDEBAR_OFFSET = 30;
        private const int SIDEBAR_WIDTH = 80;
        private const int EASY_PUZZLE_PIECES = 25;
        private const int MEDIUM_PUZZLE_PIECES = 50;
        private const int GRID_SIZE_SMALL = 5;
        private const int GRID_SIZE_LARGE = 10;
        private const int MIN_SIZE = 100;
        private const int MAX_SIZE_LIMIT = 4096; 

        public static PuzzleDefinitionDto generatePuzzle(byte[] imageBytes, DifficultyLevels difficulty)
        {
            validateInputs(imageBytes, difficulty);

            validateImageSize(imageBytes);

            var optimizedImageBytes = ImageUtilities.optimizeImage(imageBytes);

            if (optimizedImageBytes == null)
            {
                throw new ArgumentException("The file is not a valid image or could not be processed.");
            }

            var dimensions = calculateGridDimensions(difficulty.piece_count);

            return createPuzzleStructure(optimizedImageBytes, dimensions);
        }

        private static void validateInputs(byte[] imageBytes, DifficultyLevels difficulty)
        {
            if (imageBytes == null || imageBytes.Length == 0)
            {
                logger.Error("Image bytes are null or empty.");
                throw new ArgumentNullException(nameof(imageBytes));
            }

            if (difficulty == null)
            {
                logger.Error("Difficulty level is null.");
                throw new ArgumentNullException(nameof(difficulty));
            }
        }

        private static void validateImageSize(byte[] imageBytes)
        {
            try
            {
                using (var ms = new MemoryStream(imageBytes))
                using (var img = Image.FromStream(ms))
                {
                    if (img.Width < MIN_SIZE || img.Height < MIN_SIZE)
                    {
                        logger.Warn($"Rejected image too small: {img.Width}x{img.Height}. Min: {MIN_SIZE}px");
                        throw new ArgumentException($"Image must be at least {MIN_SIZE}x{MIN_SIZE} pixels.");
                    }
                    if (img.Width > MAX_SIZE_LIMIT || img.Height > MAX_SIZE_LIMIT)
                    {
                        logger.Warn($"Rejected image too large (dimensions): {img.Width}x{img.Height}. Max: {MAX_SIZE_LIMIT}px");
                        throw new ArgumentException($"Image dimensions exceed the maximum allowed limit of {MAX_SIZE_LIMIT}px.");
                    }
                }
            }
            catch (Exception)
            {
                throw new ArgumentException("Invalid image data or format.");
            }
        }

        private static PuzzleDefinitionDto createPuzzleStructure(byte[] imageBytes, GridDimensions dimensions)
        {
            using (var memoryStream = new MemoryStream(imageBytes))
            using (var image = Image.FromStream(memoryStream))
            {
                var puzzleDef = initializePuzzleDto(imageBytes, image);

                var context = new PieceContext
                {
                    PieceWidth = image.Width / dimensions.Columns,
                    PieceHeight = image.Height / dimensions.Rows,
                    SidebarStart = image.Width + SIDEBAR_OFFSET,
                    ImageHeight = image.Height
                };

                var grid = generatePieces(dimensions, context, puzzleDef);
                assignNeighbors(grid, dimensions);

                return puzzleDef;
            }
        }

        private static PuzzleDefinitionDto initializePuzzleDto(byte[] bytes, Image img)
        {
            return new PuzzleDefinitionDto
            {
                FullImageBytes = bytes,
                Pieces = new List<PuzzlePieceDefinitionDto>(),
                PuzzleWidth = img.Width,
                PuzzleHeight = img.Height
            };
        }

        private static GridDimensions calculateGridDimensions(int pieceCount)
        {
            var dimensions = new GridDimensions();

            if (pieceCount == EASY_PUZZLE_PIECES)
            {
                dimensions.Columns = GRID_SIZE_SMALL;
                dimensions.Rows = GRID_SIZE_SMALL;
            }
            else if (pieceCount == MEDIUM_PUZZLE_PIECES)
            {
                dimensions.Columns = GRID_SIZE_LARGE;
                dimensions.Rows = GRID_SIZE_SMALL;
            }
            else
            {
                dimensions.Columns = GRID_SIZE_LARGE;
                dimensions.Rows = GRID_SIZE_LARGE;
            }

            return dimensions;
        }

        private static PuzzlePieceDefinitionDto[,] generatePieces(GridDimensions dim, PieceContext ctx, PuzzleDefinitionDto puzzleDef)
        {
            var grid = new PuzzlePieceDefinitionDto[dim.Rows, dim.Columns];
            int currentId = 0;

            for (int r = 0; r < dim.Rows; r++)
            {
                for (int c = 0; c < dim.Columns; c++)
                {
                    ctx.Row = r;
                    ctx.Column = c;
                    ctx.PieceId = currentId;

                    var piece = createSinglePiece(ctx);

                    grid[r, c] = piece;
                    puzzleDef.Pieces.Add(piece);
                    currentId++;
                }
            }
            return grid;
        }

        private static PuzzlePieceDefinitionDto createSinglePiece(PieceContext ctx)
        {
            Random random = threadSafeRandom.Value;

            return new PuzzlePieceDefinitionDto
            {
                PieceId = ctx.PieceId,
                Width = ctx.PieceWidth,
                Height = ctx.PieceHeight,
                SourceX = ctx.Column * ctx.PieceWidth,
                SourceY = ctx.Row * ctx.PieceHeight,
                CorrectX = ctx.Column * ctx.PieceWidth,
                CorrectY = ctx.Row * ctx.PieceHeight,
                InitialX = random.Next(ctx.SidebarStart, ctx.SidebarStart + SIDEBAR_WIDTH),
                InitialY = random.Next(0, Math.Max(ctx.PieceHeight, ctx.ImageHeight - ctx.PieceHeight))
            };
        }

        private static void assignNeighbors(PuzzlePieceDefinitionDto[,] grid, GridDimensions dim)
        {
            for (var r = 0; r < dim.Rows; r++)
            {
                for (var c = 0; c < dim.Columns; c++)
                {
                    linkNeighbors(grid, r, c, dim);
                }
            }
        }

        private static void linkNeighbors(PuzzlePieceDefinitionDto[,] grid, int r, int c, GridDimensions dim)
        {
            var piece = grid[r, c];
            piece.TopNeighborId = (r > 0) ? grid[r - 1, c].PieceId : (int?)null;
            piece.BottomNeighborId = (r < dim.Rows - 1) ? grid[r + 1, c].PieceId : (int?)null;
            piece.LeftNeighborId = (c > 0) ? grid[r, c - 1].PieceId : (int?)null;
            piece.RightNeighborId = (c < dim.Columns - 1) ? grid[r, c + 1].PieceId : (int?)null;
        }

        private sealed class GridDimensions
        {
            public int Columns { get; set; }
            public int Rows { get; set; }
        }

        private sealed class PieceContext
        {
            public int PieceWidth { get; set; }
            public int PieceHeight { get; set; }
            public int SidebarStart { get; set; }
            public int ImageHeight { get; set; }

            public int Row { get; set; }
            public int Column { get; set; }
            public int PieceId { get; set; }
        }
    }
}