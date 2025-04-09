using EntityFrameworkCore.Projectables.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Projectables.FunctionalTests.Helpers
{
    public class SampleUserWithGlobalQueryFilterDbContext : SampleDbContext<ComplexModelTests.User>
    {
        public SampleUserWithGlobalQueryFilterDbContext(CompatibilityMode compatibilityMode = CompatibilityMode.Full) : base(compatibilityMode)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            modelBuilder.Entity<ComplexModelTests.User>(b => {
                b.HasQueryFilter(u => u.LastOrder.Id > 100);
            });
        }
    }
}
