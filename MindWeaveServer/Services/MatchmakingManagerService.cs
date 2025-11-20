using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Email;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Data.Entity.Core;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class MatchmakingManagerService : IMatchmakingManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly IPlayerRepository playerRepository;
        private int? currentPlayerId;

        private readonly MatchmakingLogic matchmakingLogic;
        private readonly GameSessionManager gameSessionManager;

        private static readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbies =
            new ConcurrentDictionary<string, LobbyStateDto>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, IMatchmakingCallback> userCallbacks =
            new ConcurrentDictionary<string, IMatchmakingCallback>(StringComparer.OrdinalIgnoreCase);

        private string currentUsername;
        private IMatchmakingCallback currentUserCallback;
        private bool isDisconnected;

        public MatchmakingManagerService()
        {
            var dbContext = new MindWeaveDBEntities1();
            var matchmakingRepository = new MatchmakingRepository(dbContext);
            var playerRepositoryDb = new PlayerRepository(dbContext);
            this.playerRepository = playerRepositoryDb;
            var guestInvitationRepository = new GuestInvitationRepository(dbContext);
            var emailService = new SmtpEmailService();

            var puzzleRepository = new PuzzleRepository(dbContext);

            this.gameSessionManager = new GameSessionManager(puzzleRepository, matchmakingRepository);


            matchmakingLogic = new MatchmakingLogic(
                matchmakingRepository,
                playerRepository,
                guestInvitationRepository,
                emailService,
                activeLobbies,
                userCallbacks,
                puzzleRepository,
                this.gameSessionManager

                );

            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed += channel_FaultedOrClosed;
                logger.Debug("Attached Faulted/Closed event handlers to the current WCF channel.");
            }
            else
            {
                logger.Warn("Could not attach channel event handlers - OperationContext or Channel is null.");
            }
            logger.Info("MatchmakingManagerService instance created (PerSession).");
        }

        public async Task<LobbyCreationResultDto> createLobby(string hostUsername, LobbySettingsDto settingsDto)
        {
            logger.Info("createLobby attempt by user: {Username}", hostUsername ?? "NULL");
            if (!ensureSessionIsRegistered(hostUsername))
            {
                logger.Warn("createLobby failed for {Username}: Session could not be registered.", hostUsername);
                return new LobbyCreationResultDto
                { Success = false, Message = Lang.ErrorCommunicationChannelFailed };
            }

            try
            {
                var result = await matchmakingLogic.createLobbyAsync(hostUsername, settingsDto);
                if (result.Success)
                {
                    logger.Info("Lobby created successfully by {Username} with code: {LobbyCode}", hostUsername, result.LobbyCode);
                }
                else
                {
                    logger.Warn("Lobby creation failed for {Username}. Reason: {Reason}", hostUsername, result.Message);
                }
                return result;
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto
                (
                    ServiceErrorType.DatabaseError,
                   Lang.GenericServerError,
                    "Database"
                );

                logger.Fatal(ex, "createLobby Fatal: Database unavailable for {Username}", hostUsername);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Lang.GenericServerError,
                    "Server"
                );

                logger.Fatal(ex, "createLobby Critical: Unhandled exception for {Username}", hostUsername);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }

        public void joinLobby(string username, string lobbyId)
        {
            logger.Info("joinLobby attempt by user: {Username} for lobby: {LobbyId}", username ?? "NULL", lobbyId ?? "NULL");
            if (!ensureSessionIsRegistered(username))
            {
                logger.Warn("joinLobby failed for {Username}: Session could not be registered.", username ?? "NULL");
                try
                {
                    currentUserCallback?.lobbyCreationFailed(Lang.ErrorCommunicationChannelFailed);
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Exception sending lobbyCreationFailed callback during joinLobby session check for {Username}", username ?? "NULL");
                }
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.joinLobbyAsync(username, lobbyId, currentUserCallback);
                    logger.Info("JoinLobby logic executed for {Username} and lobby {LobbyId}. Logic will handle callbacks.", username, lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Service Exception in JoinLobby for {Username}, lobby {LobbyId}", username ?? "NULL", lobbyId ?? "NULL");
                    try
                    {
                        currentUserCallback?.lobbyCreationFailed(Lang.GenericServerError);
                    }
                    catch (Exception e)
                    {
                        logger.Warn(e, "Exception sending LobbyCreationFailed callback after error in JoinLobby for {Username}", username ?? "NULL");
                    }

                    await handleDisconnect();
                }
            });
        }

        public void leaveLobby(string username, string lobbyId)
        {
            logger.Info("leaveLobby attempt by user: {Username} from lobby: {LobbyId}", username ?? "NULL", lobbyId ?? "NULL");
            if (!ensureSessionIsRegistered(username))
            {
                logger.Warn("leaveLobby called by {Username}, but session is not registered. Ignoring.", username ?? "NULL");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.leaveLobbyAsync(username, lobbyId);
                    logger.Info("LeaveLobby logic executed for {Username} from lobby {LobbyId}.", username, lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Service Exception in LeaveLobby for {Username}, lobby {LobbyId}", username ?? "NULL", lobbyId ?? "NULL");
                }
            });
        }

        public void startGame(string hostUsername, string lobbyId)
        {
            logger.Info("startGame attempt by host: {Username} for lobby: {LobbyId}", hostUsername ?? "NULL", lobbyId ?? "NULL");
            if (!ensureSessionIsRegistered(hostUsername))
            {
                logger.Warn("startGame called by {Username}, but session is not registered. Ignoring.", hostUsername ?? "NULL");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.startGameAsync(hostUsername, lobbyId);
                    logger.Info("StartGame logic executed for {Username} and lobby {LobbyId}.", hostUsername, lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Service Exception in StartGame by {Username}, lobby {LobbyId}", hostUsername ?? "NULL", lobbyId ?? "NULL");
                }
            });
        }

        public void kickPlayer(string hostUsername, string playerToKickUsername, string lobbyId)
        {
            logger.Info("kickPlayer attempt by host: {HostUsername} to kick {PlayerToKick} from lobby: {LobbyId}", hostUsername ?? "NULL", playerToKickUsername ?? "NULL", lobbyId ?? "NULL");
            if (!ensureSessionIsRegistered(hostUsername))
            {
                logger.Warn("kickPlayer called by {Username}, but session is not registered. Ignoring.", hostUsername ?? "NULL");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.kickPlayerAsync(hostUsername, playerToKickUsername, lobbyId);
                    logger.Info("KickPlayer logic executed by {HostUsername} for {PlayerToKick}, lobby {LobbyId}.", hostUsername, playerToKickUsername, lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Service Exception in KickPlayer by {HostUsername}, lobby {LobbyId}", hostUsername ?? "NULL", lobbyId ?? "NULL");
                }
            });
        }

        public void inviteToLobby(string inviterUsername, string invitedUsername, string lobbyId)
        {
            logger.Info("inviteToLobby attempt by: {InviterUsername} to {InvitedUsername} for lobby: {LobbyId}", inviterUsername ?? "NULL", invitedUsername ?? "NULL", lobbyId ?? "NULL");
            if (!ensureSessionIsRegistered(inviterUsername))
            {
                logger.Warn("inviteToLobby called by {Username}, but session is not registered. Ignoring.", inviterUsername ?? "NULL");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.inviteToLobbyAsync(inviterUsername, invitedUsername, lobbyId);
                    logger.Info("InviteToLobby logic executed by {InviterUsername} to {InvitedUsername}, lobby {LobbyId}.", inviterUsername, invitedUsername, lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Service Exception in InviteToLobby from {InviterUsername}, lobby {LobbyId}", inviterUsername ?? "NULL", lobbyId ?? "NULL");
                }
            });
        }

        public void changeDifficulty(string hostUsername, string lobbyId, int newDifficultyId)
        {
            logger.Info("changeDifficulty attempt by host: {Username} for lobby: {LobbyId}, new difficulty: {DifficultyId}", hostUsername ?? "NULL", lobbyId ?? "NULL", newDifficultyId);
            if (!ensureSessionIsRegistered(hostUsername))
            {
                logger.Warn("changeDifficulty called by {Username}, but session is not registered. Ignoring.", hostUsername ?? "NULL");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.changeDifficultyAsync(hostUsername, lobbyId, newDifficultyId);
                    logger.Info("ChangeDifficulty logic executed for lobby {LobbyId}.", lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Service Exception in ChangeDifficulty by {Username}, lobby {LobbyId}", hostUsername ?? "NULL", lobbyId ?? "NULL");
                }
            });
        }


        public void inviteGuestByEmail(GuestInvitationDto invitationData)
        {
            if (matchmakingLogic == null)
            {
                logger.Error("inviteGuestByEmail failed: matchmakingLogic is null.");
                return;
            }
            if (invitationData == null || string.IsNullOrWhiteSpace(invitationData.InviterUsername))
            {
                logger.Warn("inviteGuestByEmail called with invalid invitation data.");
                return;
            }

            string inviter = invitationData.InviterUsername;
            logger.Info("inviteGuestByEmail attempt by: {InviterUsername} for email {GuestEmail}, lobby: {LobbyId}", inviter, invitationData.GuestEmail ?? "NULL", invitationData.LobbyCode ?? "NULL");

            if (!ensureSessionIsRegistered(inviter))
            {
                logger.Warn("inviteGuestByEmail called by user {InviterUsername} but session is invalid or mismatched.", inviter);
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.inviteGuestByEmailAsync(invitationData);
                    logger.Info("InviteGuestByEmail logic executed for {GuestEmail}, lobby {LobbyId}.", invitationData.GuestEmail, invitationData.LobbyCode);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Service Exception in InviteGuestByEmail from {InviterUsername} for {GuestEmail}, lobby {LobbyId}", inviter, invitationData.GuestEmail ?? "NULL", invitationData.LobbyCode ?? "NULL");
                }
            });
        }

        public async Task<GuestJoinResultDto> joinLobbyAsGuest(GuestJoinRequestDto joinRequest)
        {
            string codeForContext = joinRequest?.LobbyCode ?? "NULL";
            logger.Info("joinLobbyAsGuest attempt with code: {LobbyCode}", codeForContext);

            if (matchmakingLogic == null)
            {
                logger.Error("joinLobbyAsGuest failed: Service initialization failed.");
                return new GuestJoinResultDto { Success = false, Message = Lang.ErrorServiceInitializationFailed };
            }

            if (isDisconnected)
            {
                logger.Warn("joinLobbyAsGuest rejected for code {LobbyCode}: Service instance is marked as disconnected.", codeForContext);
                return new GuestJoinResultDto
                { Success = false, Message = Lang.ErrorServiceConnectionClosing };
            }

            try
            {
                var guestCallback = OperationContext.Current?.GetCallbackChannel<IMatchmakingCallback>();
                if (guestCallback == null)
                {
                    logger.Error("joinLobbyAsGuest failed for code {LobbyCode}: Could not retrieve callback channel for guest.", codeForContext);
                    throw new InvalidOperationException("Could not retrieve callback channel for guest.");
                }

                GuestJoinResultDto result = await matchmakingLogic.joinLobbyAsGuestAsync(joinRequest, guestCallback);

                if (result.Success && !string.IsNullOrWhiteSpace(result.AssignedGuestUsername))
                {
                    currentUsername = result.AssignedGuestUsername;
                    currentUserCallback = guestCallback;
                    setupCallbackEvents(guestCallback as ICommunicationObject);
                    logger.Info("joinLobbyAsGuest successful for code {LobbyCode}. Assigned username: {GuestUsername}", codeForContext, result.AssignedGuestUsername);

                }
                else
                {
                    logger.Warn("joinLobbyAsGuest failed for code {LobbyCode}. Reason: {Reason}", codeForContext, result.Message);
                }
                return result;
            }
            catch (EntityException ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.DatabaseError,
                    Lang.GenericServerError,
                    "Database"
                );

                logger.Fatal(ex, "joinLobbyAsGuest Fatal: Database unavailable for Lobby {LobbyCode}", codeForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
            }
            catch (Exception ex)
            {
                var fault = new ServiceFaultDto(
                    ServiceErrorType.Unknown,
                    Lang.GenericServerError,
                    "Server"
                );

                logger.Fatal(ex, "joinLobbyAsGuest Critical: Unhandled exception for Lobby {LobbyCode}", codeForContext);
                throw new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
            }
        }
        private int getPlayerIdFromContext()
        {
            if (currentPlayerId.HasValue)
            {
                return currentPlayerId.Value;
            }

            if (string.IsNullOrEmpty(currentUsername))
            {
                logger.Error("GetPlayerIdFromContext: Cannot get PlayerId because currentUsername is null.");
                throw new InvalidOperationException("User session is not registered.");
            }

            try
            {
                var player = playerRepository.getPlayerByUsernameAsync(currentUsername);
                if (player == null)
                {
                    logger.Error("GetPlayerIdFromContext: No player found with username {Username}", currentUsername);
                    throw new InvalidOperationException("Player not found in database.");
                }

                currentPlayerId = player.Id;
                return currentPlayerId.Value;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception getting PlayerId for {Username}", currentUsername);
                throw;
            }
        }

        public void requestPieceDrag(string lobbyCode, int pieceId)
        {
            if (!ensureSessionIsRegistered(this.currentUsername))
            {
                logger.Warn("RequestPieceDrag: Session not registered for {Username}", this.currentUsername);
                return;
            }

            try
            {
                var playerId = getPlayerIdFromContext();
                logger.Info("Player {Username} (ID: {PlayerId}) requested drag for piece {PieceId} in lobby {LobbyCode}",
                    currentUsername, playerId, pieceId, lobbyCode);

                gameSessionManager.handlePieceDrag(lobbyCode, playerId, pieceId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception in RequestPieceDrag for player {Username}", this.currentUsername);
            }
        }

        public void requestPieceDrop(string lobbyCode, int pieceId, double newX, double newY)
        {
            if (!ensureSessionIsRegistered(this.currentUsername))
            {
                logger.Warn("RequestPieceDrop: Session not registered for {Username}", this.currentUsername);
                return;
            }

            try
            {
                var playerId = getPlayerIdFromContext();
                logger.Info("Player {Username} (ID: {PlayerId}) requested drop for piece {PieceId} at ({X},{Y}) in lobby {LobbyCode}",
                    currentUsername, playerId, pieceId, newX, newY, lobbyCode);

                Task.Run(async () => await gameSessionManager.handlePieceDrop(lobbyCode, playerId, pieceId, newX, newY));
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception in RequestPieceDrop for player {Username}", this.currentUsername);
            }
        }

        public void requestPieceRelease(string lobbyCode, int pieceId)
        {
            if (!ensureSessionIsRegistered(this.currentUsername))
            {
                logger.Warn("RequestPieceRelease: Session not registered for {Username}", this.currentUsername);
                return;
            }

            try
            {
                var playerId = getPlayerIdFromContext();
                logger.Info("Player {Username} (ID: {PlayerId}) requested release for piece {PieceId} in lobby {LobbyCode}",
                    currentUsername, playerId, pieceId, lobbyCode);

                gameSessionManager.handlePieceRelease(lobbyCode, playerId, pieceId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception in RequestPieceRelease for player {Username}", this.currentUsername);
            }
        }

        private async void channel_FaultedOrClosed(object sender, EventArgs e)
        {
            logger.Warn("WCF channel Faulted or Closed for user: {Username}. Initiating disconnect.", currentUsername);
            await handleDisconnect();
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= channel_FaultedOrClosed;
                commObject.Closed -= channel_FaultedOrClosed;
                logger.Debug("Removed Faulted/Closed event handlers from a callback channel.");
            }
        }

        private bool tryRegisterCurrentUserCallback(string username)
        {
            if (OperationContext.Current == null)
            {
                logger.Fatal("CRITICAL: tryRegisterCurrentUserCallback failed, OperationContext is null for {Username}.", username ?? "NULL");
                return false;
            }

            try
            {
                IMatchmakingCallback callback = OperationContext.Current.GetCallbackChannel<IMatchmakingCallback>();
                if (callback == null)
                {
                    logger.Fatal("CRITICAL: GetCallbackChannel returned null for {Username}.", username ?? "NULL");
                    return false;
                }

                currentUserCallback = callback;
                currentUsername = username;

                matchmakingLogic.registerCallback(username, currentUserCallback);

                ICommunicationObject commObject = currentUserCallback as ICommunicationObject;
                if (commObject != null)
                {
                    commObject.Faulted += channel_FaultedOrClosed;
                    commObject.Closed += channel_FaultedOrClosed;
                }

                logger.Info("Session and Callback registered for {Username}.", username);
                return true;
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "CRITICAL: Exception in tryRegisterCurrentUserCallback for {Username}", username ?? "NULL");
                currentUsername = null;
                currentUserCallback = null;
                return false;
            }
        }

        private bool ensureSessionIsRegistered(string username)
        {
            if (isDisconnected)
            {
                logger.Warn("ensureSessionIsRegistered check failed for {Username}: Session is already marked as disconnected.", username ?? "NULL");
                return false;
            }

            if (!string.IsNullOrEmpty(currentUsername))
            {
                if (currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                logger.Fatal("CRITICAL: Session previously registered for {CurrentUsername} received a call for {Username}. Aborting and disconnecting session.", currentUsername, username ?? "NULL");
                Task.Run(async () => await handleDisconnect());
                return false;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Fatal("CRITICAL: Method called with null or empty username before session was registered.");
                return false;
            }

            return tryRegisterCurrentUserCallback(username);
        }

        private async Task handleDisconnect()
        {
            if (isDisconnected) return;
            isDisconnected = true;

            string userToDisconnect = currentUsername;
            int? idToDisconnect = currentPlayerId;

            logger.Warn("Disconnect triggered for session. User: {Username}", userToDisconnect);

            if (OperationContext.Current?.Channel != null)
            {
                OperationContext.Current.Channel.Faulted -= channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed -= channel_FaultedOrClosed;
            }

            if (currentUserCallback != null)
            {
                cleanupCallbackEvents(currentUserCallback as ICommunicationObject);
            }

            if (!string.IsNullOrWhiteSpace(userToDisconnect))
            {
                try
                {
                    if (idToDisconnect.HasValue)
                    {
                        gameSessionManager.handlePlayerDisconnect(userToDisconnect, idToDisconnect.Value);
                    }
                    await Task.Run(() => matchmakingLogic.handleUserDisconnect(userToDisconnect));
                    logger.Info("Logic layer disconnect notification sent for {Username}", userToDisconnect);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Exception during logic layer disconnect notification for {Username}", userToDisconnect);
                }
            }
            else
            {
                logger.Info("No username was associated with this disconnecting session, skipping logic notification.");
            }

            currentUsername = null;
            currentUserCallback = null;
            currentPlayerId = null;
        }

        private void setupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= channel_FaultedOrClosed;
                commObject.Closed -= channel_FaultedOrClosed;
                commObject.Faulted += channel_FaultedOrClosed;
                commObject.Closed += channel_FaultedOrClosed;

                logger.Debug("Event handlers (Faulted/Closed) attached for user: {Username} callback. Channel State: {State}", currentUsername);
            }
            else
            {
                logger.Warn("Attempted to setup callback events, but communication object was null for user: {Username}.", currentUsername);
            }
        }
    }
}