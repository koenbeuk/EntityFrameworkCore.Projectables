using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.Extensions;

namespace EntityFrameworkCore.Projectables.Services
{
    public sealed class ProjectionExpressionResolver : IProjectionExpressionResolver
    {
        public LambdaExpression FindGeneratedExpression(MemberInfo projectableMemberInfo)
        {
            var reflectedType = projectableMemberInfo.ReflectedType ?? throw new InvalidOperationException("Expected a valid type here");
            var generatedContainingTypeName = ProjectionExpressionClassNameGenerator.GenerateFullName(reflectedType.Namespace, reflectedType.GetNestedTypePath().Select(x => x.Name), projectableMemberInfo.Name);

            var genericArguments = projectableMemberInfo switch {
                MethodInfo methodInfo => methodInfo.GetGenericArguments(),
                _ => null
            };

            var expressionFactoryMethod = reflectedType.Assembly.GetType(generatedContainingTypeName)
                ?.GetMethods()
                ?.FirstOrDefault();

            if (expressionFactoryMethod is not null)
            {
                if (genericArguments is { Length: > 0 })
                {
                    expressionFactoryMethod = expressionFactoryMethod.MakeGenericMethod(genericArguments);
                }

                return expressionFactoryMethod.Invoke(null, null) as LambdaExpression ?? throw new InvalidOperationException("Expected lambda");
            }

            var useMemberBody = projectableMemberInfo.GetCustomAttribute<ProjectableAttribute>()?.UseMemberBody;

            if (useMemberBody is not null)
            {
                var exprProperty = reflectedType.GetProperty(useMemberBody, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var lambda = exprProperty?.GetValue(null) as LambdaExpression;

                if (lambda is not null)
                {
                    if (projectableMemberInfo is PropertyInfo property &&
                        lambda.Parameters.Count == 1 && 
                        lambda.Parameters[0].Type == reflectedType && lambda.ReturnType == property.PropertyType)
                    {
                        return lambda;
                    }
                    else if (projectableMemberInfo is MethodInfo method &&
                        lambda.Parameters.Count == method.GetParameters().Length + 1 &&
                        lambda.Parameters.Last().Type == reflectedType &&
                        !lambda.Parameters.Zip(method.GetParameters(), (a, b) => a.Type != b.ParameterType).Any())
                    {
                        return lambda;
                    }
                }
            }

            var fullName = string.Join(".", Enumerable.Empty<string>()
                .Concat(new[] { reflectedType.Namespace })
                .Concat(reflectedType.GetNestedTypePath().Select(x => x.Name))
                .Concat(new[] { projectableMemberInfo.Name }));

            throw new InvalidOperationException($"Unable to resolve generated expression for {fullName}.") {
                Data = {
                    ["GeneratedContainingTypeName"] = generatedContainingTypeName
                }
            };
        }
    }
}
