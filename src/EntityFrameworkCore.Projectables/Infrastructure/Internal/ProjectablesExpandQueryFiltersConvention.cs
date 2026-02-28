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
#if NET10_0_OR_GREATER
            var queryFilter = entityType.GetDeclaredQueryFilters();
            
            foreach (var filter in queryFilter)
            {
                if (filter.Expression == null)
                {
                    continue;
                }
                
                var expandedExpression = filter.Expression.ExpandProjectables() as LambdaExpression;
                
                // Expands query filters
                if (filter.Key != null)
                {
                    entityType.SetQueryFilter(filter.Key, expandedExpression);
                }
                else
                {
                    entityType.SetQueryFilter(expandedExpression);
                }
            }
#else
            var queryFilter = entityType.GetQueryFilter();
            if (queryFilter != null)
            {
                // Expands query filters
                entityType.SetQueryFilter(queryFilter.ExpandProjectables() as LambdaExpression);
            }
#endif
        }
    }
}