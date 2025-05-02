using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using EntityFrameworkCore.Projectables.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Projectables.FunctionalTests;

public class ChangeTrackerTests
{
    public class SqliteSampleDbContext<TEntity> : DbContext
        where TEntity : class
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=test.sqlite");
            optionsBuilder.UseProjectables();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TEntity>();
        }
    }

    public record Entity
    {
        private static int _nextId = 1;
        public const int Computed1DefaultValue = -1;
        public int Id { get; set; } = _nextId++;
        public string? Name { get; set; }

        [Projectable(UseMemberBody = nameof(InternalComputed1))]
        public int Computed1 { get; set; } = Computed1DefaultValue;
        private int InternalComputed1 => Id;

        [Projectable]
        public int Computed2 => Id * 2;
    }

    [Fact]
    public async Task CanQueryAndChangeTrackedEntities()
    {
        using var dbContext = new SqliteSampleDbContext<Entity>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Add(new Entity());
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var entity = await dbContext.Set<Entity>().AsTracking().FirstAsync();
        var entityEntry = dbContext.ChangeTracker.Entries().Single();
        Assert.Same(entityEntry.Entity, entity);
        dbContext.Set<Entity>().Remove(entity);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task CanSaveChanges()
    {
        using var dbContext = new SqliteSampleDbContext<Entity>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Add(new Entity());
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var entity = await dbContext.Set<Entity>().AsTracking().FirstAsync();
        entity.Name = "test";
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var entity2 = await dbContext.Set<Entity>().FirstAsync();
        Assert.Equal("test", entity2.Name);
    }
}