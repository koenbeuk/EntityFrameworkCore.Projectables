using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace EntityFrameworkCore.Projectables.Services
{
    public sealed class ProjectableExpressionReplacer : ExpressionVisitor
    {
        readonly IProjectionExpressionResolver _resolver;
        readonly ExpressionArgumentReplacer _expressionArgumentReplacer = new();
        readonly Dictionary<MemberInfo, LambdaExpression?> _projectableMemberCache = new();

        public ProjectableExpressionReplacer(IProjectionExpressionResolver projectionExpressionResolver)
        {
            _resolver = projectionExpressionResolver;
        }

        bool TryGetReflectedExpression(MemberInfo memberInfo, [NotNullWhen(true)] out LambdaExpression? reflectedExpression)
        {
            if (!_projectableMemberCache.TryGetValue(memberInfo, out reflectedExpression))
            {
                var projectableAttribute = memberInfo.GetCustomAttribute<ProjectableAttribute>(false);
                
                reflectedExpression = projectableAttribute is not null 
                    ? _resolver.FindGeneratedExpression(memberInfo)
                    : (LambdaExpression?)null;

                _projectableMemberCache.Add(memberInfo, reflectedExpression);
            }

            return reflectedExpression is not null;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (TryGetReflectedExpression(node.Method, out var reflectedExpression))
            {
                for (var parameterIndex = 0; parameterIndex < reflectedExpression.Parameters.Count; parameterIndex++)
                {
                    var parameterExpession = reflectedExpression.Parameters[parameterIndex];
                    var mappedArgumentExpression = (parameterIndex, node.Object) switch {
                        (0, not null) => node.Object,    
                        (_, not null) => node.Arguments[parameterIndex - 1],
                        (_, null) => node.Arguments.Count > parameterIndex ? node.Arguments[parameterIndex] : null
                    };

                    if (mappedArgumentExpression is not null)
                    {
                        _expressionArgumentReplacer.ParameterArgumentMapping.Add(parameterExpession, mappedArgumentExpression);
                    }
                }
                    
                var updatedBody = _expressionArgumentReplacer.Visit(reflectedExpression.Body);
                _expressionArgumentReplacer.ParameterArgumentMapping.Clear();

                return Visit(
                    updatedBody
                );
            }

            return base.VisitMethodCall(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (TryGetReflectedExpression(node.Member, out var reflectedExpression))
            {
                if (node.Expression is not null)
                {
                    _expressionArgumentReplacer.ParameterArgumentMapping.Add(reflectedExpression.Parameters[0], node.Expression);
                    var updatedBody = _expressionArgumentReplacer.Visit(reflectedExpression.Body);
                    _expressionArgumentReplacer.ParameterArgumentMapping.Clear();

                    return Visit(
                        updatedBody
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