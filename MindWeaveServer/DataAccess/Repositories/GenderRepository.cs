using MindWeaveServer.DataAccess.Abstractions;
using System.Collections.Generic;
using System.Data.Entity;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Repositories
{
    public class GenderRepository : IGenderRepository
    {
        private readonly MindWeaveDBEntities1 context;

        public GenderRepository(MindWeaveDBEntities1 context)
        {
            this.context = context;
        }

        public async Task<List<Gender>> getAllGendersAsync()
        {
            return await context.Gender.AsNoTracking().ToListAsync();
        }
    }
}