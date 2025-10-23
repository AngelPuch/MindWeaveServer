using System.Collections.Generic;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Abstractions
{
    public interface IGenderRepository
    {
        Task<List<Gender>> getAllGendersAsync();
    }
}