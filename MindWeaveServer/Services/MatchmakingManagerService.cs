using MindWeaveServer.BusinessLogic;
using MindWeaveServer.Contracts.DataContracts.Matchmaking;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class MatchmakingManagerService : IMatchmakingManager
    {
        private readonly MatchmakingLogic matchmakingLogic;
        private static readonly ConcurrentDictionary<string, LobbyStateDto> activeLobbies = new ConcurrentDictionary<string, LobbyStateDto>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, IMatchmakingCallback> userCallbacks = new ConcurrentDictionary<string, IMatchmakingCallback>(StringComparer.OrdinalIgnoreCase);

        private string currentUsername = null;
        private IMatchmakingCallback currentUserCallback = null;

        public MatchmakingManagerService()
        {
            Console.WriteLine("==> MatchmakingManagerService INSTANCE CONSTRUCTOR called.");
            this.matchmakingLogic = new MatchmakingLogic(activeLobbies, userCallbacks);

            if (OperationContext.Current != null && OperationContext.Current.Channel != null)
            {
                OperationContext.Current.Channel.Faulted += Channel_FaultedOrClosed;
                OperationContext.Current.Channel.Closed += Channel_FaultedOrClosed;
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] WARNING: MatchmakingManagerService created without OperationContext!");
            }
        }

        public async Task<LobbyCreationResultDto> createLobby(string hostUsername, LobbySettingsDto settingsDto)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: createLobby ENTRY for user: {hostUsername}");
            currentUserCallback = getCurrentCallbackChannel(hostUsername);
            if (currentUserCallback == null) { /* Error crítico */ return new LobbyCreationResultDto { /*...*/ }; }

            if (string.IsNullOrWhiteSpace(hostUsername) || settingsDto == null) { /* Error Input */ return new LobbyCreationResultDto { /*...*/ }; }

            try { return await matchmakingLogic.createLobbyAsync(hostUsername, settingsDto); }
            catch (Exception ex) { /* Log Fatal */ return new LobbyCreationResultDto { /*...*/ }; }
        }

        private IMatchmakingCallback getCurrentCallbackChannel(string username)
        {
            if (OperationContext.Current == null) { /* Log */ return null; }
            var currentCallback = OperationContext.Current.GetCallbackChannel<IMatchmakingCallback>();
            if (currentCallback != null && !string.IsNullOrEmpty(username))
            {
                currentUsername = username;
                userCallbacks.AddOrUpdate(username, currentCallback, (key, existingVal) =>
                {
                    var existingComm = existingVal as ICommunicationObject;
                    if (existingComm == null || existingComm.State != CommunicationState.Opened)
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:O}] Updating stale matchmaking callback for {username}.");
                        if (existingComm != null) cleanupCallbackEvents(existingComm);
                        return currentCallback;
                    }
                    return existingVal;
                });

                if (userCallbacks.TryGetValue(username, out currentCallback))
                {
                    setupCallbackEvents(currentCallback as ICommunicationObject);
                }
                else { Console.WriteLine($"!!! CRITICAL: Failed to retrieve matchmaking callback for {username} after AddOrUpdate."); }
            }
            return currentCallback;
        }

        private void CommObject_FaultedOrClosed(object sender, EventArgs e)
        {
            IMatchmakingCallback callbackChannel = sender as IMatchmakingCallback;
            if (callbackChannel != null)
            {
                var userEntry = userCallbacks.FirstOrDefault(pair => pair.Value == callbackChannel);
                if (!string.IsNullOrEmpty(userEntry.Key))
                {
                    Console.WriteLine($"Callback channel for {userEntry.Key} has Faulted or Closed.");
                    removeCallbackChannel(userEntry.Key);
                }
                else { /* Log Warning */ }

                ICommunicationObject commObject = sender as ICommunicationObject;
                if (commObject != null)
                {
                    commObject.Faulted -= CommObject_FaultedOrClosed;
                    commObject.Closed -= CommObject_FaultedOrClosed;
                }
            }
        }

        private void removeCallbackChannel(string username)
        {
            if (!string.IsNullOrEmpty(username))
            {
                if (userCallbacks.TryRemove(username, out IMatchmakingCallback removedChannel))
                {
                    Console.WriteLine($"Callback channel explicitly removed for user: {username}");
                    matchmakingLogic.handleUserDisconnect(username);

                    ICommunicationObject commObject = removedChannel as ICommunicationObject;
                    if (commObject != null)
                    {
                        commObject.Faulted -= CommObject_FaultedOrClosed;
                        commObject.Closed -= CommObject_FaultedOrClosed;
                    }
                }
            }
        }

        public void joinLobby(string username, string lobbyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: joinLobby ENTRY for user: {username}, lobby: {lobbyId}");
            currentUserCallback = getCurrentCallbackChannel(username);
            if (currentUserCallback == null) { /* Error crítico */ return; }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId)) { /* Log */ return; }
            try { matchmakingLogic.joinLobby(username, lobbyId); }
            catch (Exception ex) { /* Log */ }
        }

        public void leaveLobby(string username, string lobbyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: leaveLobby ENTRY for user: {username}, lobby: {lobbyId}");
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(lobbyId)) { /* Log */ return; }
            try { matchmakingLogic.leaveLobby(username, lobbyId); }
            catch (Exception ex) { /* Log */ }
        }

        public void startGame(string hostUsername, string lobbyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: startGame ENTRY by: {hostUsername}, lobby: {lobbyId}");
            if (string.IsNullOrWhiteSpace(hostUsername) || string.IsNullOrWhiteSpace(lobbyId)) { /* Log */ return; }
            try { matchmakingLogic.startGame(hostUsername, lobbyId); }
            catch (Exception ex) { /* Log */ }
        }

        public void kickPlayer(string hostUsername, string playerToKickUsername, string lobbyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: kickPlayer ENTRY by: {hostUsername}, kicking: {playerToKickUsername}, lobby: {lobbyId}");
            if (string.IsNullOrWhiteSpace(hostUsername) || string.IsNullOrWhiteSpace(playerToKickUsername) || string.IsNullOrWhiteSpace(lobbyId)) { /* Log */ return; }
            try { matchmakingLogic.kickPlayer(hostUsername, playerToKickUsername, lobbyId); }
            catch (Exception ex) { /* Log */ }
        }
        public void inviteToLobby(string inviterUsername, string invitedUsername, string lobbyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: inviteToLobby ENTRY from: {inviterUsername}, to: {invitedUsername}, lobby: {lobbyId}");
            if (string.IsNullOrWhiteSpace(inviterUsername) || string.IsNullOrWhiteSpace(invitedUsername) || string.IsNullOrWhiteSpace(lobbyId)) { /* Log */ return; }
            try { matchmakingLogic.inviteToLobby(inviterUsername, invitedUsername, lobbyId); }
            catch (Exception ex) { /* Log */ }
        }


        public void changeDifficulty(string hostUsername, string lobbyId, int newDifficultyId)
        {
            Console.WriteLine($"{DateTime.UtcNow:O} ==> Service: changeDifficulty ENTRY by: {hostUsername}, lobby: {lobbyId}, newDiff: {newDifficultyId}");
            // No necesita registrar callback aquí
            if (string.IsNullOrWhiteSpace(hostUsername) || string.IsNullOrWhiteSpace(lobbyId) || newDifficultyId <= 0) { /* Log */ return; }
            try { matchmakingLogic.changeDifficulty(hostUsername, lobbyId, newDifficultyId); }
            catch (Exception ex) { /* Log */ }
        }

        private void Channel_FaultedOrClosed(object sender, EventArgs e)
        {
            Console.WriteLine($"[{DateTime.UtcNow:O}] Matchmaking Channel Faulted/Closed for: {currentUsername ?? "UNKNOWN"}");
            if (!string.IsNullOrEmpty(currentUsername))
            {
                cleanupAndNotifyDisconnect(currentUsername);
            }
            cleanupCallbackEvents(sender as ICommunicationObject);
        }

        private void cleanupAndNotifyDisconnect(string username)
        {
            if (string.IsNullOrEmpty(username)) return;

            if (userCallbacks.TryRemove(username, out IMatchmakingCallback removedChannel))
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] Matchmaking callback removed for {username}.");
                cleanupCallbackEvents(removedChannel as ICommunicationObject); 

                matchmakingLogic.handleUserDisconnect(username);
            }
            else { Console.WriteLine($"[{DateTime.UtcNow:O}] Attempted matchmaking cleanup for {username}, but not found."); }
        }

        private void cleanupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= Channel_FaultedOrClosed;
                commObject.Closed -= Channel_FaultedOrClosed;
            }
        }
        private void setupCallbackEvents(ICommunicationObject commObject)
        {
            if (commObject != null)
            {
                commObject.Faulted -= Channel_FaultedOrClosed;
                commObject.Closed -= Channel_FaultedOrClosed;
                commObject.Faulted += Channel_FaultedOrClosed;
                commObject.Closed += Channel_FaultedOrClosed;
            }
        }




    } 
}