using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using EntityFrameworkCore.Projectables.Extensions;
using EntityFrameworkCore.Projectables.Infrastructure;

namespace EntityFrameworkCore.Projectables.Benchmarks.Helpers
{
    class TestDbContext : DbContext
    {
        readonly bool _useProjectables;
        readonly bool _useFullCompatibiltyMode;

        public TestDbContext(bool useProjectables, bool useFullCompatibiltyMode = true)
        {
            _useProjectables = useProjectables;
            _useFullCompatibiltyMode = useFullCompatibiltyMode;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ReadmeSample;Trusted_Connection=True");

            if (_useProjectables)
            {
                optionsBuilder.UseProjectables(projectableOptions => {
                    projectableOptions.CompatibilityMode(_useFullCompatibiltyMode ? CompatibilityMode.Full : CompatibilityMode.Limited);
                });
            }
        }

        public DbSet<TestEntity> Entities => Set<TestEntity>();
    }
}
