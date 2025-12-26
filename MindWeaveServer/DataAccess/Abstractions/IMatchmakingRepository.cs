using MindWeaveServer.Contracts.DataContracts.Matchmaking;
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
        void AddPlaytimeOnly(int playerId, int minutesPlayed);
        Task updateMatchParticipantStatsAsync(MatchParticipantStatsUpdateDto updateData);
        Task registerExpulsionAsync(ExpulsionDto expulsionData);
        Task finishMatchAsync(int matchId);
        int getMatchDuration(int matchId);
        Task<int> saveChangesAsync();
        Task<Matches> getMatchByIdAsync(int matchId);
        Task updatePlayerScoreAsync(int matchId, int playerId, int score);
    }

}