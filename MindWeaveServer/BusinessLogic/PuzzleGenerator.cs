using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.DataAccess;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace MindWeaveServer.BusinessLogic
{
    public class PuzzleGenerator
    {
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
                fullImageBytes = imageBytes,
                pieces = new List<PuzzlePieceDefinitionDto>()
            };

            PuzzlePieceDefinitionDto[,] pieceGrid = new PuzzlePieceDefinitionDto[rows, columns];

            using (var ms = new MemoryStream(imageBytes))
            using (var img = Image.FromStream(ms))
            {
                puzzleDef.puzzleWidth = img.Width;
                puzzleDef.puzzleHeight = img.Height;

                int pieceWidth = img.Width / columns;
                int pieceHeight = img.Height / rows;

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
                            CorrectY = r * pieceHeight

                        };

                        pieceGrid[r, c] = piece;
                        puzzleDef.pieces.Add(piece);
                        pieceId++;
                    }
                }

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        var currentPiece = pieceGrid[r, c];

                        // Top Neighbor
                        currentPiece.TopNeighborId = (r > 0) ? pieceGrid[r - 1, c].PieceId : (int?)null;

                        // Bottom Neighbor
                        currentPiece.BottomNeighborId = (r < rows - 1) ? pieceGrid[r + 1, c].PieceId : (int?)null;

                        // Left Neighbor
                        currentPiece.LeftNeighborId = (c > 0) ? pieceGrid[r, c - 1].PieceId : (int?)null;

                        // Right Neighbor
                        currentPiece.RightNeighborId = (c < columns - 1) ? pieceGrid[r, c + 1].PieceId : (int?)null;
                    }
                }
            }

            // --- DEBUG DEL SERVIDOR (PASO 2) ---
            // Vamos a verificar si los datos existen ANTES de enviarlos al cliente.
            System.Diagnostics.Trace.WriteLine($"[SERVER] generatePuzzle: Checking data for {puzzleDef.pieces.Count} pieces...");
            foreach (var piece in puzzleDef.pieces.Take(5)) // Loguear solo las primeras 5 para no saturar
            {
                System.Diagnostics.Trace.WriteLine($"[SERVER] Piece {piece.PieceId} Neighbors: " +
                                                   $"T={piece.TopNeighborId}, " +
                                                   $"B={piece.BottomNeighborId}, " +
                                                   $"L={piece.LeftNeighborId}, " +
                                                   $"R={piece.RightNeighborId}");
            }
            System.Diagnostics.Trace.WriteLine("[SERVER] generatePuzzle: Check complete.");
            // --- FIN DEBUG ---

            return puzzleDef;
        }
    }
}