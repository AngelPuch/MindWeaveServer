using MindWeaveServer.DataAccess;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Abstractions
{
    public interface IPuzzleRepository
    {
        Task<List<Puzzles>> getAvailablePuzzlesAsync();

        void addPuzzle(Puzzles puzzle);

        Task<int> saveChangesAsync();
    }
}