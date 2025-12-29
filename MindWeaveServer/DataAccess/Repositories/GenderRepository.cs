using MindWeaveServer.DataAccess.Abstractions;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Threading.Tasks;

namespace MindWeaveServer.DataAccess.Repositories
{
    public class GenderRepository : IGenderRepository
    { 
        private readonly Func<MindWeaveDBEntities1> contextFactory;

        public GenderRepository(Func<MindWeaveDBEntities1> contextFactory)
        {
            this.contextFactory = contextFactory;
        }

        public async Task<List<Gender>> getAllGendersAsync()
        {
            using (var context = contextFactory())
            {
                return await context.Gender.AsNoTracking().ToListAsync();
            }
        }
    }
}