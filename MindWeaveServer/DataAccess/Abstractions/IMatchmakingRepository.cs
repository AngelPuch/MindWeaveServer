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
        void updateMatchStatus(Matches match, int newStatusId);
        void updateMatchStartTime(Matches match);
        void updateMatchDifficulty(Matches match, int newDifficultyId);
        Task updatePlayerScoreAsync(int matchId, int playerId, int newScore);
        Task<int> saveChangesAsync();
    }
}