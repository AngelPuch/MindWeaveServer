using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace MindWeaveServer.BusinessLogic
{
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
        private const int RANDOM_EDGE_THRESHOLD = 2;
        private const int INITIAL_PIECE_ID = 0;
        private const int TOP_EDGE_INDEX = 0;
        private const int RIGHT_EDGE_INDEX = 1;
        private const int BOTTOM_EDGE_INDEX = 2;
        private const int LEFT_EDGE_INDEX = 3;
        private const int EDGES_PER_PIECE = 4;

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
                    validateImageDimensions(img);
                }
            }
            catch (OutOfMemoryException)
            {
                throw new ArgumentException("Invalid image data or format.");
            }
            catch (FileNotFoundException)
            {
                throw new ArgumentException("Invalid image data or format.");
            }
            catch (InvalidOperationException)
            {
                throw new ArgumentException("Invalid image data or format.");
            }
        }

        private static void validateImageDimensions(Image img)
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
                    edges[r, c] = new JigsawEdgeType[EDGES_PER_PIECE];

                    edges[r, c][TOP_EDGE_INDEX] = getTopEdge(r, c, edges);
                    edges[r, c][RIGHT_EDGE_INDEX] = getRightEdge(c, random, dim);
                    edges[r, c][BOTTOM_EDGE_INDEX] = getBottomEdge(r, random, dim);
                    edges[r, c][LEFT_EDGE_INDEX] = getLeftEdge(r, c, edges);
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

            return edges[row - 1, col][BOTTOM_EDGE_INDEX] == JigsawEdgeType.Tab
                ? JigsawEdgeType.Blank
                : JigsawEdgeType.Tab;
        }

        private static JigsawEdgeType getRightEdge(int col, Random random, GridDimensions dim)
        {
            if (col == dim.Columns - 1)
            {
                return JigsawEdgeType.Flat;
            }

            return random.Next(RANDOM_EDGE_THRESHOLD) == 0
                ? JigsawEdgeType.Tab
                : JigsawEdgeType.Blank;
        }

        private static JigsawEdgeType getBottomEdge(int row, Random random, GridDimensions dim)
        {
            if (row == dim.Rows - 1)
            {
                return JigsawEdgeType.Flat;
            }

            return random.Next(RANDOM_EDGE_THRESHOLD) == 0
                ? JigsawEdgeType.Tab
                : JigsawEdgeType.Blank;
        }

        private static JigsawEdgeType getLeftEdge(int row, int col, JigsawEdgeType[,][] edges)
        {
            if (col == 0)
            {
                return JigsawEdgeType.Flat;
            }

            return edges[row, col - 1][RIGHT_EDGE_INDEX] == JigsawEdgeType.Tab
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
            int currentId = INITIAL_PIECE_ID;

            for (int r = 0; r < dim.Rows; r++)
            {
                for (int c = 0; c < dim.Columns; c++)
                {
                    ctx.Row = r;
                    ctx.Column = c;
                    ctx.PieceId = currentId;

                    var pieceEdges = new JigsawPieceEdges
                    {
                        Top = edgesGrid[r, c][TOP_EDGE_INDEX],
                        Right = edgesGrid[r, c][RIGHT_EDGE_INDEX],
                        Bottom = edgesGrid[r, c][BOTTOM_EDGE_INDEX],
                        Left = edgesGrid[r, c][LEFT_EDGE_INDEX]
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

            var pathResult = JigsawPathGenerator.createJigsawPath(
                ctx.PieceWidth,
                ctx.PieceHeight,
                edges,
                ctx.TabSize);

            int sourceX = ctx.Column * ctx.PieceWidth - pathResult.OffsetX;
            int sourceY = ctx.Row * ctx.PieceHeight - pathResult.OffsetY;

            byte[] pieceBytes = ImageUtilities.extractPieceWithMask(
                ctx.SourceImage,
                pathResult.Path,
                sourceX,
                sourceY,
                pathResult.TotalWidth,
                pathResult.TotalHeight);

            double correctX = ctx.Column * ctx.PieceWidth - pathResult.OffsetX;
            double correctY = ctx.Row * ctx.PieceHeight - pathResult.OffsetY;

            silhouettes.Add(new SilhouetteData
            {
                Path = pathResult.Path,
                X = correctX,
                Y = correctY
            });

            return new PuzzlePieceDefinitionDto
            {
                PieceId = ctx.PieceId,
                Width = ctx.PieceWidth,
                Height = ctx.PieceHeight,
                RenderWidth = pathResult.TotalWidth,
                RenderHeight = pathResult.TotalHeight,
                OffsetX = pathResult.OffsetX,
                OffsetY = pathResult.OffsetY,
                SourceX = ctx.Column * ctx.PieceWidth,
                SourceY = ctx.Row * ctx.PieceHeight,
                CorrectX = correctX,
                CorrectY = correctY,
                InitialX = random.Next(ctx.SidebarStart, ctx.SidebarStart + SIDEBAR_WIDTH),
                InitialY = random.Next(0, Math.Max(pathResult.TotalHeight, ctx.ImageHeight - pathResult.TotalHeight)),
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