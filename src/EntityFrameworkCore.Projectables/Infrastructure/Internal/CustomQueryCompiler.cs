using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using EntityFrameworkCore.Projectables.Services;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace EntityFrameworkCore.Projectables.Infrastructure.Internal
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
    public sealed class CustomQueryCompiler : IQueryCompiler
    {
        readonly IQueryCompiler _decoratedQueryCompiler;
        readonly ProjectableExpressionReplacer _projectableExpressionReplacer;

        public CustomQueryCompiler(IQueryCompiler decoratedQueryCompiler)
        {
            _decoratedQueryCompiler = decoratedQueryCompiler;
            _projectableExpressionReplacer = new ProjectableExpressionReplacer(new ProjectionExpressionResolver());
        }

        public Func<QueryContext, TResult> CreateCompiledAsyncQuery<TResult>(Expression query) 
            => _decoratedQueryCompiler.CreateCompiledAsyncQuery<TResult>(Expand(query));
        public Func<QueryContext, TResult> CreateCompiledQuery<TResult>(Expression query) 
            => _decoratedQueryCompiler.CreateCompiledQuery<TResult>(Expand(query));
        public TResult Execute<TResult>(Expression query)
            => _decoratedQueryCompiler.Execute<TResult>(Expand(query));
        public TResult ExecuteAsync<TResult>(Expression query, CancellationToken cancellationToken)
            => _decoratedQueryCompiler.ExecuteAsync<TResult>(Expand(query), cancellationToken);

        Expression Expand(Expression expression)
            => _projectableExpressionReplacer.Visit(expression);
    }
}
