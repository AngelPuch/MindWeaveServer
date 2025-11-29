using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface IPlayerExpulsionService
    {
        /// <summary>
        /// Expels a player from the specified lobby.
        /// </summary>
        /// <param name="lobbyCode">The lobby code.</param>
        /// <param name="username">The username of the player to expel.</param>
        /// <param name="reason">The reason for expulsion (e.g., "Profanity").</param>
        Task expelPlayerAsync(string lobbyCode, string username, string reason);
    }
}