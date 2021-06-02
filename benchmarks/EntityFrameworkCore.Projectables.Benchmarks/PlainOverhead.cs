using System;
using System.Linq;
using BenchmarkDotNet.Attributes;
using EntityFrameworkCore.Projectables.Extensions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Projectables.Benchmarks
{
    public class PlainOverhead
    {
        class TestEntity
        {
            public int Id { get; set; }
        }

        class TestDbContext : DbContext
        {
            readonly bool _useProjectables;

            public TestDbContext(bool useProjectables)
            {
                _useProjectables = useProjectables;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ReadmeSample;Trusted_Connection=True");
                
                if (_useProjectables)
                {
                    optionsBuilder.UseProjectables();
                }
            }

            public DbSet<TestEntity> Entities => Set<TestEntity>();
        }

        [Benchmark(Baseline = true)]
        public void WithoutProjectables()
        {
            using var dbContext = new TestDbContext(false);

            for (int i = 0; i < 10000; i++)
            {
                dbContext.Entities.Select(x => x.Id).ToQueryString();
            }
        }

        [Benchmark]
        public void WithProjectables()
        {
            using var dbContext = new TestDbContext(true);

            for (int i = 0; i < 10000; i++)
            {
                dbContext.Entities.Select(x => x.Id).ToQueryString();
            }
        }

    }
}
