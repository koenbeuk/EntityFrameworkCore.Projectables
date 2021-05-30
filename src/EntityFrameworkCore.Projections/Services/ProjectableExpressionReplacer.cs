using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projections.Services
{
    public class ProjectableExpressionReplacer : ExpressionVisitor
    {
        readonly ProjectionExpressionResolver _resolver = new();

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.GetCustomAttributes(true).OfType<ProjectableAttribute>().Any())
            {
                var reflectedExpression = _resolver.FindGeneratedExpression(node.Method);

                var parameterArgumentMapping = node.Object is not null
                    ? Enumerable.Repeat((reflectedExpression.Parameters[0], node.Object), 1)
                    : Enumerable.Empty<(ParameterExpression, Expression)>();

                if (reflectedExpression.Parameters.Count > 0)
                {
                    parameterArgumentMapping = parameterArgumentMapping.Concat(
                        node.Object is not null 
                            ? reflectedExpression.Parameters.Skip(1).Zip(node.Arguments)
                            : reflectedExpression.Parameters.Zip(node.Arguments)
                    );
                }

                var expressionArgumentReplacer = new ExpressionArgumentReplacer(parameterArgumentMapping);
                return Visit(
                    expressionArgumentReplacer.Visit(reflectedExpression.Body)
                );
            }

            return base.VisitMethodCall(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.GetCustomAttributes(false).OfType<ProjectableAttribute>().Any())
            {
                var reflectedExpression = _resolver.FindGeneratedExpression(node.Member);
                
                if (node.Expression is not null)
                {
                    var expressionArgumentReplacer = new ExpressionArgumentReplacer(
                        Enumerable.Repeat((reflectedExpression.Parameters[0], node.Expression), 1)
                    );
                    return Visit(
                        expressionArgumentReplacer.Visit(reflectedExpression.Body)
                    );
                }
                else
                {
                    return Visit(
                        reflectedExpression.Body
                    );
                }
            }

            return base.VisitMember(node);
        }
    }
}