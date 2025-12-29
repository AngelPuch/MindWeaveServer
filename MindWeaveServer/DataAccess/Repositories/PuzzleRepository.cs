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
        private readonly Func<MindWeaveDBEntities1> contextFactory;

        public PuzzleRepository(Func<MindWeaveDBEntities1> contextFactory)
        {
            this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task<List<Puzzles>> getAvailablePuzzlesAsync()
        {
            using (var context = contextFactory())
            {
                return await context.Puzzles
                    .OrderBy(p => p.puzzle_id)
                    .AsNoTracking()
                    .ToListAsync();
            }
        }

        public void addPuzzle(Puzzles puzzle)
        {
            if (puzzle == null) throw new ArgumentNullException(nameof(puzzle));

            using (var context = contextFactory())
            {
                context.Puzzles.Add(puzzle);
                context.SaveChanges();
            }
        }

        public async Task<Puzzles> getPuzzleByIdAsync(int puzzleId)
        {
            using (var context = contextFactory())
            {
                return await context.Puzzles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.puzzle_id == puzzleId);
            }
        }

        public async Task<DifficultyLevels> getDifficultyByIdAsync(int difficultyId)
        {
            using (var context = contextFactory())
            {
                return await context.DifficultyLevels.FindAsync(difficultyId);
            }
        }

        public async Task<int> saveChangesAsync()
        {
            return await Task.FromResult(0);
        }
    }
}