using System.Linq;

namespace EntityFrameworkCore.Projectables.FunctionalTests.Generics
{
    public static class EntityExtensions
    {
        [Projectable]
        public static TEntity? DefaultIfIdIsNegative<TEntity>(this TEntity entity) where TEntity : IEntity
            => entity.Id >= 0 ? entity : default;
    }
}
