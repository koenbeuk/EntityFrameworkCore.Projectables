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
                var reflectedExpressionFactory = _resolver.FindGeneratedExpressionFactory(node.Method);
                var reflectedExpresssion = reflectedExpressionFactory(node.Arguments);
                if (reflectedExpresssion is not null)
                {
                    if (node.Object is not null)
                    {
                        var expressionArgumentReplacer = new ExpressionArgumentReplacer(node.Object);
                        return expressionArgumentReplacer.Visit(reflectedExpresssion.Body);
                    }
                    else
                    {
                        return reflectedExpresssion.Body;
                    }
                }
            }

            return base.VisitMethodCall(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.GetCustomAttributes(true).OfType<ProjectableAttribute>().Any())
            {
                var reflectedExpressionFactory = _resolver.FindGeneratedExpressionFactory(node.Member);
                var reflectedExpression = reflectedExpressionFactory(null);
                if (reflectedExpression is not null)
                {
                    if (node.Expression is not null)
                    {
                        var expressionArgumentReplacer = new ExpressionArgumentReplacer(node.Expression);
                        return expressionArgumentReplacer.Visit(reflectedExpression.Body);
                    }
                    else
                    {
                        return reflectedExpression.Body;
                    }
                }
            }

            return base.VisitMember(node);
        }
    }
}