using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projections.Extensions;

namespace EntityFrameworkCore.Projections.Services
{
    public sealed class ProjectionExpressionResolver
    {
        readonly ConcurrentDictionary<string, Func<IReadOnlyCollection<Expression>?, LambdaExpression>> _lookupCache = new();

        public Func<IReadOnlyCollection<Expression>?, LambdaExpression> FindGeneratedExpressionFactory(MemberInfo projectableMemberInfo)
        {
            var reflectedType = projectableMemberInfo.ReflectedType ?? throw new InvalidOperationException("Expected a valid type here");
            var generatedContainingTypeName = ProjectionExpressionClassNameGenerator.GenerateFullName(reflectedType.Namespace, reflectedType.GetNestedTypePath().Select(x => x.Name), projectableMemberInfo.Name);

            return _lookupCache.GetOrAdd(generatedContainingTypeName, _ => {
                var expressionFactoryMethod = reflectedType.Assembly
                    .GetTypes()
                    .Where(x => x.FullName == generatedContainingTypeName)
                    .SelectMany(x => x.GetMethods())
                    .FirstOrDefault();

                if (expressionFactoryMethod is null)
                {
                    throw new InvalidOperationException("Unable to resolve generated expression") {
                        Data = {
                            ["GeneratedContainingTypeName"] = generatedContainingTypeName
                        }
                    };
                }

                return new Func<IReadOnlyCollection<Expression>?, LambdaExpression>(argumentExpressions => 
                {
                    if (argumentExpressions is null || argumentExpressions.Count is 0)
                    {
                        return expressionFactoryMethod.Invoke(null, null) as LambdaExpression ?? throw new InvalidOperationException("Expected lambda");
                    }
                    else
                    {
                        var test1 = argumentExpressions.Cast<ParameterExpression>()!;

                        var expressionFactoryConstructionMethod =
                            Expression.Lambda<Func<LambdaExpression>>(
                                Expression.Call(
                                    expressionFactoryMethod,
                                    argumentExpressions
                                )
                            ).Compile();

                        return expressionFactoryConstructionMethod.Invoke();
                    }
                });
            }); 
        }
    }
}
