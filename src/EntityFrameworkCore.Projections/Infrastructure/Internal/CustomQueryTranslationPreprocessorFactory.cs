using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projections.Services;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Projections.Infrastructure.Internal
{
    public class CustomQueryTranslationPreprocessorFactory : IQueryTranslationPreprocessorFactory
    {
        readonly IQueryTranslationPreprocessorFactory _decoratedFactory;
        readonly QueryTranslationPreprocessorDependencies _queryTranslationPreprocessorDependencies;
        readonly ProjectableExpressionReplacer _projectableExpressionReplacer = new();

        public CustomQueryTranslationPreprocessorFactory(IQueryTranslationPreprocessorFactory decoratedFactory, QueryTranslationPreprocessorDependencies queryTranslationPreprocessorDependencies)
        {
            _decoratedFactory = decoratedFactory;
            _queryTranslationPreprocessorDependencies = queryTranslationPreprocessorDependencies;
        }

        public QueryTranslationPreprocessor Create(QueryCompilationContext queryCompilationContext) 
            => new CustomQueryTranslationPreprocessor(_queryTranslationPreprocessorDependencies, queryCompilationContext, _projectableExpressionReplacer);
    }

    public class CustomQueryTranslationPreprocessor : QueryTranslationPreprocessor
    {
        readonly ProjectableExpressionReplacer _projectableExpressionReplacer;

        public CustomQueryTranslationPreprocessor(QueryTranslationPreprocessorDependencies dependencies, QueryCompilationContext queryCompilationContext, ProjectableExpressionReplacer projectableExpressionReplacer) : base(dependencies, queryCompilationContext)
        {
            _projectableExpressionReplacer = projectableExpressionReplacer;
        }

        public override Expression Process(Expression query) 
            => base.Process(_projectableExpressionReplacer.Visit(query));
    }
}
