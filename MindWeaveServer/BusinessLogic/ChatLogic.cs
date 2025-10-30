// MindWeaveServer/BusinessLogic/ChatLogic.cs
using MindWeaveServer.Contracts.DataContracts.Chat;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using NLog; // ¡Añadir using para NLog!

namespace MindWeaveServer.BusinessLogic
{
    public class ChatLogic
    {
        // Obtener instancia del logger (NOMBRE CORREGIDO)
        private static readonly Logger logger = LogManager.GetCurrentClassLogger(); // <--- NOMBRE CORREGIDO

        // Diccionarios estáticos (sin cambios)
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>> lobbyChatUsers =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, IChatCallback>>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, List<ChatMessageDto>> lobbyChatHistory =
            new ConcurrentDictionary<string, List<ChatMessageDto>>(StringComparer.OrdinalIgnoreCase);

        // Constantes (sin cambios)
        private const int MAX_HISTORY_PER_LOBBY = 50;
        private const int MAX_MESSAGE_LENGTH = 200;

        // Constructor implícito (podrías añadir un log si fuera necesario aquí)
        // public ChatLogic() { logger.Info("ChatLogic instance created."); }


        // --- Métodos Públicos ---

        public async Task joinLobbyChat(string username, string lobbyId, IChatCallback userCallback)
        {
            logger.Info("joinLobbyChat called for User: {Username}, Lobby: {LobbyId}", username ?? "NULL", lobbyId ?? "NULL");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId) || userCallback == null)
            {
                logger.Warn("joinLobbyChat ignored: Invalid parameters (username, lobbyId, or callback is null/whitespace).");
                return; // Salir si los parámetros son inválidos
            }

            // Añadir o actualizar usuario en el lobby
            var usersInLobby = lobbyChatUsers.GetOrAdd(lobbyId, id => {
                logger.Debug("Creating new user list for lobby: {LobbyId}", id);
                return new ConcurrentDictionary<string, IChatCallback>(StringComparer.OrdinalIgnoreCase);
            });

            // Lógica AddOrUpdate (sin cambios, pero con logs internos para claridad)
            usersInLobby.AddOrUpdate(username, userCallback, (key, existingVal) =>
            {
                var existingComm = existingVal as ICommunicationObject;
                // Si el callback es diferente Y el existente está cerrado/fallido, reemplazar
                if (existingVal != userCallback && (existingComm == null || existingComm.State != CommunicationState.Opened))
                {
                    logger.Warn("Replacing existing non-opened chat callback for User: {Username} in Lobby: {LobbyId}", key, lobbyId);
                    return userCallback; // Devolver el nuevo
                }
                // Si no, mantener el existente (sea el mismo o uno diferente pero abierto)
                if (existingVal != userCallback)
                    logger.Debug("Keeping existing OPEN chat callback for User: {Username} in Lobby: {LobbyId}", key, lobbyId);
                else
                    logger.Debug("Updating existing chat callback (same instance) for User: {Username} in Lobby: {LobbyId}", key, lobbyId);

                return existingVal; // Mantener el existente
            });
            logger.Info("User {Username} added/updated in chat lobby {LobbyId}", username, lobbyId);


            // Enviar historial al usuario que se une
            if (lobbyChatHistory.TryGetValue(lobbyId, out var history))
            {
                List<ChatMessageDto> historySnapshot;
                lock (history) // El lock sigue siendo importante aquí
                {
                    historySnapshot = history.ToList(); // Crear copia dentro del lock
                }
                logger.Debug("Sending {Count} historical messages to User: {Username} for Lobby: {LobbyId}", historySnapshot.Count, username, lobbyId);

                foreach (var msg in historySnapshot)
                {
                    try
                    {
                        var commObject = userCallback as ICommunicationObject;
                        if (commObject != null && commObject.State == CommunicationState.Opened)
                        {
                            userCallback.receiveLobbyMessage(msg); // Enviar mensaje
                        }
                        else
                        {
                            // Reemplazar Console.WriteLine
                            logger.Warn("[ChatLogic JOIN] Callback channel for {Username} not open while sending history. Aborting history send. State: {State}", username, commObject?.State);
                            break; // Salir del bucle si el canal no está abierto
                        }
                    }
                    catch (Exception ex)
                    {
                        // Reemplazar Console.WriteLine
                        logger.Error(ex, "[ChatLogic JOIN] Exception sending history message to {Username} for Lobby: {LobbyId}. Continuing...", username, lobbyId);
                        // Considerar salir del bucle si el canal falla repetidamente
                    }
                }
                logger.Debug("Finished sending history to User: {Username}", username);
            }
            else
            {
                logger.Debug("No chat history found for Lobby: {LobbyId} to send to User: {Username}", lobbyId, username);
            }

