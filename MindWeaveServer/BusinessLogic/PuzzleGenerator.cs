using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.DataAccess;
using System.Collections.Generic;
using System.Drawing; 
using System.IO;

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
                case 100:
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
                            pieceId = pieceId,
                            width = pieceWidth,
                            height = pieceHeight,

                            sourceX = c * pieceWidth,
                            sourceY = r * pieceHeight,

                            correctX = c * pieceWidth,
                            correctY = r * pieceHeight
                        };

                        puzzleDef.pieces.Add(piece);
                        pieceId++;
                    }
                }
            }

            return puzzleDef;
        }
    }
}