using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projections.Services
{
    public sealed class ParameterExtractor : ExpressionVisitor
    {
        List<ParameterExpression>? _extractedParameters = null;

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (_extractedParameters is null)
            {
                _extractedParameters = new List<ParameterExpression>();
            }

            _extractedParameters.Add(node);
            return base.VisitParameter(node);
        }

        public IEnumerable<ParameterExpression> ExtractedParameters
            => _extractedParameters ?? Enumerable.Empty<ParameterExpression>();
    }
}
