using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using NLog;
using System;
using System.ServiceModel;
using System.Threading.Tasks;
using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.Utilities.Abstractions;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class MatchmakingManagerService : IMatchmakingManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly MatchmakingLogic matchmakingLogic;
        private readonly GameSessionManager gameSessionManager;
        private readonly IPlayerRepository playerRepository;
        private readonly IServiceExceptionHandler exceptionHandler;

        private string currentUsername;
        private int? currentPlayerId;
        private IMatchmakingCallback currentUserCallback;
        private bool isDisconnected;

        public MatchmakingManagerService() : this(
            Bootstrapper.Container.Resolve<MatchmakingLogic>(),
            Bootstrapper.Container.Resolve<GameSessionManager>(),
            Bootstrapper.Container.Resolve<IPlayerRepository>(),
            Bootstrapper.Container.Resolve<IServiceExceptionHandler>())
        {
        }

        public MatchmakingManagerService(
            MatchmakingLogic matchmakingLogic,
            GameSessionManager gameSessionManager,
            IPlayerRepository playerRepository,
            IServiceExceptionHandler exceptionHandler)
        {
            this.matchmakingLogic = matchmakingLogic;
            this.gameSessionManager = gameSessionManager;
            this.playerRepository = playerRepository;
            this.exceptionHandler = exceptionHandler;
            initializeService();
        }

        private void initializeService()
        {
            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += channelFaultedOrClosed;
                OperationContext.Current.Channel.Closed += channelFaultedOrClosed;
            }
            else
            {
                logger.Warn("Could not attach channel event handlers - OperationContext or Channel is null.");
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
                    Message = Lang.ErrorCommunicationChannelFailed
                };
            }

            try
            {
                var result = await matchmakingLogic.createLobbyAsync(hostUsername, settingsDto);

                if (result.Success)
                {
                    logger.Info("Lobby created successfully with code: {LobbyCode}", result.LobbyCode);
                }
                else
                {
                    logger.Warn("Lobby creation failed.");
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
                trySendCallback(cb => cb.lobbyCreationFailed(Lang.ErrorCommunicationChannelFailed));
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await matchmakingLogic.joinLobbyAsync(username, lobbyId, currentUserCallback);
                    logger.Info("JoinLobby operation completed for lobby: {LobbyId}", lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "JoinLobby operation failed for lobby: {LobbyId}", lobbyId);
                    trySendCallback(cb => cb.lobbyCreationFailed(Lang.GenericServerError));
                    await handleDisconnect();
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
                    logger.Info("LeaveLobby operation completed for lobby: {LobbyId}", lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "LeaveLobby operation failed for lobby: {LobbyId}", lobbyId);
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
                    logger.Info("StartGame operation completed for lobby: {LobbyId}", lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "StartGame operation failed for lobby: {LobbyId}", lobbyId);
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
                    logger.Info("KickPlayer operation completed for lobby: {LobbyId}", lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "KickPlayer operation failed for lobby: {LobbyId}", lobbyId);
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
                    logger.Info("InviteToLobby operation completed for lobby: {LobbyId}", lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "InviteToLobby operation failed for lobby: {LobbyId}", lobbyId);
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
                    logger.Info("ChangeDifficulty operation completed for lobby: {LobbyId}", lobbyId);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "ChangeDifficulty operation failed for lobby: {LobbyId}", lobbyId);
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
                    logger.Info("InviteGuestByEmail operation completed for lobby: {LobbyId}",
                        invitationData.LobbyCode);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "InviteGuestByEmail operation failed for lobby: {LobbyId}",
                        invitationData.LobbyCode);
                }
            });
        }

        public void leaveGame(string username, string lobbyCode)
        {
            Task.Run(async () =>
            {
                await gameSessionManager.handlePlayerLeaveAsync(lobbyCode, username);
            });
        }

        public async Task<GuestJoinResultDto> joinLobbyAsGuest(GuestJoinRequestDto joinRequest)
        {
            string lobbyCode = joinRequest?.LobbyCode ?? "NULL";
            logger.Info("JoinLobbyAsGuest operation started for lobby: {LobbyCode}", lobbyCode);

            if (isDisconnected)
            {
                logger.Warn("JoinLobbyAsGuest rejected: Service instance is marked as disconnected.");
                return new GuestJoinResultDto
                {
                    Success = false,
                    Message = Lang.ErrorServiceConnectionClosing
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
                    logger.Info("JoinLobbyAsGuest operation completed successfully for lobby: {LobbyCode}", lobbyCode);
                }
                else
                {
                    logger.Warn("JoinLobbyAsGuest operation failed for lobby: {LobbyCode}", lobbyCode);
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
                int playerId = getPlayerIdFromContext();
                logger.Debug("RequestPieceDrag: PlayerId {PlayerId}, PieceId {PieceId}, Lobby {LobbyCode}",
                    playerId, pieceId, lobbyCode);

                gameSessionManager.handlePieceDrag(lobbyCode, playerId, pieceId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "RequestPieceDrag operation failed.");
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
                int playerId = getPlayerIdFromContext();
                gameSessionManager.handlePieceMove(lobbyCode, playerId, pieceId, newX, newY);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "RequestPieceMove operation failed.");
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
                int playerId = getPlayerIdFromContext();
                logger.Debug("RequestPieceDrop: PlayerId {PlayerId}, PieceId {PieceId}, Position ({X},{Y}), Lobby {LobbyCode}",
                    playerId, pieceId, newX, newY, lobbyCode);

                Task.Run(async () => await gameSessionManager.handlePieceDrop(lobbyCode, playerId, pieceId, newX, newY));
            }
            catch (Exception ex)
            {
                logger.Error(ex, "RequestPieceDrop operation failed.");
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
                int playerId = getPlayerIdFromContext();
                logger.Debug("RequestPieceRelease: PlayerId {PlayerId}, PieceId {PieceId}, Lobby {LobbyCode}",
                    playerId, pieceId, lobbyCode);

                gameSessionManager.handlePieceRelease(lobbyCode, playerId, pieceId);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "RequestPieceRelease operation failed.");
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
                logger.Error("GetPlayerIdFromContext: Cannot get PlayerId - session not registered.");
                throw new InvalidOperationException("User session is not registered.");
            }

            var player = playerRepository.getPlayerByUsernameAsync(currentUsername).GetAwaiter().GetResult();
            if (player == null)
            {
                logger.Error("GetPlayerIdFromContext: Player not found in database.");
                throw new InvalidOperationException("Player not found in database.");
            }

            currentPlayerId = player.idPlayer;
            return currentPlayerId.Value;
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

                logger.Info("Session and callback registered successfully.");
                return true;
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "CRITICAL: Exception during callback registration.");
                currentUsername = null;
                currentUserCallback = null;
                return false;
            }
        }

        private bool ensureSessionIsRegistered(string username)
        {
            if (isDisconnected)
            {
                logger.Warn("Session check failed: Already marked as disconnected.");
                return false;
            }

            if (!string.IsNullOrEmpty(currentUsername))
            {
                if (currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                logger.Fatal("CRITICAL: Session mismatch detected. Aborting and disconnecting.");
                Task.Run(async () => await handleDisconnect());
                return false;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Fatal("CRITICAL: Method called with null or empty username before session registration.");
                return false;
            }

            return tryRegisterCurrentUserCallback(username);
        }

        private async void channelFaultedOrClosed(object sender, EventArgs e)
        {
            logger.Warn("WCF channel Faulted or Closed. Initiating disconnect.");
            await handleDisconnect();
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= channelFaultedOrClosed;
                commObject.Closed -= channelFaultedOrClosed;
                logger.Debug("Callback event handlers removed.");
            }
        }

        private async Task handleDisconnect()
        {
            if (isDisconnected) return;
            isDisconnected = true;

            string userToDisconnect = currentUsername;
            int? idToDisconnect = currentPlayerId;

            logger.Warn("Disconnect triggered for session. PlayerId: {PlayerId}", idToDisconnect);

            if (OperationContext.Current?.Channel != null)
            {
                OperationContext.Current.Channel.Faulted -= channelFaultedOrClosed;
                OperationContext.Current.Channel.Closed -= channelFaultedOrClosed;
            }

            cleanupCallbackEvents(currentUserCallback as ICommunicationObject);

            if (!string.IsNullOrWhiteSpace(userToDisconnect))
            {
                try
                {
                    if (idToDisconnect.HasValue)
                    {
                        gameSessionManager.handlePlayerDisconnect(userToDisconnect, idToDisconnect.Value);
                    }
                    await Task.Run(() => matchmakingLogic.handleUserDisconnect(userToDisconnect));
                    logger.Info("Disconnect notification sent. PlayerId: {PlayerId}", idToDisconnect);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Exception during disconnect notification.");
                }
            }
            else
            {
                logger.Info("No session associated with this disconnect.");
            }

            currentUsername = null;
            currentUserCallback = null;
            currentPlayerId = null;
        }

        private void setupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= channelFaultedOrClosed;
                commObject.Closed -= channelFaultedOrClosed;
                commObject.Faulted += channelFaultedOrClosed;
                commObject.Closed += channelFaultedOrClosed;

                logger.Debug("Callback event handlers attached.");
            }
            else
            {
                logger.Warn("Cannot setup callback events - communication object is null.");
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
            catch (Exception ex)
            {
                logger.Warn(ex, "Exception sending callback.");
            }
        }
    }
}