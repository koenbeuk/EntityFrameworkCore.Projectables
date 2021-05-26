using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projections.Extensions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Projections.FunctionalTests.Helpers
{
    public class SampleDbContext<TEntity> : DbContext
        where TEntity : class
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer("Server=(localdb)\v11.0;Integrated Security=true"); // Fake connection string as we're actually never connecting
            optionsBuilder.UseProjections();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TEntity>();
        }
    }
}
