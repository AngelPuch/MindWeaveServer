using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Utilities.Abstractions;
using NLog;
using System;
using System.Data.Entity.Core;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.IO;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(
        InstanceContextMode = InstanceContextMode.PerSession,
        ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class MatchmakingManagerService : IMatchmakingManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly MatchmakingLogic matchmakingLogic;
        private readonly GameSessionManager gameSessionManager;
        private readonly IPlayerRepository playerRepository;
        private readonly IServiceExceptionHandler exceptionHandler;
        private readonly IDisconnectionHandler disconnectionHandler;

        private string currentUsername;
        private int? currentPlayerId;
        private IMatchmakingCallback currentUserCallback;

        private volatile bool isDisconnecting;
        private readonly object disconnectLock = new object();

        public MatchmakingManagerService()
        {
            Bootstrapper.init();

            this.matchmakingLogic = Bootstrapper.Container.Resolve<MatchmakingLogic>();
            this.gameSessionManager = Bootstrapper.Container.Resolve<GameSessionManager>();
            this.playerRepository = Bootstrapper.Container.Resolve<IPlayerRepository>();
            this.exceptionHandler = Bootstrapper.Container.Resolve<IServiceExceptionHandler>();
            this.disconnectionHandler = Bootstrapper.Container.Resolve<IDisconnectionHandler>();

            subscribeToChannelEvents();
        }

        public MatchmakingManagerService(
            MatchmakingLogic matchmakingLogic,
            GameSessionManager gameSessionManager,
            IPlayerRepository playerRepository,
            IServiceExceptionHandler exceptionHandler,
            IDisconnectionHandler disconnectionHandler)
        {
            this.matchmakingLogic = matchmakingLogic;
            this.gameSessionManager = gameSessionManager;
            this.playerRepository = playerRepository;
            this.exceptionHandler = exceptionHandler;
            this.disconnectionHandler = disconnectionHandler;

            subscribeToChannelEvents();
        }

        private void subscribeToChannelEvents()
        {
            if (OperationContext.Current?.Channel == null)
            {
                logger.Warn("MatchmakingManagerService: Cannot subscribe to channel events - OperationContext or Channel is null.");
                return;
            }

            OperationContext.Current.Channel.Faulted += onChannelFaulted;
            OperationContext.Current.Channel.Closed += onChannelClosed;

            logger.Debug("MatchmakingManagerService: Subscribed to channel Faulted/Closed events.");
        }

        private void onChannelFaulted(object sender, EventArgs e)
        {
            logger.Warn("MatchmakingManagerService: Channel FAULTED for user {0}. Initiating disconnection.",
                currentUsername ?? "Unknown");

            initiateGameCleanupAsync();
        }

        private void onChannelClosed(object sender, EventArgs e)
        {
            logger.Info("MatchmakingManagerService: Channel CLOSED for user {0}. Initiating disconnection.",
                currentUsername ?? "Unknown");

            initiateGameCleanupAsync();
        }

        private void initiateGameCleanupAsync()
        {
            string usernameToDisconnect;

            lock (disconnectLock)
            {
                if (isDisconnecting) return;
                isDisconnecting = true;
                usernameToDisconnect = currentUsername;
            }

            if (string.IsNullOrWhiteSpace(usernameToDisconnect)) return;

            Task.Run(async () =>
            {
                try
                {
                    await disconnectionHandler.handleGameDisconnectionAsync(usernameToDisconnect);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error during matchmaking cleanup for {0}.", usernameToDisconnect);
                }
                finally
                {
                    cleanupLocalState();
                }
            });
        }

        private void cleanupLocalState()
        {
            cleanupCallbackEvents(OperationContext.Current?.Channel);
            cleanupCallbackEvents(currentUserCallback as ICommunicationObject);

            currentUsername = null;
            currentUserCallback = null;
            currentPlayerId = null;
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= onChannelFaulted;
                commObject.Closed -= onChannelClosed;
            }
        }

        private void preloadPlayerId()
        {
            try
            {
                if (!currentPlayerId.HasValue && !string.IsNullOrEmpty(currentUsername))
                {
                    var player = playerRepository.getPlayerByUsernameAsync(currentUsername).GetAwaiter().GetResult();
                    if (player != null)
                    {
                        currentPlayerId = player.idPlayer;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Could not preload player ID via synchronous call.");
            }
        }

        public async Task<LobbyCreationResultDto> createLobby(string hostUsername, LobbySettingsDto settingsDto)
        {
            logger.Info("CreateLobby operation started.");

            if (!ensureSessionIsRegistered(hostUsername))
            {
                logger.Warn("CreateLobby failed: Session could not be registered.");
                return new LobbyCreationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.ERROR_COMMUNICATION_CHANNEL
                };
            }

            try
            {
                var result = await matchmakingLogic.createLobbyAsync(hostUsername, settingsDto);
                if (result.Success)
                {
                    preloadPlayerId();
                }
                return result;
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "CreateLobbyOperation");
            }
        }

        public void joinLobby(string username, string lobbyId)
        {
            logger.Info("JoinLobby operation started for lobby: {LobbyId}", lobbyId ?? "NULL");

            if (!ensureSessionIsRegistered(username))
            {
                logger.Warn("JoinLobby failed: Session could not be registered.");
                trySendCallback(cb => cb.lobbyCreationFailed(MessageCodes.ERROR_COMMUNICATION_CHANNEL));
                return;
            }

            preloadPlayerId();

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.joinLobbyAsync(username, lobbyId, currentUserCallback);
                }
                catch (EntityException dbEx)
                {
                    logger.Error(dbEx, "Database error joining lobby: {LobbyId}", lobbyId);
                    trySendCallback(cb => cb.lobbyCreationFailed(MessageCodes.MATCH_JOIN_ERROR_DATA));
                }
                catch (SqlException sqlEx)
                {
                    logger.Error(sqlEx, "SQL error joining lobby: {LobbyId}", lobbyId);
                    trySendCallback(cb => cb.lobbyCreationFailed(MessageCodes.ERROR_SERVER_GENERIC));
                }
                catch (TimeoutException timeEx)
                {
                    logger.Error(timeEx, "Timeout joining lobby: {LobbyId}", lobbyId);
                    trySendCallback(cb => cb.lobbyCreationFailed(MessageCodes.ERROR_SERVER_GENERIC));
                }
            });
        }

        public void leaveLobby(string username, string lobbyId)
        {
            logger.Info("LeaveLobby operation started for lobby: {LobbyId}", lobbyId ?? "NULL");

            if (!ensureSessionIsRegistered(username))
            {
                logger.Warn("LeaveLobby called but session is not registered.");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.leaveLobbyAsync(username, lobbyId);
                }
                catch (EntityException dbEx)
                {
                    logger.Error(dbEx, "Database error leaving lobby: {LobbyId}", lobbyId);
                }
                catch (SqlException sqlEx)
                {
                    logger.Error(sqlEx, "SQL error leaving lobby: {LobbyId}", lobbyId);
                }
                catch (TimeoutException ex)
                {
                    logger.Error(ex, "Timeout leaving lobby: {0}", lobbyId);
                }
                catch (CommunicationException commEx)
                {
                    logger.Warn(commEx, "Network error notifying users of leave in lobby: {LobbyId}", lobbyId);
                }
            });
        }

        public void startGame(string hostUsername, string lobbyId)
        {
            logger.Info("StartGame operation started for lobby: {LobbyId}", lobbyId ?? "NULL");

            if (!ensureSessionIsRegistered(hostUsername))
            {
                logger.Warn("StartGame called but session is not registered.");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.startGameAsync(hostUsername, lobbyId);
                }
                catch (EntityException ex)
                {
                    logger.Error(ex, "Database error starting game for lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.MATCH_START_DB_ERROR));
                }
                catch (DbUpdateException ex)
                {
                    logger.Error(ex, "Database update error starting game for lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.MATCH_START_DB_ERROR));
                }
                catch (SqlException ex)
                {
                    logger.Error(ex, "SQL error starting game for lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.ERROR_SERVER_GENERIC));
                }
                catch (TimeoutException ex)
                {
                    logger.Error(ex, "Timeout starting game for lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.ERROR_SERVER_GENERIC));
                }
                catch (FileNotFoundException ex)
                {
                    logger.Error(ex, "Puzzle file not found for lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.MATCH_PUZZLE_FILE_NOT_FOUND));
                }
                catch (InvalidOperationException ex)
                {
                    logger.Error(ex, "Invalid operation starting game for lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.ERROR_SERVER_GENERIC));
                }
                catch (Exception ex)
                {
                    logger.Fatal(ex, "UNHANDLED EXCEPTION starting game for lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.ERROR_SERVER_GENERIC));
                }
            });
        }

        public void kickPlayer(string hostUsername, string playerToKickUsername, string lobbyId)
        {
            logger.Info("KickPlayer operation started for lobby: {LobbyId}", lobbyId ?? "NULL");

            if (!ensureSessionIsRegistered(hostUsername))
            {
                logger.Warn("KickPlayer called but session is not registered.");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.kickPlayerAsync(hostUsername, playerToKickUsername, lobbyId);
                }
                catch (EntityException ex)
                {
                    logger.Error(ex, "Database error kicking player from lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.ERROR_SERVER_GENERIC));
                }
                catch (SqlException ex)
                {
                    logger.Error(ex, "SQL error kicking player from lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.ERROR_SERVER_GENERIC));
                }
                catch (CommunicationException ex)
                {
                    logger.Warn(ex, "Communication error notifying kicked player in lobby: {0}", lobbyId);
                }
            });
        }

        public void inviteToLobby(string inviterUsername, string invitedUsername, string lobbyId)
        {
            logger.Info("InviteToLobby operation started for lobby: {LobbyId}", lobbyId ?? "NULL");

            if (!ensureSessionIsRegistered(inviterUsername))
            {
                logger.Warn("InviteToLobby called but session is not registered.");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.inviteToLobbyAsync(inviterUsername, invitedUsername, lobbyId);
                }
                catch (CommunicationException ex)
                {
                    logger.Warn(ex, "Communication error inviting player to lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.MATCH_COMMUNICATION_ERROR));
                }
                catch (ObjectDisposedException ex)
                {
                    logger.Warn(ex, "Channel disposed while inviting player to lobby: {0}", lobbyId);
                }
            });
        }

        public void changeDifficulty(string hostUsername, string lobbyId, int newDifficultyId)
        {
            logger.Info("ChangeDifficulty operation started for lobby: {LobbyId}, difficulty: {DifficultyId}",
                lobbyId ?? "NULL", newDifficultyId);

            if (!ensureSessionIsRegistered(hostUsername))
            {
                logger.Warn("ChangeDifficulty called but session is not registered.");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.changeDifficultyAsync(hostUsername, lobbyId, newDifficultyId);
                }
                catch (EntityException ex)
                {
                    logger.Error(ex, "Database error changing difficulty for lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.MATCH_DIFFICULTY_CHANGE_ERROR));
                }
                catch (DbUpdateException ex)
                {
                    logger.Error(ex, "Database update error changing difficulty for lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.MATCH_DIFFICULTY_CHANGE_ERROR));
                }
                catch (SqlException ex)
                {
                    logger.Error(ex, "SQL error changing difficulty for lobby: {0}", lobbyId);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.ERROR_SERVER_GENERIC));
                }
            });
        }

        public void inviteGuestByEmail(GuestInvitationDto invitationData)
        {
            if (invitationData == null || string.IsNullOrWhiteSpace(invitationData.InviterUsername))
            {
                logger.Warn("InviteGuestByEmail called with invalid invitation data.");
                return;
            }

            logger.Info("InviteGuestByEmail operation started for lobby: {LobbyId}",
                invitationData.LobbyCode ?? "NULL");

            if (!ensureSessionIsRegistered(invitationData.InviterUsername))
            {
                logger.Warn("InviteGuestByEmail called but session is invalid.");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.inviteGuestByEmailAsync(invitationData);
                }
                catch (EntityException ex)
                {
                    logger.Error(ex, "Database error sending guest invite for lobby: {0}", invitationData.LobbyCode);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.MATCH_GUEST_INVITE_SEND_ERROR));
                }
                catch (DbUpdateException ex)
                {
                    logger.Error(ex, "Database update error sending guest invite for lobby: {0}", invitationData.LobbyCode);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.MATCH_GUEST_INVITE_SEND_ERROR));
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                    logger.Error(ex, "Email service error for lobby: {0}", invitationData.LobbyCode);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.ERROR_COMMUNICATION_CHANNEL));
                }
                catch (TimeoutException ex)
                {
                    logger.Error(ex, "Timeout sending guest invite for lobby: {0}", invitationData.LobbyCode);
                    trySendCallback(cb => cb.notifyLobbyActionFailed(MessageCodes.ERROR_SERVER_GENERIC));
                }
            });
        }

        public void leaveGame(string username, string lobbyCode)
        {
            logger.Info("LeaveGame operation started for lobby: {0}", lobbyCode ?? "NULL");

            Task.Run(async () =>
            {
                try
                {
                    await gameSessionManager.handlePlayerLeaveAsync(lobbyCode, username);
                }
                catch (EntityException ex)
                {
                    logger.Error(ex, "Database error during player leave from game: {0}", lobbyCode);
                }
                catch (DbUpdateException ex)
                {
                    logger.Error(ex, "Database update error during player leave from game: {0}", lobbyCode);
                }
                catch (SqlException ex)
                {
                    logger.Error(ex, "SQL error during player leave from game: {0}", lobbyCode);
                }
            });
        }

        public async Task<GuestJoinResultDto> joinLobbyAsGuest(GuestJoinRequestDto joinRequest)
        {
            string lobbyCode = joinRequest?.LobbyCode ?? "NULL";
            logger.Info("JoinLobbyAsGuest operation started for lobby: {LobbyCode}", lobbyCode);

            if (isDisconnecting)
            {
                logger.Warn("JoinLobbyAsGuest rejected: Service instance is marked as disconnected.");
                return new GuestJoinResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.ERROR_SERVICE_CLOSING
                };
            }

            try
            {
                var guestCallback = OperationContext.Current?.GetCallbackChannel<IMatchmakingCallback>();
                if (guestCallback == null)
                {
                    logger.Error("JoinLobbyAsGuest failed: Could not retrieve callback channel.");
                    throw new InvalidOperationException("Could not retrieve callback channel for guest.");
                }

                GuestJoinResultDto result = await matchmakingLogic.joinLobbyAsGuestAsync(joinRequest, guestCallback);

                if (result.Success && !string.IsNullOrWhiteSpace(result.AssignedGuestUsername))
                {
                    currentUsername = result.AssignedGuestUsername;
                    currentUserCallback = guestCallback;
                    setupCallbackEvents(guestCallback as ICommunicationObject);
                }

                return result;
            }
            catch (Exception ex)
            {
                throw exceptionHandler.handleException(ex, "JoinLobbyAsGuestOperation");
            }
        }

        public void requestPieceDrag(string lobbyCode, int pieceId)
        {
            if (!ensureSessionIsRegistered(currentUsername))
            {
                logger.Warn("RequestPieceDrag: Session not registered.");
                return;
            }

            try
            {
                int playerId = getPlayerIdFromContext(lobbyCode);
                gameSessionManager.handlePieceDrag(lobbyCode, playerId, pieceId);
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "RequestPieceDrag: Player session invalid.");
            }
            catch (EntityException ex)
            {
                logger.Error(ex, "RequestPieceDrag: Database error.");
            }
            catch (CommunicationException ex)
            {
                logger.Warn(ex, "RequestPieceDrag: Communication error broadcasting.");
            }
        }

        public void requestPieceMove(string lobbyCode, int pieceId, double newX, double newY)
        {
            if (!ensureSessionIsRegistered(currentUsername))
            {
                return;
            }

            try
            {
                int playerId = getPlayerIdFromContext(lobbyCode);
                gameSessionManager.handlePieceMove(lobbyCode, playerId, pieceId, newX, newY);
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "RequestPieceMove: Player session invalid.");
            }
            catch (CommunicationException ex)
            {
                logger.Warn(ex, "RequestPieceMove: Communication error broadcasting.");
            }
        }

        public void requestPieceDrop(string lobbyCode, int pieceId, double newX, double newY)
        {
            if (!ensureSessionIsRegistered(currentUsername))
            {
                logger.Warn("RequestPieceDrop: Session not registered.");
                return;
            }

            try
            {
                int playerId = getPlayerIdFromContext(lobbyCode);
                Task.Run(async () =>
                {
                    try
                    {
                        await gameSessionManager.handlePieceDrop(lobbyCode, playerId, pieceId, newX, newY);
                    }
                    catch (EntityException ex)
                    {
                        logger.Error(ex, "RequestPieceDrop: Database error during piece drop.");
                    }
                    catch (DbUpdateException ex)
                    {
                        logger.Error(ex, "RequestPieceDrop: Database update error during piece drop.");
                    }
                    catch (CommunicationException ex)
                    {
                        logger.Warn(ex, "RequestPieceDrop: Communication error broadcasting.");
                    }
                    catch (Exception ex) 
                    {
                        logger.Error(ex, "RequestPieceDrop: Unexpected error.");
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "RequestPieceDrop: Player session invalid.");
            }
        }

        public void requestPieceRelease(string lobbyCode, int pieceId)
        {
            if (!ensureSessionIsRegistered(currentUsername))
            {
                logger.Warn("RequestPieceRelease: Session not registered.");
                return;
            }

            try
            {
                int playerId = getPlayerIdFromContext(lobbyCode);
                logger.Debug("RequestPieceRelease: PlayerId {PlayerId}, PieceId {PieceId}, Lobby {LobbyCode}",
                    playerId, pieceId, lobbyCode);

                gameSessionManager.handlePieceRelease(lobbyCode, playerId, pieceId);
            }
            catch (InvalidOperationException ex)
            {
                logger.Error(ex, "RequestPieceRelease: Player session invalid.");
            }
            catch (CommunicationException ex)
            {
                logger.Warn(ex, "RequestPieceRelease: Communication error broadcasting.");
            }
        }


        private int getPlayerIdFromContext(string lobbyCode = null)
        {
            if (currentPlayerId.HasValue)
            {
                return currentPlayerId.Value;
            }

            if (string.IsNullOrEmpty(currentUsername))
            {
                logger.Error("GetPlayerIdFromContext: Cannot get PlayerId - session not registered.");
                throw new InvalidOperationException("User session is not registered.");
            }

            var player = playerRepository.getPlayerByUsernameAsync(currentUsername).GetAwaiter().GetResult();
            if (player != null)
            {
                currentPlayerId = player.idPlayer;
                return currentPlayerId.Value;
            }

            if (!string.IsNullOrEmpty(lobbyCode))
            {
                int? guestId = gameSessionManager.getPlayerIdInLobby(lobbyCode, currentUsername);
                if (guestId.HasValue)
                {
                    logger.Info("Guest ID {0} resolved for user {1}", guestId, currentUsername);
                    currentPlayerId = guestId.Value;
                    return currentPlayerId.Value;
                }
            }

            logger.Error("GetPlayerIdFromContext: Player/Guest not found for user: {0}", currentUsername);
            throw new InvalidOperationException("Player not found in database or active session.");
        }

        private bool tryRegisterCurrentUserCallback(string username)
        {
            if (OperationContext.Current == null)
            {
                logger.Fatal("CRITICAL: OperationContext is null during callback registration.");
                return false;
            }

            try
            {
                IMatchmakingCallback callback = OperationContext.Current.GetCallbackChannel<IMatchmakingCallback>();
                if (callback == null)
                {
                    logger.Fatal("CRITICAL: GetCallbackChannel returned null.");
                    return false;
                }

                currentUserCallback = callback;
                currentUsername = username;

                matchmakingLogic.registerCallback(username, currentUserCallback);
                setupCallbackEvents(currentUserCallback as ICommunicationObject);

                return true;
            }
            catch (InvalidOperationException ex)
            {
                logger.Fatal(ex, "CRITICAL: InvalidOperationException during callback registration.");
                currentUsername = null;
                currentUserCallback = null;
                return false;
            }
            catch (CommunicationException ex)
            {
                logger.Fatal(ex, "CRITICAL: CommunicationException during callback registration.");
                currentUsername = null;
                currentUserCallback = null;
                return false;
            }
        }

        private bool ensureSessionIsRegistered(string username)
        {
            if (isDisconnecting)
            {
                logger.Warn("Session check failed: Already marked as disconnecting.");
                return false;
            }

            if (!string.IsNullOrEmpty(currentUsername))
            {
                if (currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                logger.Fatal("CRITICAL: Session mismatch detected. Aborting.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Fatal("CRITICAL: Method called with null or empty username before session registration.");
                return false;
            }

            return tryRegisterCurrentUserCallback(username);
        }

        private void setupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= onChannelFaulted;
                commObject.Closed -= onChannelClosed;
                commObject.Faulted += onChannelFaulted;
                commObject.Closed += onChannelClosed;
            }
        }

        private void trySendCallback(Action<IMatchmakingCallback> action)
        {
            try
            {
                if (currentUserCallback != null)
                {
                    action(currentUserCallback);
                }
            }
            catch (CommunicationException ex)
            {
                logger.Warn(ex, "CommunicationException sending callback.");

            }
            catch (TimeoutException ex)
            {
                logger.Warn(ex, "TimeoutException sending callback.");
            }
            catch (ObjectDisposedException ex)
            {
                logger.Warn(ex, "ObjectDisposedException sending callback.");
            }
        }
    }
}