using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.Extensions;
using EntityFrameworkCore.Projectables.Services;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Projectables.Infrastructure.Internal
{
    public class CustomQueryTranslationPreprocessorFactory : IQueryTranslationPreprocessorFactory
    {
        readonly IQueryTranslationPreprocessorFactory _decoratedFactory;
        readonly QueryTranslationPreprocessorDependencies _queryTranslationPreprocessorDependencies;

        public CustomQueryTranslationPreprocessorFactory(IQueryTranslationPreprocessorFactory decoratedFactory, QueryTranslationPreprocessorDependencies queryTranslationPreprocessorDependencies)
        {
            _decoratedFactory = decoratedFactory;
            _queryTranslationPreprocessorDependencies = queryTranslationPreprocessorDependencies;
        }

        public QueryTranslationPreprocessor Create(QueryCompilationContext queryCompilationContext)
            => new CustomQueryTranslationPreprocessor(_decoratedFactory.Create(queryCompilationContext), _queryTranslationPreprocessorDependencies, queryCompilationContext);
    }

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