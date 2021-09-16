using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.Extensions;
using EntityFrameworkCore.Projectables.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Projectables.FunctionalTests.Helpers
{
    public class SampleDbContext<TEntity> : DbContext
        where TEntity : class
    {
        readonly CompatibilityMode _compatibilityMode;

        public SampleDbContext(CompatibilityMode compatibilityMode = CompatibilityMode.Limited)
        {
            _compatibilityMode = compatibilityMode;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer("Server=(localdb)\v11.0;Integrated Security=true"); // Fake connection string as we're actually never connecting
            optionsBuilder.UseProjectables(options => {
                options.CompatibilityMode(_compatibilityMode); // Needed by our ComplexModelTests
            });
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TEntity>();
        }
    }
}
