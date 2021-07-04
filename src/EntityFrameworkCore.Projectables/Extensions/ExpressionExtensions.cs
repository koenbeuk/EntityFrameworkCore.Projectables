using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.Services;

namespace EntityFrameworkCore.Projectables.Extensions
{
    public static class ExpressionExtensions
    {
        static ProjectableExpressionReplacer _projectableExpressionReplacer = new ProjectableExpressionReplacer(new ProjectionExpressionResolver());

        [Obsolete("Use ExpandProjectables instead")]
        public static Expression ExpandQuaryables(this Expression expression)
            => ExpandProjectables(expression);

        public static Expression ExpandProjectables(this Expression expression)
            => _projectableExpressionReplacer.Visit(expression);
    }
}