            // Simular async si no hay operaciones async reales (opcional, para cumplir firma)
            await Task.CompletedTask;
        }

        public void leaveLobbyChat(string username, string lobbyId)
        {
            logger.Info("leaveLobbyChat called for User: {Username}, Lobby: {LobbyId}", username ?? "NULL", lobbyId ?? "NULL");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId))
            {
                logger.Warn("leaveLobbyChat ignored: Username or LobbyId is null/whitespace.");
                return;
            }

            if (lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                if (usersInLobby.TryRemove(username, out _))
                {
                    logger.Info("User {Username} successfully removed from chat lobby {LobbyId}", username, lobbyId);

                    // Si el lobby queda vacío, limpiar usuarios e historial
                    if (usersInLobby.IsEmpty)
                    {
                        logger.Info("Chat lobby {LobbyId} is now empty. Removing user list and history.", lobbyId);
                        if (lobbyChatUsers.TryRemove(lobbyId, out _))
                        {
                            if (lobbyChatHistory.TryRemove(lobbyId, out _))
                            {
                                logger.Debug("Successfully removed history for empty lobby {LobbyId}", lobbyId);
                            }
                            else
                            {
                                logger.Warn("Could not remove history for empty lobby {LobbyId} (might have been removed already).", lobbyId);
                            }
                        }
                        else
                        {
                            logger.Warn("Could not remove user list for empty lobby {LobbyId} (might have been removed already).", lobbyId);
                        }
                    }
                }
                else
                {
                    // Reemplazar Console.WriteLine
                    logger.Warn("[ChatLogic LEAVE] User '{Username}' was not found in lobby '{LobbyId}' during leave attempt.", username, lobbyId);
                }
            }
            else
            {
                // Reemplazar Console.WriteLine
                logger.Warn("[ChatLogic LEAVE] Lobby '{LobbyId}' not found during leave attempt for user '{Username}'.", lobbyId, username);
            }
        }

        public void processAndBroadcastMessage(string senderUsername, string lobbyId, string messageContent)
        {
            logger.Info("processAndBroadcastMessage called by User: {Username} in Lobby: {LobbyId}", senderUsername ?? "NULL", lobbyId ?? "NULL");

            if (string.IsNullOrWhiteSpace(senderUsername) || string.IsNullOrWhiteSpace(lobbyId) || string.IsNullOrWhiteSpace(messageContent))
            {
                // Reemplazar Console.WriteLine
                logger.Warn("[ChatLogic SEND FAILED] Invalid parameters for sending message (sender, lobby, or content is null/whitespace).");
                return;
            }

            // Truncar mensaje si excede el límite
            if (messageContent.Length > MAX_MESSAGE_LENGTH)
            {
                logger.Debug("Message from {Username} in lobby {LobbyId} exceeds max length ({MaxLength}). Truncating.", senderUsername, lobbyId, MAX_MESSAGE_LENGTH);
                messageContent = messageContent.Substring(0, MAX_MESSAGE_LENGTH) + "...";
            }

            var messageDto = new ChatMessageDto
            {
                senderUsername = senderUsername,
                content = messageContent,
                timestamp = DateTime.UtcNow // Usar UTC consistentemente
            };

            addMessageToHistory(lobbyId, messageDto); // Log dentro de este método
            broadcastMessage(lobbyId, messageDto); // Log dentro de este método
        }


        // --- Métodos Privados ---

        private void addMessageToHistory(string lobbyId, ChatMessageDto messageDto)
        {
            var history = lobbyChatHistory.GetOrAdd(lobbyId, id => {
                logger.Debug("Creating new chat history list for lobby: {LobbyId}", id);
                return new List<ChatMessageDto>();
            });

            int countAfterAdd = 0;
            lock (history) // Lock es crucial para modificar la List<>
            {
                history.Add(messageDto);
                // Limpiar historial si excede el máximo
                while (history.Count > MAX_HISTORY_PER_LOBBY)
                {
                    history.RemoveAt(0); // Quitar el más antiguo
                }
                countAfterAdd = history.Count;
            }
            // Reemplazar Console.WriteLine (loguear fuera del lock)
            logger.Debug("[ChatLogic HISTORY] Message from {SenderUsername} added to history for lobby '{LobbyId}'. New Count: {HistoryCount}", messageDto.senderUsername, lobbyId, countAfterAdd);
        }

        private void broadcastMessage(string lobbyId, ChatMessageDto messageDto)
        {
            logger.Debug("Broadcasting message from {SenderUsername} to lobby {LobbyId}", messageDto.senderUsername, lobbyId);

            if (lobbyChatUsers.TryGetValue(lobbyId, out var usersInLobby))
            {
                // Tomar snapshot de los usuarios actuales para evitar problemas de concurrencia al iterar y modificar
                var currentUsersSnapshot = usersInLobby.ToList(); // Copia KeyValuePair<string, IChatCallback>
                List<string> usersToRemove = new List<string>(); // Lista para marcar usuarios con canal fallido

                logger.Debug("Attempting to broadcast to {Count} users in lobby {LobbyId}", currentUsersSnapshot.Count, lobbyId);

                foreach (var userEntry in currentUsersSnapshot)
                {
                    string recipientUsername = userEntry.Key;
                    IChatCallback recipientCallback = userEntry.Value;

                    try
                    {
                        var commObject = recipientCallback as ICommunicationObject;
                        if (commObject != null && commObject.State == CommunicationState.Opened)
                        {
                            recipientCallback.receiveLobbyMessage(messageDto);
                            // Reemplazar Console.WriteLine con log de Debug
                            logger.Debug("  -> Sent chat message to {RecipientUsername} in lobby {LobbyId}", recipientUsername, lobbyId);
                        }
                        else
                        {
                            // Reemplazar Console.WriteLine
                            logger.Warn("  -> FAILED sending chat message to {RecipientUsername} (Channel State: {State}). Marking for removal.", recipientUsername, commObject?.State);
                            usersToRemove.Add(recipientUsername); // Marcar para quitar después
                        }
                    }
                    catch (Exception ex)
                    {
                        // Reemplazar Console.WriteLine
                        logger.Error(ex, "  -> EXCEPTION sending chat message to {RecipientUsername}. Marking for removal.", recipientUsername);
                        usersToRemove.Add(recipientUsername); // Marcar para quitar después
                    }
                }

                // Limpiar usuarios con canales fallidos DESPUÉS de iterar
                if (usersToRemove.Any())
                {
                    logger.Warn("Found {Count} users with failed channels during broadcast in lobby {LobbyId}. Removing them...", usersToRemove.Count, lobbyId);
                    foreach (var userToRemove in usersToRemove)
                    {
                        // Intentar quitar del diccionario principal (usersInLobby)
                        if (usersInLobby.TryRemove(userToRemove, out _))
                        {
                            // Reemplazar Console.WriteLine
                            logger.Info("[ChatLogic BROADCAST CLEANUP] Removed user {UserToRemove} from chat lobby {LobbyId} due to channel issue.", userToRemove, lobbyId);
                        }
                    }

                    // Verificar si el lobby quedó vacío después de la limpieza
                    if (usersInLobby.IsEmpty)
                    {
                        logger.Info("Chat lobby {LobbyId} became empty after broadcast cleanup. Removing lobby resources.", lobbyId);
                        if (lobbyChatUsers.TryRemove(lobbyId, out _))
                        {
                            if (lobbyChatHistory.TryRemove(lobbyId, out _))
                            {
                                // Reemplazar Console.WriteLine
                                logger.Debug("[ChatLogic CLEANUP] Lobby '{LobbyId}' chat resources released after broadcast cleanup (lobby empty).", lobbyId);
                            }
                            else { logger.Warn("Could not remove history for empty lobby {LobbyId} after broadcast cleanup.", lobbyId); }
                        }
                        else { logger.Warn("Could not remove user list for empty lobby {LobbyId} after broadcast cleanup.", lobbyId); }
                    }
                }
            }
            else
            {
                // Reemplazar Console.WriteLine
                logger.Warn("[ChatLogic BROADCAST WARNING] Lobby '{LobbyId}' not found for broadcast (might have been cleaned up).", lobbyId);
            }
        }
    }
}