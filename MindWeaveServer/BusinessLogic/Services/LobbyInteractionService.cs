using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Email;
using MindWeaveServer.Utilities.Email.Templates;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic.Services
{
    public class LobbyInteractionService : ILobbyInteractionService
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IGameStateManager gameStateManager;
        private readonly ILobbyValidationService validationService;
        private readonly INotificationService notificationService;
        private readonly GameSessionManager gameSessionManager;

        private readonly IMatchmakingRepository matchmakingRepository;
        private readonly IPlayerRepository playerRepository;
        private readonly IGuestInvitationRepository invitationRepository;
        private readonly IEmailService emailService;

        private const int MATCH_STATUS_IN_PROGRESS = 3;
        private const int MATCH_STATUS_CANCELED = 4;
        private const int ID_REASON_HOST_DECISION = 1;
        private const int GUEST_EXPIRY_MINUTES = 10;
        private const int INVALID_ID = 0;
        private const int MAX_PLAYERS_PER_LOBBY = 4;


        public LobbyInteractionService(
            IGameStateManager gameStateManager,
            ILobbyValidationService validationService,
            INotificationService notificationService,
            GameSessionManager gameSessionManager,
            IMatchmakingRepository matchmakingRepository,
            IPlayerRepository playerRepository,
            IGuestInvitationRepository invitationRepository,
            IEmailService emailService)
        {
            this.gameStateManager = gameStateManager;
            this.validationService = validationService;
            this.notificationService = notificationService;
            this.gameSessionManager = gameSessionManager;
            this.matchmakingRepository = matchmakingRepository;
            this.playerRepository = playerRepository;
            this.invitationRepository = invitationRepository;
            this.emailService = emailService;
        }

        public async Task invitePlayerAsync(LobbyActionContext context)
        {
            var lobby = getLobby(context.LobbyCode);
            var validation = validationService.canInvitePlayer(lobby, context.TargetUsername);

            if (!validation.IsSuccess)
            {
                logger.Warn("Invite blocked: {0}", validation.ErrorMessage);
                notificationService.notifyActionFailed(context.RequesterUsername, validation.ErrorMessage);
                return;
            }

            notificationService.sendSocialNotification(context.TargetUsername,
                cb => cb.notifyLobbyInvite(context.RequesterUsername, context.LobbyCode));

            await Task.CompletedTask;
        }

        public async Task kickPlayerAsync(LobbyActionContext context)
        {
            var lobby = getLobby(context.LobbyCode);
            var validation = validationService.canKickPlayer(lobby, context.RequesterUsername, context.TargetUsername);

            if (!validation.IsSuccess)
            {
                notificationService.notifyActionFailed(context.RequesterUsername, validation.ErrorMessage);
                return;
            }

            int hostId = await getPlayerIdAsync(context.RequesterUsername);
            int targetId = await getPlayerIdAsync(context.TargetUsername);

            if (gameSessionManager.getSession(context.LobbyCode) != null)
            {
                await kickFromActiveSessionAsync(context.LobbyCode, targetId, hostId);
            }
            else
            {
                await kickFromLobbyStateAsync(lobby, targetId, hostId, context.TargetUsername);
            }

            removePlayerFromMemory(lobby, context.TargetUsername);
            notificationService.broadcastLobbyState(lobby);
        }

        public async Task startGameAsync(LobbyActionContext context)
        {
            var lobby = getLobby(context.LobbyCode);
            var validation = validationService.canStartGame(lobby, context.RequesterUsername);

            if (!validation.IsSuccess)
            {
                notificationService.notifyActionFailed(context.RequesterUsername, validation.ErrorMessage);
                return;
            }

            Matches match = await updateMatchStatusAsync(context.LobbyCode, MATCH_STATUS_IN_PROGRESS);
            if (match == null)
            {
                notificationService.notifyActionFailed(context.RequesterUsername, Lang.DatabaseErrorStartingMatch);
                return;
            }

            await createAndNotifySessionAsync(lobby, match);
        }

        public async Task changeDifficultyAsync(LobbyActionContext context, int newDifficultyId)
        {
            var lobby = getLobby(context.LobbyCode);

            if (lobby == null || !lobby.HostUsername.Equals(context.RequesterUsername, StringComparison.OrdinalIgnoreCase))
            {
                notificationService.notifyActionFailed(context.RequesterUsername, Lang.notHost);
                return;
            }

            if (await persistDifficultyChangeAsync(context.LobbyCode, newDifficultyId))
            {
                lobby.CurrentSettingsDto.DifficultyId = newDifficultyId;
                notificationService.broadcastLobbyState(lobby);
            }
            else
            {
                notificationService.notifyActionFailed(context.RequesterUsername, Lang.ErrorSavingDifficultyChange);
            }
        }

        public async Task inviteGuestByEmailAsync(string inviterUsername, string lobbyCode, string guestEmail)
        {
            var lobby = getLobby(lobbyCode);

            if (lobby == null)
            {
                notificationService.notifyActionFailed(inviterUsername, Lang.LobbyDataNotFound);
                return;
            }

            if (lobby.Players.Count >= MAX_PLAYERS_PER_LOBBY)
            {
                notificationService.notifyActionFailed(inviterUsername, Lang.LobbyIsFull);
                return;
            }

            int inviterId = await getPlayerIdAsync(inviterUsername);
            int matchId = await getMatchIdAsync(lobbyCode);

            if (matchId > 0 && await saveGuestInvitationAsync(matchId, inviterId, guestEmail))
            {
                await emailService.sendEmailAsync(guestEmail, guestEmail, new GuestInviteEmailTemplate(inviterUsername, lobbyCode));
                logger.Info("Guest invite sent to {0}", guestEmail);
            }
            else
            {
                notificationService.notifyActionFailed(inviterUsername, Lang.ErrorSendingGuestInvitation);
            }
        }

        private LobbyStateDto getLobby(string code)
        {
            gameStateManager.ActiveLobbies.TryGetValue(code, out var lobby);
            return lobby;
        }

        private async Task kickFromActiveSessionAsync(string lobbyCode, int targetId, int hostId)
        {
            var session = gameSessionManager.getSession(lobbyCode);
            if (session != null)
            {
                await session.kickPlayerAsync(targetId, ID_REASON_HOST_DECISION, hostId);
            }
        }

        private async Task kickFromLobbyStateAsync(LobbyStateDto lobby, int targetId, int hostId, string targetUsername)
        {
            var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobby.LobbyId);
            if (match != null)
            {
                await matchmakingRepository.registerExpulsionAsync(new ExpulsionDto
                {
                    MatchId = match.matches_id,
                    PlayerId = targetId,
                    HostPlayerId = hostId,
                    ReasonId = ID_REASON_HOST_DECISION
                });
            }

            notificationService.notifyKicked(targetUsername, Lang.KickedByHost);
        }

        private void removePlayerFromMemory(LobbyStateDto lobby, string username)
        {
            if (lobby == null) return;
            lock (lobby)
            {
                lobby.Players.RemoveAll(p => p.Equals(username, StringComparison.OrdinalIgnoreCase));
            }

            if (gameStateManager.GuestUsernamesInLobby.TryGetValue(lobby.LobbyId, out var guests))
            {
                guests.Remove(username);
            }
        }

        private async Task<Matches> updateMatchStatusAsync(string lobbyCode, int statusId)
        {
            var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
            if (match != null)
            {
                matchmakingRepository.updateMatchStatus(match, statusId);
                matchmakingRepository.updateMatchStartTime(match);
            }
            return match;
        }

        private async Task createAndNotifySessionAsync(LobbyStateDto lobby, Matches match)
        {
            var playersMap = new ConcurrentDictionary<int, PlayerSessionData>();
            var guests = gameStateManager.GuestUsernamesInLobby.GetOrAdd(lobby.LobbyId, new HashSet<string>());

            foreach (var user in lobby.Players)
            {
                int pid = guests.Contains(user)
                    ? -Math.Abs(user.GetHashCode())
                    : await getPlayerIdAsync(user);

                if (gameStateManager.MatchmakingCallbacks.TryGetValue(user, out IMatchmakingCallback mmCallback))
                {
                    playersMap.TryAdd(pid, new PlayerSessionData
                    {
                        PlayerId = pid,
                        Username = user,
                        Callback = mmCallback
                    });
                }
                else
                {
                    logger.Warn("CreateSession: No matchmaking callback found for user {0}. They will be excluded.", user);
                }
            }

            if (playersMap.IsEmpty)
            {
                logger.Warn("CreateSession: Player map is empty (callbacks missing?). Canceling match {0}.", match.matches_id);
                await updateMatchStatusAsync(lobby.LobbyId, MATCH_STATUS_CANCELED);
                notificationService.notifyActionFailed(lobby.HostUsername, "Error: No players found with active connections.");
                return;
            }

            var session = await gameSessionManager.createGameSession(lobby.LobbyId, match.matches_id, match.puzzle_id, match.DifficultyLevels, playersMap);
            session.startMatchTimer(match.DifficultyLevels.time_limit_seconds);

            Action<IMatchmakingCallback> startMsg = cb => cb.onGameStarted(session.PuzzleDefinition, match.DifficultyLevels.time_limit_seconds);

            foreach (var p in playersMap.Values)
            {
                notificationService.sendToUser(p.Username, startMsg);
            }

            gameStateManager.ActiveLobbies.TryRemove(lobby.LobbyId, out _);
        }

        private async Task<bool> persistDifficultyChangeAsync(string lobbyCode, int difficultyId)
        {
            var match = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
            if (match != null)
            {
                matchmakingRepository.updateMatchDifficulty(match, difficultyId);
                return true;
            }
            return false;
        }

        private async Task<bool> saveGuestInvitationAsync(int matchId, int inviterId, string email)
        {
            await invitationRepository.addInvitationAsync(new GuestInvitations
            {
                match_id = matchId,
                inviter_player_id = inviterId,
                guest_email = email,
                sent_timestamp = DateTime.UtcNow,
                expiry_timestamp = DateTime.UtcNow.AddMinutes(GUEST_EXPIRY_MINUTES)
            });
            return true;
        }

        private async Task<int> getPlayerIdAsync(string username)
        {
            if (string.IsNullOrEmpty(username)) return INVALID_ID;
            var p = await playerRepository.getPlayerByUsernameAsync(username);
            return p?.idPlayer ?? INVALID_ID;
        }

        private async Task<int> getMatchIdAsync(string lobbyCode)
        {
            var m = await matchmakingRepository.getMatchByLobbyCodeAsync(lobbyCode);
            return m?.matches_id ?? INVALID_ID;
        }
    }
}