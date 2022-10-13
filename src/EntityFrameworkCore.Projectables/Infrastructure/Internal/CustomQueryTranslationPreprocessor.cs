using System.Linq.Expressions;
using EntityFrameworkCore.Projectables.Extensions;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Projectables.Infrastructure.Internal
{
    public class CustomQueryTranslationPreprocessor : QueryTranslationPreprocessor
    {
        readonly QueryTranslationPreprocessor _decoratedPreprocessor;

        public CustomQueryTranslationPreprocessor(QueryTranslationPreprocessor decoratedPreprocessor, QueryTranslationPreprocessorDependencies dependencies, QueryCompilationContext queryCompilationContext) : base(dependencies, queryCompilationContext)
        {
            _decoratedPreprocessor = decoratedPreprocessor;
        }

        public override Expression Process(Expression query)
            => _decoratedPreprocessor.Process(query.ExpandProjectables());
    }
}