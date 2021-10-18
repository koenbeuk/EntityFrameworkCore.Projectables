using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Projectables.FunctionalTests.ExtensionMethods
{
    public static class EntityExtensions
    {
        [Projectable]
        public static int Squared(this int i) => i * i;

        [Projectable]
        public static int Foo(this Entity entity) => entity.Id + 1;

        [Projectable]
        public static int Foo2(this Entity entity) => entity.Foo() + 1;

        [Projectable]
        public static Entity? LeadingEntity(this Entity entity, DbContext dbContext)
            => dbContext.Set<Entity>().Where(y => y.Id > entity.Id).FirstOrDefault();
    }
}
