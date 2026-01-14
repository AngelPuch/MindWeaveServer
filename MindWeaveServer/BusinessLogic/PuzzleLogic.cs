using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Utilities;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class PuzzleLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IPuzzleRepository puzzleRepository;
        private readonly IPlayerRepository playerRepository;

        private const string PNG = ".png";
        private const string JPG = ".jpg";
        private const string JPEG = ".jpeg";
        private const string UPLOAD_FOLDER_NAME = "UploadedPuzzles";
        private const string DEFAULT_PUZZLES_FOLDER = "DefaultPuzzles";
        private const string DEFAULT_PUZZLE_PREFIX = "puzzleDefault";
        private const string DEFAULT_PUZZLE_NAME_FALLBACK = "Puzzle";
        private const char FILENAME_SEPARATOR = '_';

        public PuzzleLogic(IPuzzleRepository puzzleRepository, IPlayerRepository playerRepository)
        {
            this.puzzleRepository = puzzleRepository;
            this.playerRepository = playerRepository;
        }

        public async Task<List<PuzzleInfoDto>> getAvailablePuzzlesAsync()
        {
            var puzzlesFromDb = await puzzleRepository.getAvailablePuzzlesAsync();
            var puzzles = new List<PuzzleInfoDto>();

            foreach (var p in puzzlesFromDb)
            {
                bool isDefault = p.image_path.StartsWith(DEFAULT_PUZZLE_PREFIX, StringComparison.OrdinalIgnoreCase);
                string targetFolder = isDefault ? getDefaultPuzzlesPath() : getUploadFolderPath();

                byte[] imageBytes = tryLoadFileWithExtensions(targetFolder, p.image_path);

                var dto = new PuzzleInfoDto
                {
                    PuzzleId = p.puzzle_id,
                    Name = Path.GetFileNameWithoutExtension(p.image_path ?? DEFAULT_PUZZLE_NAME_FALLBACK),
                    ImagePath = p.image_path,
                    IsUploaded = !isDefault,
                    ImageBytes = imageBytes
                };

                puzzles.Add(dto);
            }

            return puzzles;
        }

        public async Task<UploadResultDto> uploadPuzzleImageAsync(string username, byte[] imageBytes, string fileName)
        {
            if (imageBytes == null || imageBytes.Length == 0 ||
                string.IsNullOrWhiteSpace(fileName) ||
                string.IsNullOrWhiteSpace(username))
            {
                return new UploadResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.PUZZLE_UPLOAD_FAILED
                };
            }

            byte[] optimizedBytes = ImageUtilities.optimizeImage(imageBytes);
            string uploadPath = getUploadFolderPath();

            ensureDirectoryExists(uploadPath);

            string uniqueFileName = generateUniqueFileName(fileName);
            string filePath = Path.Combine(uploadPath, uniqueFileName);

            if (!tryWriteFileToDisk(filePath, optimizedBytes))
            {
                return new UploadResultDto { Success = false, MessageCode = MessageCodes.PUZZLE_UPLOAD_FAILED };
            }

            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player == null)
            {
                tryDeleteFileForCleanup(filePath);
                return new UploadResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_USER_NOT_FOUND
                };
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
                MessageCode = MessageCodes.PUZZLE_UPLOAD_SUCCESS,
                NewPuzzleId = newPuzzle.puzzle_id
            };
        }

        public async Task<PuzzleDefinitionDto> getPuzzleDefinitionAsync(int puzzleId, int difficultyId)
        {
            var puzzleData = await puzzleRepository.getPuzzleByIdAsync(puzzleId);
            if (puzzleData == null)
            {
                return null;
            }

            var difficulty = await puzzleRepository.getDifficultyByIdAsync(difficultyId);
            if (difficulty == null)
            {
                return null;
            }

            byte[] imageBytes = loadPuzzleImageBytesOrThrow(puzzleData);

            return PuzzleGenerator.generatePuzzle(imageBytes, difficulty);
        }

        private static bool tryWriteFileToDisk(string filePath, byte[] bytes)
        {
            try
            {
                File.WriteAllBytes(filePath, bytes);
                return true;
            }
            catch (IOException ioEx)
            {
                logger.Error(ioEx, "I/O error writing file to disk: {FilePath}", filePath);
                return false;
            }
            catch (UnauthorizedAccessException authEx)
            {
                logger.Error(authEx, "Access denied writing file to disk: {FilePath}", filePath);
                return false;
            }
            catch (SecurityException secEx)
            {
                logger.Error(secEx, "Security error writing file to disk: {FilePath}", filePath);
                return false;
            }
        }

        private static byte[] loadPuzzleImageBytesOrThrow(Puzzles puzzleData)
        {
            bool isDefaultPuzzle = puzzleData.image_path.StartsWith(DEFAULT_PUZZLE_PREFIX, StringComparison.OrdinalIgnoreCase);
            string folderPath = isDefaultPuzzle ? getDefaultPuzzlesPath() : getUploadFolderPath();

            byte[] bytes = tryLoadFileWithExtensions(folderPath, puzzleData.image_path);

            if (bytes == null || bytes.Length == 0)
            {
                string msg = $"CRITICAL: Puzzle image file NOT FOUND on server. PuzzleId: {puzzleData.puzzle_id}. Search Path: {folderPath}";
                logger.Error(msg);
                throw new FileNotFoundException(msg, puzzleData.image_path);
            }

            return bytes;
        }

        private static byte[] tryLoadFileWithExtensions(string folderPath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return Array.Empty<byte>();
            }

            string fullPath = Path.Combine(folderPath, fileName);
            if (File.Exists(fullPath))
            {
                return readFileSafe(fullPath);
            }

            if (!Path.HasExtension(fullPath))
            {
                string[] extensions = { PNG, JPG, JPEG };
                foreach (var ext in extensions)
                {
                    string testPath = fullPath + ext;
                    if (File.Exists(testPath))
                    {
                        return readFileSafe(testPath);
                    }
                }
            }

            return Array.Empty<byte>();
        }

        private static byte[] readFileSafe(string path)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException ioEx)
            {
                logger.Error(ioEx, "I/O error reading file from disk: {FilePath}", path);
                return Array.Empty<byte>();
            }
            catch (UnauthorizedAccessException authEx)
            {
                logger.Error(authEx, "Access denied reading file from disk: {FilePath}", path);
                return Array.Empty<byte>();
            }
            catch (SecurityException secEx)
            {
                logger.Error(secEx, "Security error reading file from disk: {FilePath}", path);
                return Array.Empty<byte>();
            }
        }

        private static void ensureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
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

        private static void tryDeleteFileForCleanup(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (IOException ioEx)
            {
                logger.Debug(ioEx, "I/O error during cleanup file deletion: {FilePath}", filePath);
            }
            catch (UnauthorizedAccessException authEx)
            {
                logger.Debug(authEx, "Access denied during cleanup file deletion: {FilePath}", filePath);
            }
            catch (SecurityException secEx)
            {
                logger.Debug(secEx, "Security error during cleanup file deletion: {FilePath}", filePath);
            }
        }
    }
}