using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.DataAccess;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace MindWeaveServer.BusinessLogic
{
    public class PuzzleGenerator
    {
        private static readonly Random random = new Random();
        public PuzzleDefinitionDto generatePuzzle(byte[] imageBytes, DifficultyLevels difficulty)
        {
            int columns, rows;
            switch (difficulty.piece_count)
            {
                case 25:
                    columns = 5;
                    rows = 5;
                    break;
                case 50:
                    columns = 10;
                    rows = 5;
                    break;
                default:
                    columns = 10;
                    rows = 10;
                    break;
            }

            var puzzleDef = new PuzzleDefinitionDto
            {
                FullImageBytes = imageBytes,
                Pieces = new List<PuzzlePieceDefinitionDto>()
            };

            PuzzlePieceDefinitionDto[,] pieceGrid = new PuzzlePieceDefinitionDto[rows, columns];

            using (var ms = new MemoryStream(imageBytes))
            using (var img = Image.FromStream(ms))
            {
                puzzleDef.PuzzleWidth = img.Width;
                puzzleDef.PuzzleHeight = img.Height;

                int pieceWidth = img.Width / columns;
                int pieceHeight = img.Height / rows;

                int scatterAreaWidth = (int)(img.Width * 1.5);
                int scatterAreaHeight = (int)(img.Height * 1.5);

                int pieceId = 0;

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        var piece = new PuzzlePieceDefinitionDto
                        {
                            PieceId = pieceId,
                            Width = pieceWidth,
                            Height = pieceHeight,

                            SourceX = c * pieceWidth,
                            SourceY = r * pieceHeight,

                            CorrectX = c * pieceWidth,
                            CorrectY = r * pieceHeight,

                            InitialX = random.Next(0, scatterAreaWidth - pieceWidth),
                            InitialY = random.Next(0, scatterAreaHeight - pieceHeight)

                        };

                        pieceGrid[r, c] = piece;
                        puzzleDef.Pieces.Add(piece);
                        pieceId++;
                    }
                }

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        var currentPiece = pieceGrid[r, c];

                        currentPiece.TopNeighborId = (r > 0) ? pieceGrid[r - 1, c].PieceId : (int?)null;
                        currentPiece.BottomNeighborId = (r < rows - 1) ? pieceGrid[r + 1, c].PieceId : (int?)null;
                        currentPiece.LeftNeighborId = (c > 0) ? pieceGrid[r, c - 1].PieceId : (int?)null;
                        currentPiece.RightNeighborId = (c < columns - 1) ? pieceGrid[r, c + 1].PieceId : (int?)null;
                    }
                }
            }

            System.Diagnostics.Trace.WriteLine($"[SERVER] generatePuzzle: Checking data for {puzzleDef.Pieces.Count} pieces...");
            foreach (var piece in puzzleDef.Pieces.Take(5))
            {
                System.Diagnostics.Trace.WriteLine($"[SERVER] Piece {piece.PieceId} Neighbors: " +
                                                   $"T={piece.TopNeighborId}, " +
                                                   $"B={piece.BottomNeighborId}, " +
                                                   $"L={piece.LeftNeighborId}, " +
                                                   $"R={piece.RightNeighborId}");
            }
            System.Diagnostics.Trace.WriteLine("[SERVER] generatePuzzle: Check complete.");

            return puzzleDef;
        }
    }
}