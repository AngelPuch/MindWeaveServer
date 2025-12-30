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

        private const string UPLOAD_FOLDER_NAME = "UploadedPuzzles";
        private const string DEFAULT_PUZZLES_FOLDER = "DefaultPuzzles";
        private const string DEFAULT_PUZZLE_PREFIX = "puzzleDefault";
        private const string DEFAULT_PUZZLE_NAME_FALLBACK = "Puzzle";
        private const string LOG_PLACEHOLDER_NULL = "NULL";
        private const char FILENAME_SEPARATOR = '_';

        public PuzzleLogic(
            IPuzzleRepository puzzleRepository,
            IPlayerRepository playerRepository)
        {
            this.puzzleRepository = puzzleRepository ?? throw new ArgumentNullException(nameof(puzzleRepository));
            this.playerRepository = playerRepository ?? throw new ArgumentNullException(nameof(playerRepository));
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
                    Name = Path.GetFileNameWithoutExtension(p.image_path ?? DEFAULT_PUZZLE_NAME_FALLBACK),
                    ImagePath = p.image_path,
                    IsUploaded = !p.image_path.StartsWith(DEFAULT_PUZZLE_PREFIX, StringComparison.OrdinalIgnoreCase)
                };

                if (dto.IsUploaded)
                {
                    string filePath = Path.Combine(uploadPath, p.image_path);

                    if (File.Exists(filePath))
                    {
                        dto.ImageBytes = File.ReadAllBytes(filePath);
                    }
                }

                puzzles.Add(dto);
            }

            return puzzles;
        }

        public async Task<UploadResultDto> uploadPuzzleImageAsync(string username, byte[] imageBytes, string fileName)
        {
            logger.Info("uploadPuzzleImageAsync logic started for user: {0}, fileName: {1}",
                username ?? LOG_PLACEHOLDER_NULL, fileName ?? LOG_PLACEHOLDER_NULL);

            if (imageBytes == null || imageBytes.Length == 0 ||
                string.IsNullOrWhiteSpace(fileName) ||
                string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("uploadPuzzleImageAsync logic failed for {0}: Invalid data provided.", username ?? LOG_PLACEHOLDER_NULL);
                return new UploadResultDto { Success = false, Message = Lang.ErrorPuzzleUploadInvalidData };
            }

            byte[] optimizedBytes = ImageUtilities.optimizeImage(imageBytes);
            string uploadPath = getUploadFolderPath();

            if (!Directory.Exists(uploadPath))
            {
                Directory.CreateDirectory(uploadPath);
            }

            string uniqueFileName = generateUniqueFileName(fileName);
            string filePath = Path.Combine(uploadPath, uniqueFileName);

            File.WriteAllBytes(filePath, optimizedBytes);

            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                logger.Warn("Upload Error: Player {0} not found. Cleaning up file.", username);
                tryDeleteFileForCleanup(filePath);
                return new UploadResultDto { Success = false, Message = Lang.ErrorPuzzleUploadPlayerNotFound };
            }

            var newPuzzle = new Puzzles
            {
                image_path = uniqueFileName,
                upload_date = DateTime.UtcNow,
                player_id = player.idPlayer
            };

            puzzleRepository.addPuzzle(newPuzzle);

            return new UploadResultDto
            {
                Success = true,
                Message = Lang.SuccessPuzzleUpload,
                NewPuzzleId = newPuzzle.puzzle_id
            };
        }

        public async Task<PuzzleDefinitionDto> getPuzzleDefinitionAsync(int puzzleId, int difficultyId)
        {
            logger.Info("getPuzzleDefinitionAsync started for puzzleId: {0}, difficultyId: {1}",
                puzzleId, difficultyId);

            var puzzleData = await puzzleRepository.getPuzzleByIdAsync(puzzleId);
            if (puzzleData == null)
            {
                logger.Warn("Puzzle definition requested for non-existent puzzleId: {0}", puzzleId);
                return null;
            }

            byte[] imageBytes = loadPuzzleImageBytes(puzzleData);
            if (imageBytes == null)
            {
                logger.Error("Puzzle file not found for puzzleId: {0}", puzzleId);
                throw new FileNotFoundException(
                    $"Puzzle image file not found for puzzle {puzzleId}",
                    puzzleData.image_path);
            }

            var difficulty = await puzzleRepository.getDifficultyByIdAsync(difficultyId);
            if (difficulty == null)
            {
                logger.Warn("Invalid difficultyId {0} requested.", difficultyId);
                return null;
            }

            return PuzzleGenerator.generatePuzzle(imageBytes, difficulty);
        }

        private static string getUploadFolderPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UPLOAD_FOLDER_NAME);
        }

        private static string getDefaultPuzzlesPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DEFAULT_PUZZLES_FOLDER);
        }

        private static string generateUniqueFileName(string originalFileName)
        {
            string safeName = Path.GetFileName(originalFileName);

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(c, FILENAME_SEPARATOR);
            }

            return $"{Guid.NewGuid()}{FILENAME_SEPARATOR}{safeName}";
        }

        private static byte[] loadPuzzleImageBytes(Puzzles puzzleData)
        {
            bool isDefaultPuzzle = puzzleData.image_path.StartsWith(DEFAULT_PUZZLE_PREFIX, StringComparison.OrdinalIgnoreCase);

            var filePath = Path.Combine(isDefaultPuzzle ? getDefaultPuzzlesPath() : getUploadFolderPath(), puzzleData.image_path);

            if (!File.Exists(filePath))
            {
                logger.Error("Puzzle file not found at path: {0}", filePath);
                return Array.Empty<byte>();
            }

            return File.ReadAllBytes(filePath);
        }

        private static void tryDeleteFileForCleanup(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (IOException ex)
            {
                logger.Warn(ex, "Failed to clean up file (IO): {0}", filePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.Warn(ex, "Failed to clean up file (Access): {0}", filePath);
            }
        }
    }
}