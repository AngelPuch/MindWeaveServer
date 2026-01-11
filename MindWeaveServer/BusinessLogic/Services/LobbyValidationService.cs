using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.DataContracts.Shared; 
using MindWeaveServer.Resources;
using NLog;
using System;
using System.Linq;

namespace MindWeaveServer.BusinessLogic.Services
{
    public class LobbyValidationService : ILobbyValidationService
    {

        private readonly IGameStateManager gameStateManager;
        private readonly GameSessionManager gameSessionManager;
        private readonly LobbyModerationManager moderationManager;

        private const int MAX_PLAYERS_PER_LOBBY = 4;
        private const int MIN_PLAYERS_TO_START_GAME = 2;

  

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
                return ValidationResult.failure(MessageCodes.MATCH_USERNAME_REQUIRED);
            }

            if (isUserBusy(hostUsername, null))
            {
                return ValidationResult.failure(MessageCodes.MATCH_USER_ALREADY_BUSY);
            }

            return ValidationResult.success();
        }

        public ValidationResult canJoinLobby(LobbyStateDto lobby, string username, string inputCode)
        {
            if (lobby == null || !lobby.LobbyId.Equals(inputCode, StringComparison.Ordinal))
            {
                return ValidationResult.failure(
                    MessageCodes.MATCH_LOBBY_NOT_FOUND,
                    inputCode
                );
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return ValidationResult.failure(
                    MessageCodes.MATCH_USERNAME_REQUIRED
                );
            }

            if (moderationManager.isBanned(lobby.LobbyId, username))
            {
                return ValidationResult.failure(
                    MessageCodes.MATCH_USER_BANNED
                );
            }

            if (lobby.Players.Count >= MAX_PLAYERS_PER_LOBBY)
            {
                return ValidationResult.failure(
                    MessageCodes.MATCH_LOBBY_FULL
                );
            }

            if (lobby.Players.Contains(username, StringComparer.OrdinalIgnoreCase))
            {
                return ValidationResult.failure(
                    MessageCodes.MATCH_PLAYER_ALREADY_IN_LOBBY,
                    username
                );
            }

            if (isUserBusy(username, lobby.LobbyId))
            {
                return ValidationResult.failure(
                    MessageCodes.MATCH_USER_ALREADY_BUSY
                );
            }

            return ValidationResult.success();

        }

        public ValidationResult canInvitePlayer(LobbyStateDto lobby, string targetUsername)
        {
            if (lobby == null)
            {
                return ValidationResult.failure(MessageCodes.MATCH_LOBBY_NOT_FOUND);
            }
            if (string.IsNullOrWhiteSpace(targetUsername))
            {
                return ValidationResult.failure(MessageCodes.MATCH_USERNAME_REQUIRED);
            }
            if (moderationManager.isBanned(lobby.LobbyId, targetUsername))
            {
                return ValidationResult.failure(MessageCodes.MATCH_CANNOT_INVITE_BANNED, targetUsername);
            }
            if (!gameStateManager.isUserConnected(targetUsername))
            {
                return ValidationResult.failure(MessageCodes.MATCH_USER_NOT_ONLINE, targetUsername);
            }
            if (lobby.Players.Count >= MAX_PLAYERS_PER_LOBBY)
            {
                return ValidationResult.failure(MessageCodes.MATCH_LOBBY_FULL);
            }
            if (lobby.Players.Contains(targetUsername, StringComparer.OrdinalIgnoreCase))
            { 
                return ValidationResult.failure(MessageCodes.MATCH_PLAYER_ALREADY_IN_LOBBY, targetUsername);
            }
            if (isUserBusy(targetUsername, lobby.LobbyId))
            {
                return ValidationResult.failure(MessageCodes.MATCH_USER_ALREADY_BUSY, targetUsername);
            }

            return ValidationResult.success();
        }

        public ValidationResult canStartGame(LobbyStateDto lobby, string requestUsername)
        {
            if (lobby == null) { return ValidationResult.failure(MessageCodes.MATCH_LOBBY_NOT_FOUND); }

            if (!lobby.HostUsername.Equals(requestUsername, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.failure(MessageCodes.MATCH_NOT_HOST);
            }
            if (lobby.Players.Count < MIN_PLAYERS_TO_START_GAME)
            {
                return ValidationResult.failure(MessageCodes.MATCH_NOT_ENOUGH_PLAYERS);
            }
            if (lobby.Players.Count > MAX_PLAYERS_PER_LOBBY)
            {
                return ValidationResult.failure(MessageCodes.MATCH_NOT_ENOUGH_PLAYERS);
            }
            return ValidationResult.success();
        }

        public ValidationResult canKickPlayer(LobbyStateDto lobby, string hostUsername, string targetUsername)
        {
            if (lobby == null) { return ValidationResult.failure(MessageCodes.MATCH_LOBBY_NOT_FOUND); }

            if (!lobby.HostUsername.Equals(hostUsername, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.failure(MessageCodes.MATCH_NOT_HOST);
            }

            if (hostUsername.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.failure(MessageCodes.MATCH_HOST_CANNOT_KICK_SELF);
            }

            if (!lobby.Players.Contains(targetUsername, StringComparer.OrdinalIgnoreCase))
            {
                return ValidationResult.failure(MessageCodes.MATCH_PLAYER_NOT_FOUND);
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

            foreach (var lobby in gameStateManager.ActiveLobbies.Values)
            {

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