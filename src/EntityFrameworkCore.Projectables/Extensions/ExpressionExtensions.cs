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
        [Obsolete("Use ExpandProjectables instead")]
        public static Expression ExpandQuaryables(this Expression expression)
            => ExpandProjectables(expression);

        /// <summary>
        /// Replaces all calls to properties and methods that are marked with the <C>Projectable</C> attribute with their respective expression tree
        /// </summary>
        public static Expression ExpandProjectables(this Expression expression)
            => new ProjectableExpressionReplacer(new ProjectionExpressionResolver()).Visit(expression);
    }
}
