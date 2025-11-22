using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Utilities;

namespace MindWeaveServer.BusinessLogic
{
    public class PuzzleLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly IPuzzleRepository puzzleRepository;
        private readonly IPlayerRepository playerRepository;
        private readonly PuzzleGenerator puzzleGenerator;

        private readonly string uploadFolderName = "UploadedPuzzles";

        public PuzzleLogic(IPuzzleRepository puzzleRepository, IPlayerRepository playerRepository)
        {
            this.puzzleRepository = puzzleRepository ?? throw new ArgumentNullException(nameof(puzzleRepository));
            this.playerRepository = playerRepository ?? throw new ArgumentNullException(nameof(playerRepository));
            this.puzzleGenerator = new PuzzleGenerator();
            logger.Info("PuzzleLogic instance created.");
        }

        public async Task<List<PuzzleInfoDto>> getAvailablePuzzlesAsync()
        {
            logger.Info("getAvailablePuzzlesAsync logic started.");
            var puzzlesFromDb = await puzzleRepository.getAvailablePuzzlesAsync();
            var puzzles = new List<PuzzleInfoDto>();
            string uploadPath = getUploadFolderPath();

            foreach (var p in puzzlesFromDb)
            {
                var dto = new PuzzleInfoDto
                {
                    PuzzleId = p.puzzle_id,
                    Name = Path.GetFileNameWithoutExtension(p.image_path ?? "Puzzle"),
                    ImagePath = p.image_path,
                    IsUploaded = !p.image_path.StartsWith("puzzleDefault", StringComparison.OrdinalIgnoreCase)
                };

                if (dto.IsUploaded)
                {

                    string filePath = Path.Combine(uploadPath, p.image_path);
                    if (File.Exists(filePath))
                    {
                        dto.ImageBytes = File.ReadAllBytes(filePath);
                    }
                    else
                    {
                        logger.Warn("Uploaded puzzle file not found, will not be sent to client: {Path}", filePath);
                    }
                }

                puzzles.Add(dto);
            }

            logger.Info("getAvailablePuzzlesAsync logic: Found {Count} puzzles.", puzzles.Count);
            return puzzles;
        }
        public async Task<UploadResultDto> uploadPuzzleImageAsync(string username, byte[] imageBytes, string fileName)
        {
            logger.Info("uploadPuzzleImageAsync logic started for user: {Username}, fileName: {FileName}", username ?? "NULL", fileName ?? "NULL");

            if (imageBytes == null || imageBytes.Length == 0 || string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("uploadPuzzleImageAsync logic failed for {Username}: Invalid data provided.", username ?? "NULL");
                return new UploadResultDto { Success = false, Message = Lang.ErrorPuzzleUploadInvalidData };
            }
            byte[] optimizedBytes = ImageUtilities.optimizeImage(imageBytes);

            logger.Info("Image optimized successfully. Original size: {Old} bytes, New size: {New} bytes.", imageBytes.Length, optimizedBytes.Length);


            string uploadPath = getUploadFolderPath();

            if (!Directory.Exists(uploadPath))
            {
                Directory.CreateDirectory(uploadPath);
                logger.Info("Created upload directory: {UploadPath}", uploadPath);
            }

            string uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                uniqueFileName = uniqueFileName.Replace(c, '_');
            }

            string filePath = Path.Combine(uploadPath, uniqueFileName);

            File.WriteAllBytes(filePath, optimizedBytes);
            logger.Info("Image saved successfully to: {FilePath}", filePath);

            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                logger.Warn("Upload Error: Player {Username} not found. Cleaning up file.", username);
                tryDeleteFile(filePath);
                return new UploadResultDto { Success = false, Message = Lang.ErrorPuzzleUploadPlayerNotFound };
            }

            var newPuzzle = new Puzzles
            {
                image_path = uniqueFileName,
                upload_date = DateTime.UtcNow,
                player_id = player.idPlayer
            };

            puzzleRepository.addPuzzle(newPuzzle);
            await puzzleRepository.saveChangesAsync();

            logger.Info("New puzzle record created with ID: {PuzzleId} for user {Username}", newPuzzle.puzzle_id, username);

            return new UploadResultDto
            {
                Success = true,
                Message = Lang.SuccessPuzzleUpload,
                NewPuzzleId = newPuzzle.puzzle_id
            };
        }

        private string getUploadFolderPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, uploadFolderName);
        }

        private static void tryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    logger.Info("Cleaned up orphaned puzzle file: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to clean up orphaned puzzle file: {FilePath}", filePath);
            }
        }

        public async Task<PuzzleDefinitionDto> getPuzzleDefinitionAsync(int puzzleId, int difficultyId)
        {

            var puzzleData = await puzzleRepository.getPuzzleByIdAsync(puzzleId);
            if (puzzleData == null)
            {
                logger.Warn("Puzzle definition requested for non-existent puzzleId: {puzzleId}", puzzleId);
                return null;
            }

            byte[] imageBytes;
            if (!puzzleData.image_path.StartsWith("puzzleDefault", StringComparison.OrdinalIgnoreCase))
            {
                string uploadPath = getUploadFolderPath();
                string filePath = Path.Combine(uploadPath, puzzleData.image_path);
                if (File.Exists(filePath))
                {
                    imageBytes = File.ReadAllBytes(filePath);
                }
                else
                {
                    logger.Error("Puzzle file not found for {puzzleId} at path: {filePath}", puzzleId, filePath);
                    return null;
                }
            }
            else
            {
                string defaultPuzzlePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefaultPuzzles");
                string filePath = Path.Combine(defaultPuzzlePath, puzzleData.image_path);
                if (File.Exists(filePath))
                {
                    imageBytes = File.ReadAllBytes(filePath);
                }
                else
                {
                    logger.Error("DEFAULT PUZZLE FILE NOT FOUND at path: {filePath}", filePath);
                    return null;
                }
            }

            var difficulty = await puzzleRepository.getDifficultyByIdAsync(difficultyId);
            if (difficulty == null)
            {
                logger.Warn("Invalid difficultyId {DifficultyId} requested.", difficultyId);
                return null;
            }

            PuzzleDefinitionDto puzzleDefinition = puzzleGenerator.generatePuzzle(imageBytes, difficulty);
            return puzzleDefinition;

        }

    }
}