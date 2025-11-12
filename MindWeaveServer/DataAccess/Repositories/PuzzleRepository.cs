using MindWeaveServer.DataAccess.Abstractions;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Repositories
{
    public class PuzzleRepository : IPuzzleRepository
    {
        private readonly MindWeaveDBEntities1 context;

        public PuzzleRepository(MindWeaveDBEntities1 context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<List<Puzzles>> getAvailablePuzzlesAsync()
        {
            return await context.Puzzles
                .OrderBy(p => p.puzzle_id)
                .AsNoTracking()
                .ToListAsync();
        }

        public void addPuzzle(Puzzles puzzle)
        {
            if (puzzle == null)
            {
                throw new ArgumentNullException(nameof(puzzle));
            }
            context.Puzzles.Add(puzzle);
        }

        public async Task<int> saveChangesAsync()
        {
            return await context.SaveChangesAsync();
        }

        public async Task<Puzzles> getPuzzleByIdAsync(int puzzleId)
        {
            return await context.Puzzles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.puzzle_id == puzzleId);
        }
    }
}