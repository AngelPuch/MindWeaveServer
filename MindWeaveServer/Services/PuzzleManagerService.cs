using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using NLog;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class PuzzleManagerService : IPuzzleManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly string uploadFolderName = "UploadedPuzzles";
        private string getUploadFolderPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, uploadFolderName);
        }


        public async Task<List<PuzzleInfoDto>> getAvailablePuzzlesAsync()
        {
            logger.Info("getAvailablePuzzlesAsync request received.");
            try
            {
                using (var context = new MindWeaveDBEntities1())
                {
                    var puzzlesFromDb = await context.Puzzles
                                             .OrderBy(p => p.puzzle_id)
                                             .Select(p => new {
                                                 Id = p.puzzle_id,
                                                 Path = p.image_path
                                             })
                                             .ToListAsync();

                    var puzzles = puzzlesFromDb.Select(p => new PuzzleInfoDto
                    {
                        puzzleId = p.Id,
                        imagePath = p.Path,
                        name = Path.GetFileNameWithoutExtension(p.Path ?? "Puzzle")
                    }).ToList();

                    logger.Info("getAvailablePuzzlesAsync: Found {Count} puzzles in DB.", puzzles.Count);
                    return puzzles;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error getting available puzzles.");
                return new List<PuzzleInfoDto>();
            }
        }

        public async Task<UploadResultDto> uploadPuzzleImageAsync(string username, byte[] imageBytes, string fileName)
        {
            logger.Info("uploadPuzzleImageAsync attempt by user: {Username}, fileName: {FileName}", username ?? "NULL", fileName ?? "NULL");

            if (imageBytes == null || imageBytes.Length == 0 || string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("uploadPuzzleImageAsync failed for {Username}: Invalid data provided (null/empty bytes, fileName, or username).", username ?? "NULL");
                return new UploadResultDto { success = false, message = "Invalid data provided for upload." }; // TODO: Lang key
            }

            string uploadPath = getUploadFolderPath();

            try
            {
                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                    logger.Info("Created upload directory: {UploadPath}", uploadPath);
                }

                string uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
                uniqueFileName = string.Join("_", uniqueFileName.Split(Path.GetInvalidFileNameChars()));
                string filePath = Path.Combine(uploadPath, uniqueFileName);


                File.WriteAllBytes(filePath, imageBytes);
                logger.Info("Image saved successfully to: {FilePath}", filePath);

                using (var context = new MindWeaveDBEntities1())
                {
                    var player = await context.Player.FirstOrDefaultAsync(p => p.username == username);
                    if (player == null)
                    {
                        logger.Warn("Upload Error: Player {Username} not found.", username);
                        return new UploadResultDto { success = false, message = "Uploading player not found." }; // TODO: Lang key
                    }

                    var newPuzzle = new Puzzles
                    {
                        image_path = uniqueFileName,
                        upload_date = DateTime.UtcNow,
                        player_id = player.idPlayer
                    };

                    context.Puzzles.Add(newPuzzle);
                    await context.SaveChangesAsync();
                    logger.Info("New puzzle record created with ID: {PuzzleId} for user {Username}", newPuzzle.puzzle_id, username);

                    return new UploadResultDto { success = true, message = "Image uploaded successfully!", newPuzzleId = newPuzzle.puzzle_id }; // TODO: Lang key
                }
            }
            catch (IOException ioEx)
            {
                logger.Error(ioEx, "I/O Error uploading image for {Username}", username);
                return new UploadResultDto { success = false, message = $"Server file error during upload: {ioEx.Message}" }; // TODO: Lang key
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Generic Error uploading image for {Username}", username);
                return new UploadResultDto { success = false, message = $"Server error during upload: {ex.Message}" }; // TODO: Lang key
            }
        }
    }
}