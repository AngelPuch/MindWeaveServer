using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Resources;
using NLog;
using System;
using System.Linq;

namespace MindWeaveServer.BusinessLogic.Services
{
    public class LobbyValidationService : ILobbyValidationService
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IGameStateManager gameStateManager;
        private readonly GameSessionManager gameSessionManager;
        private readonly LobbyModerationManager moderationManager;

        private const int MAX_PLAYERS_PER_LOBBY = 4;
        private const int MIN_PLAYERS_TO_START_GAME = 2;

        private const string ERROR_USER_BUSY_GENERIC = "User is already in a game or lobby.";
        private const string ERROR_USER_BANNED = "You are banned from this lobby.";
        private const string ERROR_USER_BUSY_SPECIFIC = "You are already in another game or lobby.";
        private const string ERROR_HOST_CANNOT_KICK_SELF = "Host cannot kick themselves.";

        public LobbyValidationService(
            IGameStateManager gameStateManager,
            GameSessionManager gameSessionManager,
            LobbyModerationManager moderationManager)
        {
            this.gameStateManager = gameStateManager;
            this.gameSessionManager = gameSessionManager;
            this.moderationManager = moderationManager;
        }

        public ValidationResult canCreateLobby(string hostUsername)
        {
            if (string.IsNullOrWhiteSpace(hostUsername))
            {
                return ValidationResult.failure(Lang.ValidationUsernameRequired);
            }

            if (isUserBusy(hostUsername, null))
            {
                return ValidationResult.failure(ERROR_USER_BUSY_GENERIC);
            }

            return ValidationResult.success();
        }

        public ValidationResult canJoinLobby(LobbyStateDto lobby, string username, string inputCode)
        {
            if (lobby == null)
            {
                return ValidationResult.failure(string.Format(Lang.lobbyNotFoundOrInactive, inputCode));
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return ValidationResult.failure(Lang.ValidationUsernameRequired);
            }

            if (!lobby.LobbyId.Equals(inputCode, StringComparison.Ordinal))
            {
                return ValidationResult.failure(string.Format(Lang.lobbyNotFoundOrInactive, inputCode));
            }

            if (moderationManager.isBanned(lobby.LobbyId, username))
            {
                return ValidationResult.failure(ERROR_USER_BANNED);
            }

            if (lobby.Players.Count >= MAX_PLAYERS_PER_LOBBY)
            {
                return ValidationResult.failure(string.Format(Lang.LobbyIsFull, lobby.LobbyId));
            }

            if (lobby.Players.Contains(username, StringComparer.OrdinalIgnoreCase))
            {
                return ValidationResult.failure(string.Format(Lang.PlayerAlreadyInLobby, username));
            }

            if (isUserBusy(username, lobby.LobbyId))
            {
                return ValidationResult.failure(ERROR_USER_BUSY_SPECIFIC);
            }

            return ValidationResult.success();
        }

        public ValidationResult canInvitePlayer(LobbyStateDto lobby, string targetUsername)
        {
            if (lobby == null)
            {
                return ValidationResult.failure(Lang.LobbyDataNotFound);
            }

            if (string.IsNullOrWhiteSpace(targetUsername))
            {
                return ValidationResult.failure(Lang.ValidationUsernameRequired);
            }

            if (!gameStateManager.isUserConnected(targetUsername))
            {
                return ValidationResult.failure($"{targetUsername} {Lang.ErrorUserNotOnline}");
            }

            if (lobby.Players.Count >= MAX_PLAYERS_PER_LOBBY)
            {
                return ValidationResult.failure(string.Format(Lang.LobbyIsFull, lobby.LobbyId));
            }

            if (lobby.Players.Contains(targetUsername, StringComparer.OrdinalIgnoreCase))
            {
                return ValidationResult.failure(string.Format(Lang.PlayerAlreadyInLobby, targetUsername));
            }

            if (isUserBusy(targetUsername, lobby.LobbyId))
            {
                return ValidationResult.failure(string.Format(Lang.ValidationUserAlreadyInGame, targetUsername));
            }

            return ValidationResult.success();
        }

        public ValidationResult canStartGame(LobbyStateDto lobby, string requestUsername)
        {
            if (lobby == null) return ValidationResult.failure(Lang.LobbyDataNotFound);

            if (!lobby.HostUsername.Equals(requestUsername, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.failure(Lang.notHost);
            }

            if (lobby.Players.Count < MIN_PLAYERS_TO_START_GAME) return ValidationResult.failure(Lang.NotEnoughPlayersToStart);

            if (lobby.Players.Count > MAX_PLAYERS_PER_LOBBY)
            {
                return ValidationResult.failure(Lang.NotEnoughPlayersToStart);
            }

            return ValidationResult.success();
        }

        public ValidationResult canKickPlayer(LobbyStateDto lobby, string hostUsername, string targetUsername)
        {
            if (lobby == null) return ValidationResult.failure(Lang.LobbyDataNotFound);

            if (!lobby.HostUsername.Equals(hostUsername, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.failure(Lang.notHost);
            }

            if (hostUsername.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.failure(ERROR_HOST_CANNOT_KICK_SELF);
            }

            if (!lobby.Players.Contains(targetUsername, StringComparer.OrdinalIgnoreCase))
            {
                return ValidationResult.failure(Lang.ErrorPlayerNotFound);
            }

            return ValidationResult.success();
        }

        private bool isUserBusy(string username, string currentLobbyId)
        {
            bool inGame = gameSessionManager.isPlayerInAnySession(username);
            if (inGame)
            {
                return true;
            }

            var lobbies = gameStateManager.ActiveLobbies;
            foreach (var kvp in lobbies)
            {
                var lobby = kvp.Value;

                if (currentLobbyId != null && lobby.LobbyId == currentLobbyId) continue;

                bool inLobby = false;
                lock (lobby)
                {
                    inLobby = lobby.Players.Contains(username, StringComparer.OrdinalIgnoreCase);
                }

                if (inLobby)
                {
                    return true;
                }
            }
            return false;
        }
    }
}