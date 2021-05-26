using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projections.Services
{
    public class ExpressionArgumentReplacer : ExpressionVisitor
    {
        readonly Expression _targetExpression;

        public ExpressionArgumentReplacer(Expression targetExpression)
        {
            _targetExpression = targetExpression;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node.Name == "projectionTarget")
            {
                return _targetExpression;
            }
            else
            {
                return base.VisitParameter(node);
            }
        }
    }
}
