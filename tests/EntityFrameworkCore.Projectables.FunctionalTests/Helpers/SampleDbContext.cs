using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.Extensions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Projectables.FunctionalTests.Helpers
{
    public class SampleDbContext<TEntity> : DbContext
        where TEntity : class
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer("Server=(localdb)\v11.0;Integrated Security=true"); // Fake connection string as we're actually never connecting
            optionsBuilder.UseProjectables();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TEntity>();
        }
    }
}
