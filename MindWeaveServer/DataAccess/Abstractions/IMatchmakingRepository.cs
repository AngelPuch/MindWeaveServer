using MindWeaveServer.DataAccess;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Abstractions
{
    public interface IMatchmakingRepository
    {
        Task<bool> doesLobbyCodeExistAsync(string lobbyCode);
        Task<Matches> createMatchAsync(Matches match);
        Task<MatchParticipants> addParticipantAsync(MatchParticipants participant);
        Task<Matches> getMatchByLobbyCodeAsync(string lobbyCode);
        Task<MatchParticipants> getParticipantAsync(int matchId, int playerId);
        Task<bool> removeParticipantAsync(MatchParticipants participant);
        Task<int> updateMatchStatusAsync(Matches match, int newStatusId);
        Task<int> updateMatchStartTimeAsync(Matches match);
        Task<int> updateMatchDifficultyAsync(Matches match, int newDifficultyId);
        Task<int> saveChangesAsync();
    }
}