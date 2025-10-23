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
// QUITA: using System.Web.Hosting;

namespace MindWeaveServer.Services // Asegúrate que el namespace sea correcto
{
    // Añadir ServiceBehavior si es necesario (probablemente PerCall o PerSession)
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class PuzzleManagerService : IPuzzleManager
    {
        // Ruta relativa a la carpeta de ejecución del servidor
        private readonly string UPLOAD_FOLDER_NAME = "UploadedPuzzles";
        private string getUploadFolderPath()
        {
            // Combina la ruta base del servidor con el nombre de la carpeta
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UPLOAD_FOLDER_NAME);
        }


        public async Task<List<PuzzleInfoDto>> getAvailablePuzzlesAsync()
        {
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

                    Console.WriteLine($"getAvailablePuzzlesAsync: Found {puzzles.Count} puzzles in DB."); // Log para confirmar
                    return puzzles;
                }
            }
            catch (Exception ex)
            {
                // Loguea el error completo para más detalles
                Console.WriteLine($"Error getting puzzles: {ex.ToString()}");
                return new List<PuzzleInfoDto>();
            }
        }

        public async Task<UploadResultDto> uploadPuzzleImageAsync(string username, byte[] imageBytes, string fileName)
        {
            if (imageBytes == null || imageBytes.Length == 0 || string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(username))
            {
                return new UploadResultDto { success = false, message = "Invalid data provided for upload." }; // TODO: Lang key
            }

            string uploadPath = getUploadFolderPath(); // Obtiene la ruta de subida

            try
            {
                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                    Console.WriteLine($"Created upload directory: {uploadPath}");
                }

                string uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
                // Asegura que no haya caracteres inválidos en el nombre final (aunque Guid + GetFileName suele ser seguro)
                uniqueFileName = string.Join("_", uniqueFileName.Split(Path.GetInvalidFileNameChars()));
                string filePath = Path.Combine(uploadPath, uniqueFileName);


                File.WriteAllBytes(filePath, imageBytes);
                Console.WriteLine($"Image saved to: {filePath}");

                using (var context = new MindWeaveDBEntities1())
                {
                    var player = await context.Player.FirstOrDefaultAsync(p => p.username == username);
                    if (player == null)
                    {
                        Console.WriteLine($"Upload Error: Player {username} not found.");
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
                    Console.WriteLine($"New puzzle record created with ID: {newPuzzle.puzzle_id}");

                    return new UploadResultDto { success = true, message = "Image uploaded successfully!", newPuzzleId = newPuzzle.puzzle_id }; // TODO: Lang key
                }
            }
            // Captura específicamente errores de I/O
            catch (IOException ioEx)
            {
                Console.WriteLine($"I/O Error uploading image for {username}: {ioEx.ToString()}");
                return new UploadResultDto { success = false, message = $"Server file error during upload: {ioEx.Message}" }; // TODO: Lang key
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Generic Error uploading image for {username}: {ex.ToString()}");
                return new UploadResultDto { success = false, message = $"Server error during upload: {ex.Message}" }; // TODO: Lang key
            }
        }
    }
}