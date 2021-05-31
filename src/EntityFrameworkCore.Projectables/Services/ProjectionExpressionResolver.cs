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
        readonly ConcurrentDictionary<string, LambdaExpression> _lookupCache = new();

        public LambdaExpression FindGeneratedExpression(MemberInfo projectableMemberInfo)
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

                return expressionFactoryMethod.Invoke(null, null) as LambdaExpression ?? throw new InvalidOperationException("Expected lambda");
            });
        }
    }
}
