using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Projections.Services;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace EntityFrameworkCore.Projections.Infrastructure.Internal
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
    public sealed class WrappedQueryCompiler : IQueryCompiler
    {
        readonly ProjectableExpressionReplacer _projectionExpressionReplacer = new();
        readonly IQueryCompiler _queryCompiler;

        public WrappedQueryCompiler(IQueryCompiler queryCompiler)
        {
            _queryCompiler = queryCompiler;
        }

        public Func<QueryContext, TResult> CreateCompiledAsyncQuery<TResult>(Expression query)
            => _queryCompiler.CreateCompiledAsyncQuery<TResult>(PatchQuery(query));

        public Func<QueryContext, TResult> CreateCompiledQuery<TResult>(Expression query)
            => _queryCompiler.CreateCompiledQuery<TResult>(PatchQuery(query));

        public TResult Execute<TResult>(Expression query)
            => _queryCompiler.Execute<TResult>(PatchQuery(query));

        public TResult ExecuteAsync<TResult>(Expression query, CancellationToken cancellationToken)
            => _queryCompiler.ExecuteAsync<TResult>(PatchQuery(query), cancellationToken);

        Expression PatchQuery(Expression expression)
            => _projectionExpressionReplacer.Visit(expression);
        
    }
}
