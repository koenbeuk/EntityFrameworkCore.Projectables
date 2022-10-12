using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables.Services
{
    public sealed class ExpressionArgumentReplacer : ExpressionVisitor
    {
        public Dictionary<ParameterExpression, Expression> ParameterArgumentMapping { get; } = new();

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (ParameterArgumentMapping.TryGetValue(node, out var mappedArgument))
            {
                return mappedArgument;
            }

            return base.VisitParameter(node);
        }
    }
}
