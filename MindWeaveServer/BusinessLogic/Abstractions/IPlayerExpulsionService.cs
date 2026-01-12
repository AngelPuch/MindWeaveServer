using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface IPlayerExpulsionService
    {
        Task expelPlayerAsync(string lobbyCode, string username, string reason);
    }
}