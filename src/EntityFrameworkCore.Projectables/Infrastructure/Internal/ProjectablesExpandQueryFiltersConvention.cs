using System.Linq.Expressions;
using EntityFrameworkCore.Projectables.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace EntityFrameworkCore.Projectables.Infrastructure.Internal;

public class ProjectablesExpandQueryFiltersConvention : IModelFinalizingConvention 
{

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var queryFilter = entityType.GetQueryFilter();
            if (queryFilter != null)
            {
                // Expands query filters
                entityType.SetQueryFilter(queryFilter.ExpandProjectables() as LambdaExpression);
            }
        }
    }
}