using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Shared;
using NLog;
using System;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class MatchmakingLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ILobbyLifecycleService lifecycleService;
        private readonly ILobbyInteractionService interactionService;
        private readonly INotificationService notificationService;
        private readonly IGameStateManager gameStateManager;
        private readonly GameSessionManager gameSessionManager;
        private readonly IPlayerRepository playerRepository;
        private readonly IMatchmakingRepository matchmakingRepository;
        private readonly LobbyModerationManager moderationManager;

        private const int ID_REASON_PROFANITY = 2;
        public const int INVALID_PLAYER_ID = 0;
        public const string PROFANITY_REASON_TEXT = "Profanity";

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Dependencies are injected via DI container")]
        public MatchmakingLogic(
            ILobbyLifecycleService lifecycleService,
            ILobbyInteractionService interactionService,
            INotificationService notificationService,
            IGameStateManager gameStateManager,
            GameSessionManager gameSessionManager,
            IPlayerRepository playerRepository,
            IMatchmakingRepository matchmakingRepository,
            LobbyModerationManager moderationManager)
        {
            this.lifecycleService = lifecycleService;
            this.interactionService = interactionService;
            this.notificationService = notificationService;
            this.gameStateManager = gameStateManager;
            this.gameSessionManager = gameSessionManager;
            this.playerRepository = playerRepository;
            this.matchmakingRepository = matchmakingRepository;
            this.moderationManager = moderationManager;

            logger.Info("MatchmakingLogic Facade initialized.");
        }

        public async Task<LobbyCreationResultDto> createLobbyAsync(string hostUsername, LobbySettingsDto settings)
        {
            return await lifecycleService.createLobbyAsync(hostUsername, settings);
        }

        public async Task joinLobbyAsync(string username, string lobbyCode, IMatchmakingCallback callback)
        {
            var context = new LobbyActionContext { RequesterUsername = username, LobbyCode = lobbyCode };
            await lifecycleService.joinLobbyAsync(context, callback);
        }

        public async Task leaveLobbyAsync(string username, string lobbyCode)
        {
            var context = new LobbyActionContext { RequesterUsername = username, LobbyCode = lobbyCode };
            await lifecycleService.leaveLobbyAsync(context);
        }

        public void handleUserDisconnect(string username)
        {
            lifecycleService.handleUserDisconnect(username);
        }

        public async Task<GuestJoinResultDto> joinLobbyAsGuestAsync(GuestJoinRequestDto joinRequest, IMatchmakingCallback callback)
        {
            return await lifecycleService.joinLobbyAsGuestAsync(joinRequest, callback);
        }

        public async Task startGameAsync(string hostUsername, string lobbyCode)
        {
            var context = new LobbyActionContext { RequesterUsername = hostUsername, LobbyCode = lobbyCode };
            await interactionService.startGameAsync(context);
        }

        public async Task kickPlayerAsync(string hostUsername, string targetUsername, string lobbyCode)
        {
            var context = new LobbyActionContext
            {
                RequesterUsername = hostUsername,
                TargetUsername = targetUsername,
                LobbyCode = lobbyCode
            };
            await interactionService.kickPlayerAsync(context);
        }

        public async Task inviteToLobbyAsync(string inviterUsername, string invitedUsername, string lobbyCode)
        {
            var context = new LobbyActionContext
            {
                RequesterUsername = inviterUsername,
                TargetUsername = invitedUsername,
                LobbyCode = lobbyCode
            };
            await interactionService.invitePlayerAsync(context);
        }

        public async Task changeDifficultyAsync(string hostUsername, string lobbyId, int newDifficultyId)
        {
            var context = new LobbyActionContext { RequesterUsername = hostUsername, LobbyCode = lobbyId };
            await interactionService.changeDifficultyAsync(context, newDifficultyId);
        }

        public async Task inviteGuestByEmailAsync(GuestInvitationDto invitationData)
        {
            if (invitationData == null)
            {
                return;
            }
            await interactionService.inviteGuestByEmailAsync(invitationData.InviterUsername, invitationData.LobbyCode, invitationData.GuestEmail);
        }

        public void registerCallback(string username, IMatchmakingCallback callback)
        {
            if (string.IsNullOrWhiteSpace(username) || callback == null)
            {
                return;
            }
            gameStateManager.MatchmakingCallbacks.AddOrUpdate(username, callback, (k, v) => callback);
        }

        public async Task expelPlayerAsync(string lobbyCode, string username, string reasonText)
        {
            logger.Info("ExpelPlayerAsync (System) for lobby: {LobbyCode}. Reason: {Reason}.", lobbyCode, reasonText);

            if (!gameStateManager.ActiveLobbies.TryGetValue(lobbyCode, out var lobbyState))
            {
                return;
            }

            if (isHostBeingExpelled(lobbyState, username))
            {
                await handleHostExpulsion(lobbyCode, username, lobbyState, reasonText);
                return;
            }

            await handlePlayerExpulsion(lobbyCode, username, reasonText);
        }

        private static bool isHostBeingExpelled(LobbyStateDto lobbyState, string username)
        {
            return lobbyState.HostUsername == username;
        }

        private Task handleHostExpulsion(string lobbyCode, string username, LobbyStateDto lobbyState, string reasonText)
        {
            logger.Info("Host expelled from lobby by System (Profanity). Destroying lobby: {LobbyCode}.", lobbyCode);

            moderationManager.banUser(lobbyCode, username, reasonText);
            notificationService.broadcastLobbyDestroyed(lobbyState, MessageCodes.NOTIFY_HOST_LEFT);

            gameStateManager.ActiveLobbies.TryRemove(lobbyCode, out _);
            gameStateManager.GuestUsernamesInLobby.TryRemove(lobbyCode, out _);
            return Task.CompletedTask;
        }

        private async Task handlePlayerExpulsion(string lobbyCode, string username, string reasonText)
        {
            int hostId = await getHostIdAsync(lobbyCode);

            moderationManager.banUser(lobbyCode, username, reasonText);
            logger.Info("User banned from lobby: {LobbyCode}. Reason: {Reason}.", lobbyCode, reasonText);

            var session = gameSessionManager.getSession(lobbyCode);

            if (session != null)
            {
                await expelFromActiveSessionAsync(session, username, hostId);
            }
            else
            {
                await expelFromLobbyStateAsync(lobbyCode, username, hostId);
            }
        }

        private async Task<int> getHostIdAsync(string lobbyCode)
        {
            if (!gameStateManager.ActiveLobbies.TryGetValue(lobbyCode, out var state))
            {
                return INVALID_PLAYER_ID;
            }

            var hostPlayer = await playerRepository.getPlayerByUsernameAsync(state.HostUsername);
            return hostPlayer?.idPlayer ?? INVALID_PLAYER_ID;
        }

        private async Task expelFromActiveSessionAsync(GameSession session, string username, int hostId)
        {
            var player = await playerRepository.getPlayerByUsernameAsync(username);
            if (player != null)
            {
                await session.kickPlayerAsync(player.idPlayer, ID_REASON_PROFANITY, hostId);
            }
            else
            {
                logger.Warn("ExpelFromActiveSession: Player not found in DB, cannot kick from session.");
            }
        }

        private async Task expelFromLobbyStateAsync(string lobbyCode, string username, int hostId)
        {
            await registerLobbyExpulsionInDbAsync(lobbyCode, username, hostId);
            notificationService.notifyKicked(username, MessageCodes.NOTIFY_KICKED_PROFANITY);
            updateLobbyStateAndNotify(lobbyCode, username);
        }

        private async Task registerLobbyExpulsionInDbAsync(string lobbyCode, string username, int hostId)
        {
            var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
            var player = await playerRepository.getPlayerByUsernameAsync(username);

            if (match == null || player == null)
            {
                logger.Warn("RegisterLobbyExpulsionInDb: Match or player not found.");
                return;
            }

            var dto = new ExpulsionDto
            {
                MatchId = match.matches_id,
                PlayerId = player.idPlayer,
                ReasonId = ID_REASON_PROFANITY,
                HostPlayerId = hostId
            };
            await matchmakingRepository.registerExpulsionAsync(dto);
        }

        private void updateLobbyStateAndNotify(string lobbyCode, string username)
        {
            if (!gameStateManager.ActiveLobbies.TryGetValue(lobbyCode, out var lobbyState))
            {
                return;
            }

            lock (lobbyState)
            {
                lobbyState.Players.RemoveAll(p => p.Equals(username, StringComparison.OrdinalIgnoreCase));
            }
            notificationService.broadcastLobbyState(lobbyState);
        }
    }
}