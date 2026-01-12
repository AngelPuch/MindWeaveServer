using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Utilities;
using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;

namespace MindWeaveServer.BusinessLogic
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Minor Code Smell",
    "S2245:Make sure that using this pseudorandom number generator is safe here",
    Justification = "Random is sufficient for puzzle generation - cryptographic security is not required for game content")]
    public class PuzzleGenerator
    {
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
        private const float TAB_SIZE_RATIO = 0.20f;

        public static PuzzleDefinitionDto generatePuzzle(byte[] imageBytes, DifficultyLevels difficulty)
        {
            validateInputs(imageBytes, difficulty);
            validateImageSize(imageBytes);

            var optimizedImageBytes = ImageUtilities.optimizeImageAsPng(imageBytes);

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
                throw new ArgumentNullException(nameof(imageBytes));
            }

            if (difficulty == null)
            {
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
                        throw new ArgumentException($"Image must be at least {MIN_SIZE}x{MIN_SIZE} pixels.");
                    }
                    if (img.Width > MAX_SIZE_LIMIT || img.Height > MAX_SIZE_LIMIT)
                    {
                        throw new ArgumentException($"Image dimensions exceed the maximum allowed limit of {MAX_SIZE_LIMIT}px.");
                    }
                }
            }
            catch (ArgumentException)
            {
                throw;
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

                int pieceWidth = image.Width / dimensions.Columns;
                int pieceHeight = image.Height / dimensions.Rows;
                int tabSize = (int)(Math.Min(pieceWidth, pieceHeight) * TAB_SIZE_RATIO);

                var edgesGrid = generateEdgesGrid(dimensions);

                var context = new PieceContext
                {
                    PieceWidth = pieceWidth,
                    PieceHeight = pieceHeight,
                    TabSize = tabSize,
                    SidebarStart = image.Width + SIDEBAR_OFFSET + tabSize,
                    ImageHeight = image.Height,
                    SourceImage = image
                };

                var silhouettes = new List<SilhouetteData>();
                var grid = generatePieces(dimensions, context, puzzleDef, edgesGrid, silhouettes);
                assignNeighbors(grid, dimensions);

                puzzleDef.SilhouetteImageBytes = ImageUtilities.generateSilhouetteImage(
                    puzzleDef.PuzzleWidth,
                    puzzleDef.PuzzleHeight,
                    silhouettes);

                foreach (var sil in silhouettes)
                {
                    sil.Path?.Dispose();
                }

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

        private static JigsawEdgeType[,][] generateEdgesGrid(GridDimensions dim)
        {
            Random random = threadSafeRandom.Value;
            var edges = new JigsawEdgeType[dim.Rows, dim.Columns][];

            for (int r = 0; r < dim.Rows; r++)
            {
                for (int c = 0; c < dim.Columns; c++)
                {
                    edges[r, c] = new JigsawEdgeType[4];

                    edges[r, c][0] = getTopEdge(r, c, edges);
                    edges[r, c][1] = getRightEdge(c, random, dim);
                    edges[r, c][2] = getBottomEdge(r, random, dim);
                    edges[r, c][3] = getLeftEdge(r, c, edges);
                }
            }

            return edges;
        }

        private static JigsawEdgeType getTopEdge(int row, int col, JigsawEdgeType[,][] edges)
        {
            if (row == 0)
            {
                return JigsawEdgeType.Flat;
            }

            return edges[row - 1, col][2] == JigsawEdgeType.Tab
                ? JigsawEdgeType.Blank
                : JigsawEdgeType.Tab;
        }

        private static JigsawEdgeType getRightEdge(int col, Random random, GridDimensions dim)
        {
            if (col == dim.Columns - 1)
            {
                return JigsawEdgeType.Flat;
            }

            return random.Next(2) == 0
                ? JigsawEdgeType.Tab
                : JigsawEdgeType.Blank;
        }

        private static JigsawEdgeType getBottomEdge(int row, Random random, GridDimensions dim)
        {
            if (row == dim.Rows - 1)
            {
                return JigsawEdgeType.Flat;
            }

            return random.Next(2) == 0
                ? JigsawEdgeType.Tab
                : JigsawEdgeType.Blank;
        }

        private static JigsawEdgeType getLeftEdge(int row, int col, JigsawEdgeType[,][] edges)
        {
            if (col == 0)
            {
                return JigsawEdgeType.Flat;
            }

            return edges[row, col - 1][1] == JigsawEdgeType.Tab
                ? JigsawEdgeType.Blank
                : JigsawEdgeType.Tab;
        }

        private static PuzzlePieceDefinitionDto[,] generatePieces(
            GridDimensions dim,
            PieceContext ctx,
            PuzzleDefinitionDto puzzleDef,
            JigsawEdgeType[,][] edgesGrid,
            List<SilhouetteData> silhouettes)
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

                    var pieceEdges = new JigsawPieceEdges
                    {
                        Top = edgesGrid[r, c][0],
                        Right = edgesGrid[r, c][1],
                        Bottom = edgesGrid[r, c][2],
                        Left = edgesGrid[r, c][3]
                    };

                    var piece = createJigsawPiece(ctx, pieceEdges, silhouettes);

                    grid[r, c] = piece;
                    puzzleDef.Pieces.Add(piece);
                    currentId++;
                }
            }
            return grid;
        }

        private static PuzzlePieceDefinitionDto createJigsawPiece(
            PieceContext ctx,
            JigsawPieceEdges edges,
            List<SilhouetteData> silhouettes)
        {
            Random random = threadSafeRandom.Value;

            int offsetX, offsetY, totalWidth, totalHeight;

            var path = JigsawPathGenerator.createJigsawPath(
                ctx.PieceWidth,
                ctx.PieceHeight,
                edges,
                ctx.TabSize,
                out offsetX,
                out offsetY,
                out totalWidth,
                out totalHeight);

            int sourceX = ctx.Column * ctx.PieceWidth - offsetX;
            int sourceY = ctx.Row * ctx.PieceHeight - offsetY;

            byte[] pieceBytes = ImageUtilities.extractPieceWithMask(
                ctx.SourceImage,
                path,
                sourceX,
                sourceY,
                totalWidth,
                totalHeight);

            double correctX = ctx.Column * ctx.PieceWidth - offsetX;
            double correctY = ctx.Row * ctx.PieceHeight - offsetY;

            silhouettes.Add(new SilhouetteData
            {
                Path = path,
                X = correctX,
                Y = correctY
            });

            return new PuzzlePieceDefinitionDto
            {
                PieceId = ctx.PieceId,
                Width = ctx.PieceWidth,
                Height = ctx.PieceHeight,
                RenderWidth = totalWidth,
                RenderHeight = totalHeight,
                OffsetX = offsetX,
                OffsetY = offsetY,
                SourceX = ctx.Column * ctx.PieceWidth,
                SourceY = ctx.Row * ctx.PieceHeight,
                CorrectX = correctX,
                CorrectY = correctY,
                InitialX = random.Next(ctx.SidebarStart, ctx.SidebarStart + SIDEBAR_WIDTH),
                InitialY = random.Next(0, Math.Max(totalHeight, ctx.ImageHeight - totalHeight)),
                PieceImageBytes = pieceBytes
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
            public int TabSize { get; set; }
            public int SidebarStart { get; set; }
            public int ImageHeight { get; set; }
            public Image SourceImage { get; set; }
            public int Row { get; set; }
            public int Column { get; set; }
            public int PieceId { get; set; }
        }
    }
}