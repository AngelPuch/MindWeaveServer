using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface IDisconnectionHandler
    {
        Task handleFullDisconnectionAsync(string username, string reason);
    }
}