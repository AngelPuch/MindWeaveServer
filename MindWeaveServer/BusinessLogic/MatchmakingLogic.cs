using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using NLog;
using System;
using System.Threading.Tasks;
using Autofac.Features.OwnedInstances;

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
        private readonly Func<Owned<IPlayerRepository>> playerFactory;
        private readonly Func<Owned<IMatchmakingRepository>> matchmakingFactory;

        private const int ID_REASON_HOST_DECISION = 1;
        private const int ID_REASON_PROFANITY = 2;
        public const int INVALID_PLAYER_ID = 0;
        public const string PROFANITY_REASON_TEXT = "Profanity";


        public MatchmakingLogic(
            ILobbyLifecycleService lifecycleService,
            ILobbyInteractionService interactionService,
            INotificationService notificationService,
            IGameStateManager gameStateManager,
            GameSessionManager gameSessionManager,
            Func<Owned<IPlayerRepository>> playerFactory,
            Func<Owned<IMatchmakingRepository>> matchmakingFactory)
        {
            this.lifecycleService = lifecycleService;
            this.interactionService = interactionService;
            this.notificationService = notificationService;
            this.gameStateManager = gameStateManager;
            this.gameSessionManager = gameSessionManager;
            this.playerFactory = playerFactory;
            this.matchmakingFactory = matchmakingFactory;

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
            if (invitationData == null) return;
            await interactionService.inviteGuestByEmailAsync(invitationData.InviterUsername, invitationData.LobbyCode, invitationData.GuestEmail);
        }

        public void registerCallback(string username, IMatchmakingCallback callback)
        {
            if (string.IsNullOrWhiteSpace(username) || callback == null) return;
            gameStateManager.MatchmakingCallbacks.AddOrUpdate(username, callback, (k, v) => callback);
        }

        public async Task expelPlayerAsync(string lobbyCode, string username, string reasonText)
        {
            logger.Info("ExpelPlayerAsync (System) for {0} in {1}. Reason: {2}", username, lobbyCode, reasonText);

            var (reasonId, kickMessage) = determineExpulsionDetails(reasonText);
            int hostId = await getHostIdAsync(lobbyCode);

            var session = gameSessionManager.getSession(lobbyCode);

            if (session != null)
            {
                await expelFromActiveSessionAsync(session, username, reasonId, hostId);
            }
            else
            {
                await expelFromLobbyStateAsync(lobbyCode, username, reasonId, hostId, kickMessage);
            }
        }

        private static (int reasonId, string message) determineExpulsionDetails(string reasonText)
        {
            int reasonId = (reasonText == PROFANITY_REASON_TEXT) ? ID_REASON_PROFANITY : ID_REASON_HOST_DECISION;
            string message = (reasonId == ID_REASON_PROFANITY) ? Lang.KickMessageProfanity : Lang.KickedByHost;
            return (reasonId, message);
        }

        private async Task<int> getHostIdAsync(string lobbyCode)
        {
            if (gameStateManager.ActiveLobbies.TryGetValue(lobbyCode, out var state))
            {
                using (var scope = playerFactory())
                {
                    var hostP = await scope.Value.getPlayerByUsernameAsync(state.HostUsername);
                    return hostP?.idPlayer ?? INVALID_PLAYER_ID;
                }
            }
            return INVALID_PLAYER_ID;
        }
        private async Task expelFromActiveSessionAsync(GameSession session, string username, int reasonId, int hostId)
        {
            using (var scope = playerFactory())
            {
                var player = await scope.Value.getPlayerByUsernameAsync(username);
                if (player != null)
                {
                    await session.kickPlayerAsync(player.idPlayer, reasonId, hostId);
                }
                else
                {
                    logger.Warn("ExpelFromActiveSession: Player {0} not found in DB, cannot kick from session.", username);
                }
            }
        }

        private async Task expelFromLobbyStateAsync(string lobbyCode, string username, int reasonId, int hostId, string kickMessage)
        {
            await registerLobbyExpulsionInDbAsync(lobbyCode, username, reasonId, hostId);

            notificationService.notifyKicked(username, kickMessage);

            updateLobbyStateAndNotify(lobbyCode, username);
        }

        private async Task registerLobbyExpulsionInDbAsync(string lobbyCode, string username, int reasonId, int hostId)
        {
            using (var matchScope = matchmakingFactory())
            using (var playerScope = playerFactory())
            {
                var match = await matchScope.Value.getMatchByLobbyCodeAsync(lobbyCode);
                var player = await playerScope.Value.getPlayerByUsernameAsync(username);

                if (match != null && player != null)
                {
                    var dto = new ExpulsionDto
                    {
                        MatchId = match.matches_id,
                        PlayerId = player.idPlayer,
                        ReasonId = reasonId,
                        HostPlayerId = hostId
                    };
                    await matchScope.Value.registerExpulsionAsync(dto);
                }
                else
                {
                    logger.Warn("RegisterLobbyExpulsionInDb: Match or player not found. Match: {0}, Player: {1}",
                        match != null, player != null);
                }
            }

        }

        private void updateLobbyStateAndNotify(string lobbyCode, string username)
        {
            if (gameStateManager.ActiveLobbies.TryGetValue(lobbyCode, out var lobbyState))
            {
                lock (lobbyState)
                {
                    lobbyState.Players.RemoveAll(p => p.Equals(username, StringComparison.OrdinalIgnoreCase));
                }
                notificationService.broadcastLobbyState(lobbyState);
            }
        }
    }
}