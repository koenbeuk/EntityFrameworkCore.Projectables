using EntityFrameworkCore.Projections.Services;
using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projections.Infrastructure.Internal
{
    public class WrappedQueryTranslationPreprocessorFactory : IQueryTranslationPreprocessorFactory
    {
        readonly IQueryTranslationPreprocessorFactory _originalFactory;
        readonly QueryTranslationPreprocessorDependencies _dependencies;

        public WrappedQueryTranslationPreprocessorFactory(IQueryTranslationPreprocessorFactory originalFactory, QueryTranslationPreprocessorDependencies dependencies)
        {
            _originalFactory = originalFactory;
            _dependencies = dependencies;
        }

        public QueryTranslationPreprocessor Create(QueryCompilationContext queryCompilationContext)
        {
            var originalPreprocessor = _originalFactory.Create(queryCompilationContext);

            return new WrappedQueryTranslationPreprocessor(originalPreprocessor, _dependencies, queryCompilationContext);
        }
    }

    public class WrappedQueryTranslationPreprocessor : QueryTranslationPreprocessor
    {
        readonly ProjectableExpressionReplacer _projectableExpressionReplacer;
        readonly QueryTranslationPreprocessor _originalPreprocessor;

        public WrappedQueryTranslationPreprocessor(QueryTranslationPreprocessor originalPreprocessor, QueryTranslationPreprocessorDependencies dependencies, QueryCompilationContext queryCompilationContext) : base(dependencies, queryCompilationContext)
        {
            _originalPreprocessor = originalPreprocessor;
            _projectableExpressionReplacer = new ProjectableExpressionReplacer();
        }

        public override Expression NormalizeQueryableMethod(Expression expression)
        {
            return _originalPreprocessor.NormalizeQueryableMethod(expression);
        }

        public override Expression Process(Expression query)
        {
            query = _projectableExpressionReplacer.Visit(query);

            return _originalPreprocessor.Process(query);
        }
    }
}
