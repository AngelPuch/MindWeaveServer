// MindWeaveServer/Services/PuzzleManagerService.cs
using MindWeaveServer.Contracts.DataContracts; // Para DTOs
// Si creaste subcarpeta 'Puzzle' para DTOs: using MindWeaveServer.Contracts.DataContracts.Puzzle;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using NLog; // ¡Añadir using para NLog!
// QUITA: using System.Web.Hosting;

namespace MindWeaveServer.Services // Asegúrate que el namespace sea correcto
{
    // Añadir ServiceBehavior si es necesario (probablemente PerCall o PerSession)
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class PuzzleManagerService : IPuzzleManager
    {
        // Obtener instancia del logger (NOMBRE CORREGIDO)
        private static readonly Logger logger = LogManager.GetCurrentClassLogger(); // <--- NOMBRE CORREGIDO

        // Ruta relativa a la carpeta de ejecución del servidor
        private readonly string UPLOAD_FOLDER_NAME = "UploadedPuzzles";
        private string getUploadFolderPath()
        {
            // Combina la ruta base del servidor con el nombre de la carpeta
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UPLOAD_FOLDER_NAME);
        }


        public async Task<List<PuzzleInfoDto>> getAvailablePuzzlesAsync()
        {
            logger.Info("getAvailablePuzzlesAsync request received."); // <--- LOG AÑADIDO
            try
            {
                using (var context = new MindWeaveDBEntities1())
                {
                    // PASO 1: Traer solo los datos necesarios de la BD
                    var puzzlesFromDb = await context.Puzzles
                                             .OrderBy(p => p.puzzle_id)
                                             .Select(p => new { // Selecciona un tipo anónimo temporalmente
                                                 Id = p.puzzle_id,
                                                 Path = p.image_path
                                             })
                                             .ToListAsync(); // Ejecuta la consulta SQL aquí

                    // PASO 2: Mapear a PuzzleInfoDto EN MEMORIA, calculando el nombre ahora
                    var puzzles = puzzlesFromDb.Select(p => new PuzzleInfoDto
                    {
                        puzzleId = p.Id,
                        imagePath = p.Path,
                        // Ahora SÍ podemos usar Path.GetFileNameWithoutExtension
                        name = Path.GetFileNameWithoutExtension(p.Path ?? "Puzzle")
                    }).ToList(); // Convierte a lista de DTOs

                    // Reemplazar Console.WriteLine
                    logger.Info("getAvailablePuzzlesAsync: Found {Count} puzzles in DB.", puzzles.Count);
                    return puzzles;
                }
            }
            catch (Exception ex)
            {
                // Reemplazar Console.WriteLine
                logger.Error(ex, "Error getting available puzzles.");
                return new List<PuzzleInfoDto>();
            }
        }

        public async Task<UploadResultDto> uploadPuzzleImageAsync(string username, byte[] imageBytes, string fileName)
        {
            logger.Info("uploadPuzzleImageAsync attempt by user: {Username}, fileName: {FileName}", username ?? "NULL", fileName ?? "NULL"); // <--- LOG AÑADIDO

            if (imageBytes == null || imageBytes.Length == 0 || string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("uploadPuzzleImageAsync failed for {Username}: Invalid data provided (null/empty bytes, fileName, or username).", username ?? "NULL");
                return new UploadResultDto { success = false, message = "Invalid data provided for upload." }; // TODO: Lang key
            }

            string uploadPath = getUploadFolderPath(); // Obtiene la ruta de subida

            try
            {
                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                    // Reemplazar Console.WriteLine
                    logger.Info("Created upload directory: {UploadPath}", uploadPath);
                }

                string uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
                // Asegura que no haya caracteres inválidos en el nombre final (aunque Guid + GetFileName suele ser seguro)
                uniqueFileName = string.Join("_", uniqueFileName.Split(Path.GetInvalidFileNameChars()));
                string filePath = Path.Combine(uploadPath, uniqueFileName);


                File.WriteAllBytes(filePath, imageBytes);
                // Reemplazar Console.WriteLine
                logger.Info("Image saved successfully to: {FilePath}", filePath);

                using (var context = new MindWeaveDBEntities1())
                {
                    var player = await context.Player.FirstOrDefaultAsync(p => p.username == username);
                    if (player == null)
                    {
                        // Reemplazar Console.WriteLine
                        logger.Warn("Upload Error: Player {Username} not found.", username);
                        // Considerar borrar el archivo guardado si el jugador no existe?
                        // File.Delete(filePath);
                        return new UploadResultDto { success = false, message = "Uploading player not found." }; // TODO: Lang key
                    }

                    var newPuzzle = new Puzzles
                    {
                        // Guarda solo el nombre único del archivo. El cliente/servidor reconstruirá la ruta completa si es necesario.
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
            // Captura específicamente errores de I/O
            catch (IOException ioEx)
            {
                // Reemplazar Console.WriteLine
                logger.Error(ioEx, "I/O Error uploading image for {Username}", username ?? "NULL");
                return new UploadResultDto { success = false, message = $"Server file error during upload: {ioEx.Message}" }; // TODO: Lang key
            }
            catch (Exception ex)
            {
                // Reemplazar Console.WriteLine
                logger.Error(ex, "Generic Error uploading image for {Username}", username ?? "NULL");
                return new UploadResultDto { success = false, message = $"Server error during upload: {ex.Message}" }; // TODO: Lang key
            }
        }
    }
}