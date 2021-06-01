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
        readonly IEnumerable<(ParameterExpression parameter, Expression argument)>? _parameterArgumentMapping;

        public ExpressionArgumentReplacer(IEnumerable<(ParameterExpression, Expression)>? parameterArgumentMapping = null)
        {
            _parameterArgumentMapping = parameterArgumentMapping;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            var mappedArgument = _parameterArgumentMapping?
                .Where(x => x.parameter == node)
                .Select(x => x.argument)
                .FirstOrDefault();

            if (mappedArgument is not null)
            {
                return mappedArgument;
            }

            return base.VisitParameter(node);
        }
    }
}
