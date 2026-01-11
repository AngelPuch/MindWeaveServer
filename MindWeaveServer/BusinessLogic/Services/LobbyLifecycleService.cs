using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Utilities;
using NLog;
using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic.Services
{
    public class LobbyLifecycleService : ILobbyLifecycleService
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IGameStateManager gameStateManager;
        private readonly ILobbyValidationService validationService;
        private readonly INotificationService notificationService;
        private readonly LobbyModerationManager moderationManager;

        private readonly IMatchmakingRepository matchmakingRepository;
        private readonly IPlayerRepository playerRepository;
        private readonly IPuzzleRepository puzzleRepository;
        private readonly IGuestInvitationRepository invitationRepository;

        private const int MAX_LOBBY_CODE_ATTEMPTS = 10;
        private const int MATCH_STATUS_WAITING = 1;
        private const int MATCH_STATUS_CANCELED = 4;
        private const int DEFAULT_PUZZLE_ID = 4;
        private const int DEFAULT_DIFFICULTY_ID = 1;
        private const int MAX_GUEST_NAME_COUNTER = 99;
        private const int MAX_GUEST_USERNAME_LENGTH = 16;

        public LobbyLifecycleService(
            IGameStateManager gameStateManager,
            ILobbyValidationService validationService,
            INotificationService notificationService,
            LobbyModerationManager moderationManager,
            IMatchmakingRepository matchmakingRepository,
            IPlayerRepository playerRepository,
            IPuzzleRepository puzzleRepository,
            IGuestInvitationRepository invitationRepository)
        {
            this.gameStateManager = gameStateManager;
            this.validationService = validationService;
            this.notificationService = notificationService;
            this.moderationManager = moderationManager;
            this.matchmakingRepository = matchmakingRepository;
            this.playerRepository = playerRepository;
            this.puzzleRepository = puzzleRepository;
            this.invitationRepository = invitationRepository;
        }

        public async Task<LobbyCreationResultDto> createLobbyAsync(string hostUsername, LobbySettingsDto settings)
        {
            var validation = validationService.canCreateLobby(hostUsername);
            if (!validation.IsSuccess)
            {
                return new LobbyCreationResultDto { Success = false, MessageCode = validation.MessageCode };
            }

            var hostPlayer = await playerRepository.getPlayerByUsernameAsync(hostUsername);

            if (hostPlayer == null)
            {
                return new LobbyCreationResultDto { Success = false, MessageCode = MessageCodes.AUTH_USER_NOT_FOUND };
            }

            Matches newMatch = await tryCreateUniqueMatchRecordAsync(hostPlayer, settings);
            if (newMatch == null)
            {
                return new LobbyCreationResultDto { Success = false, MessageCode = MessageCodes.MATCH_LOBBY_CREATION_FAILED };
            }

            moderationManager.initializeLobby(newMatch.lobby_code);
            var (puzzleBytes, puzzlePath) = await resolvePuzzleResourcesAsync(newMatch.puzzle_id, settings.CustomPuzzleImage);

            settings.CustomPuzzleImage = puzzleBytes;

            var lobbyState = new LobbyStateDto
            {
                LobbyId = newMatch.lobby_code,
                HostUsername = hostUsername,
                Players = new List<string> { hostUsername },
                CurrentSettingsDto = settings,
                PuzzleImagePath = puzzlePath
            };

            if (gameStateManager.ActiveLobbies.TryAdd(newMatch.lobby_code, lobbyState))
            {
                return new LobbyCreationResultDto
                {
                    Success = true,
                    MessageCode = MessageCodes.MATCH_LOBBY_CREATED,
                    LobbyCode = newMatch.lobby_code,
                    InitialLobbyState = lobbyState
                };
            }

            return new LobbyCreationResultDto { Success = false, MessageCode = MessageCodes.MATCH_LOBBY_CREATION_FAILED };
        }

        public async Task joinLobbyAsync(LobbyActionContext context, IMatchmakingCallback callback)
        {
            registerMatchmakingCallback(context.RequesterUsername, callback);

            gameStateManager.ActiveLobbies.TryGetValue(context.LobbyCode, out var lobby);

            var validation = validationService.canJoinLobby(lobby, context.RequesterUsername, context.LobbyCode);
            if (!validation.IsSuccess)
            {
                notificationService.notifyLobbyCreationFailed(context.RequesterUsername, validation.MessageCode);
                return;
            }

            bool addedToMemory;
            lock (lobby)
            {
                if (lobby.Players.Contains(context.RequesterUsername, StringComparer.OrdinalIgnoreCase))
                {
                    addedToMemory = false;
                }
                else
                {
                    lobby.Players.Add(context.RequesterUsername);
                    addedToMemory = true;
                }
            }

            if (addedToMemory)
            {
                try
                {
                    await tryAddParticipantToDbAsync(context.LobbyCode, context.RequesterUsername);
                }
                catch (DbUpdateException)
                {
                    handleJoinFailure(lobby, context.RequesterUsername);
                    throw;
                }
            }

            notificationService.broadcastLobbyState(lobby);
        }

        public async Task<GuestJoinResultDto> joinLobbyAsGuestAsync(GuestJoinRequestDto request, IMatchmakingCallback callback)
        {
            var (isValid, invitation, lobby, error) = await validateGuestJoinAsync(request);
            if (!isValid) return error;

            string finalGuestUsername;

            lock (lobby)
            {
                if (lobby.Players.Count >= 4)
                {
                    return new GuestJoinResultDto { Success = false, MessageCode = MessageCodes.MATCH_LOBBY_FULL };
                }

                finalGuestUsername = generateUniqueGuestName(request.LobbyCode, request.DesiredGuestUsername);
                if (finalGuestUsername == null)
                {
                    return new GuestJoinResultDto { Success = false, MessageCode = MessageCodes.MATCH_GUEST_NAME_GENERATION_FAILED };
                }

                lobby.Players.Add(finalGuestUsername);

                var guests = gameStateManager.GuestUsernamesInLobby.GetOrAdd(request.LobbyCode, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                guests.Add(finalGuestUsername);
            }

            registerMatchmakingCallback(finalGuestUsername, callback);
            await markInvitationUsedAsync(invitation);

            notificationService.broadcastLobbyState(lobby);

            return new GuestJoinResultDto
            {
                Success = true,
                MessageCode = MessageCodes.MATCH_GUEST_JOIN_SUCCESS,
                AssignedGuestUsername = finalGuestUsername,
                InitialLobbyState = lobby,
                PlayerId = -Math.Abs(finalGuestUsername.GetHashCode())
            };
        }

        public async Task leaveLobbyAsync(LobbyActionContext context)
        {
            if (!gameStateManager.ActiveLobbies.TryGetValue(context.LobbyCode, out var lobby))
            {
                removeMatchmakingCallback(context.RequesterUsername);
                return;
            }

            bool wasGuest = isGuest(context.LobbyCode, context.RequesterUsername);
            bool isLobbyClosed = false;
            List<string> remainingPlayers = null;

            lock (lobby)
            {
                lobby.Players.RemoveAll(p => p.Equals(context.RequesterUsername, StringComparison.OrdinalIgnoreCase));

                bool isHost = lobby.HostUsername.Equals(context.RequesterUsername, StringComparison.OrdinalIgnoreCase);
                if (isHost || lobby.Players.Count == 0)
                {
                    isLobbyClosed = true;
                    remainingPlayers = lobby.Players.ToList();
                }

                if (wasGuest)
                {
                    removeGuestTracking(context.LobbyCode, context.RequesterUsername);
                }
            }

            await SyncDbOnLeaveAsync(context.LobbyCode, context.RequesterUsername, wasGuest, isLobbyClosed);

            removeMatchmakingCallback(context.RequesterUsername);

            if (isLobbyClosed)
            {
                closeLobby(context.LobbyCode, remainingPlayers);
            }
            else
            {
                notifyOthersPlayerLeft(lobby, context.RequesterUsername);
                notificationService.broadcastLobbyState(lobby);
            }
        }

        private void notifyOthersPlayerLeft(LobbyStateDto lobby, string usernameLeft)
        {
            foreach (var player in lobby.Players)
            {
                if (gameStateManager.MatchmakingCallbacks.TryGetValue(player, out var callback))
                {
                    try
                    {
                        callback.onPlayerLeftMatch(usernameLeft);
                    }
                    catch (CommunicationException ex)
                    {
                        logger.Warn(ex, "Failed to notify {0} that {1} left.", player, usernameLeft);
                    }
                    catch (ObjectDisposedException) { }
                    catch (TimeoutException) { }
                }
            }
        }

        public void handleUserDisconnect(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return;

            var lobbiesToLeave = gameStateManager.ActiveLobbies
                .Where(kvp =>
                {
                    lock (kvp.Value) { return kvp.Value.Players.Contains(username, StringComparer.OrdinalIgnoreCase); }
                })
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var lobbyCode in lobbiesToLeave)
            {
                Task.Run(() => leaveLobbyAsync(new LobbyActionContext { LobbyCode = lobbyCode, RequesterUsername = username }));
            }

            removeMatchmakingCallback(username);
        }

        private async Task<Matches> tryCreateUniqueMatchRecordAsync(Player host, LobbySettingsDto settings)
        {
            for (int i = 0; i < MAX_LOBBY_CODE_ATTEMPTS; i++)
            {
                string code = LobbyCodeGenerator.generateUniqueCode();
                if (gameStateManager.ActiveLobbies.ContainsKey(code) || await matchmakingRepository.doesLobbyCodeExistAsync(code))
                    continue;

                var match = new Matches
                {
                    creation_time = DateTime.UtcNow,
                    match_status_id = MATCH_STATUS_WAITING,
                    puzzle_id = settings.PreloadedPuzzleId ?? DEFAULT_PUZZLE_ID,
                    difficulty_id = settings.DifficultyId > 0 ? settings.DifficultyId : DEFAULT_DIFFICULTY_ID,
                    lobby_code = code
                };

                try
                {
                    var created = await matchmakingRepository.createMatchAsync(match);
                    await matchmakingRepository.addParticipantAsync(new MatchParticipants
                    { match_id = created.matches_id, player_id = host.idPlayer, is_host = true });
                    return created;
                }
                catch (DbUpdateException)
                {
                    logger.Warn("Lobby code collision detected for {0}. Retrying.", code);
                }
            }
            return null;
        }

        private void handleJoinFailure(LobbyStateDto lobby, string username)
        {
            revertPlayerFromMemory(lobby, username);
            notificationService.notifyLobbyCreationFailed(username, MessageCodes.MATCH_JOIN_ERROR_DATA);
        }

        private async Task<(byte[] bytes, string path)> resolvePuzzleResourcesAsync(int puzzleId, byte[] customBytes)
        {
            if (customBytes != null && customBytes.Length > 0) return (customBytes, null);

            var puzzle = await puzzleRepository.getPuzzleByIdAsync(puzzleId);
            string path = puzzle?.image_path ?? "puzzleDefault.png";

            if (!path.StartsWith("puzzleDefault"))
            {
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UploadedPuzzles", path);
                if (File.Exists(fullPath))
                {
                    return (File.ReadAllBytes(fullPath), path);
                }
            }
            return (null, path);
        }

        private async Task tryAddParticipantToDbAsync(string lobbyCode, string username)
        {
            var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
            var player = await playerRepository.getPlayerByUsernameAsync(username);

            if (match != null && player != null && match.match_status_id == MATCH_STATUS_WAITING)
            {
                var exists = await matchmakingRepository.getParticipantAsync(match.matches_id, player.idPlayer);
                if (exists == null)
                {
                    await matchmakingRepository.addParticipantAsync(new MatchParticipants
                    {
                        match_id = match.matches_id,
                        player_id = player.idPlayer,
                        is_host = false
                    });
                }
            }
        }

        private async Task SyncDbOnLeaveAsync(string lobbyCode, string username, bool wasGuest, bool isLobbyClosed)
        {
            var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
            if (match == null) return;

            if (!wasGuest)
            {
                var player = await playerRepository.getPlayerByUsernameAsync(username);
                if (player != null)
                {
                    var participant = await matchmakingRepository.getParticipantAsync(match.matches_id, player.idPlayer);
                    if (participant != null) await matchmakingRepository.removeParticipantAsync(participant);
                }
            }

            if (isLobbyClosed && match.match_status_id == MATCH_STATUS_WAITING)
            {
                matchmakingRepository.updateMatchStatus(match, MATCH_STATUS_CANCELED);
            }
        }

        private void closeLobby(string lobbyCode, List<string> playersToKick)
        {
            gameStateManager.ActiveLobbies.TryRemove(lobbyCode, out _);
            gameStateManager.GuestUsernamesInLobby.TryRemove(lobbyCode, out _);

            if (playersToKick != null)
            {
                foreach (var p in playersToKick)
                {
                    notificationService.notifyKicked(p, MessageCodes.NOTIFY_HOST_LEFT);
                    removeMatchmakingCallback(p);
                }
            }
        }

        private void revertPlayerFromMemory(LobbyStateDto lobby, string username)
        {
            lock (lobby)
            {
                lobby.Players.Remove(username);
            }
            if (lobby.Players.Count == 0)
            {
                gameStateManager.ActiveLobbies.TryRemove(lobby.LobbyId, out _);
            }
        }

        private async Task<(bool valid, GuestInvitations inv, LobbyStateDto lobby, GuestJoinResultDto error)> validateGuestJoinAsync(GuestJoinRequestDto req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.LobbyCode) || string.IsNullOrWhiteSpace(req.GuestEmail))
            {
                return (false, null, null, new GuestJoinResultDto { Success = false, MessageCode = MessageCodes.VALIDATION_FIELDS_REQUIRED });
            }

            if (!gameStateManager.ActiveLobbies.TryGetValue(req.LobbyCode, out var lobby))
            {
                return (false, null, null, new GuestJoinResultDto { Success = false, MessageCode = MessageCodes.MATCH_LOBBY_NOT_FOUND });
            }

            var match = await matchmakingRepository.getMatchByLobbyCodeAsync(req.LobbyCode);

            if (match == null || match.match_status_id != MATCH_STATUS_WAITING)
                return (false, null, null, new GuestJoinResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.MATCH_LOBBY_NOT_FOUND,
                    MessageParams = new[] { req.LobbyCode }
                });

            var invitation = await invitationRepository.findValidInvitationAsync(match.matches_id, req.GuestEmail.Trim().ToLowerInvariant());
            if (invitation == null)
            {
                return (false, null, null, new GuestJoinResultDto { Success = false, MessageCode = MessageCodes.MATCH_GUEST_INVITE_INVALID });
            }
            return (true, invitation, lobby, null);
        }

        private string generateUniqueGuestName(string lobbyCode, string baseName)
        {
            var guests = gameStateManager.GuestUsernamesInLobby.GetOrAdd(lobbyCode, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            string name = baseName.Trim();
            if (name.Length > MAX_GUEST_USERNAME_LENGTH) name = name.Substring(0, MAX_GUEST_USERNAME_LENGTH);

            gameStateManager.ActiveLobbies.TryGetValue(lobbyCode, out var lobby);
            var allPlayers = lobby?.Players ?? new List<string>();

            int counter = 1;
            string candidate = name;
            while ((guests.Contains(candidate) || allPlayers.Contains(candidate, StringComparer.OrdinalIgnoreCase)) && counter <= MAX_GUEST_NAME_COUNTER)
            {
                string suffix = counter.ToString();
                int availLen = MAX_GUEST_USERNAME_LENGTH - suffix.Length;
                candidate = (name.Length > availLen ? name.Substring(0, availLen) : name) + suffix;
                counter++;
            }

            return counter > MAX_GUEST_NAME_COUNTER ? null : candidate;
        }

        private async Task markInvitationUsedAsync(GuestInvitations inv)
        {
            await invitationRepository.markInvitationAsUsedAsync(inv);
        }

        private bool isGuest(string lobbyCode, string username)
        {
            return gameStateManager.GuestUsernamesInLobby.TryGetValue(lobbyCode, out var guests) && guests.Contains(username);
        }

        private void removeGuestTracking(string lobbyCode, string username)
        {
            if (gameStateManager.GuestUsernamesInLobby.TryGetValue(lobbyCode, out var guests))
            {
                guests.Remove(username);
                if (guests.Count == 0) gameStateManager.GuestUsernamesInLobby.TryRemove(lobbyCode, out _);
            }
        }

        private void registerMatchmakingCallback(string username, IMatchmakingCallback cb)
        {
            gameStateManager.MatchmakingCallbacks.AddOrUpdate(username, cb, (k, v) => cb);
        }

        private void removeMatchmakingCallback(string username)
        {
            gameStateManager.MatchmakingCallbacks.TryRemove(username, out _);
        }
    }
}