using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using EntityFrameworkCore.Projectables.Extensions;

namespace EntityFrameworkCore.Projectables.Services
{
    public sealed class ProjectionExpressionResolver : IProjectionExpressionResolver
    {
        public LambdaExpression FindGeneratedExpression(MemberInfo projectableMemberInfo)
        {
            var projectableAttribute = projectableMemberInfo.GetCustomAttribute<ProjectableAttribute>()
                ?? throw new InvalidOperationException("Expected member to have a Projectable attribute. None found");

            var expression = GetExpressionFromGeneratedType(projectableMemberInfo);

            if (expression is null && projectableAttribute.UseMemberBody is not null)
            {
                expression = GetExpressionFromMemberBody(projectableMemberInfo, projectableAttribute.UseMemberBody);
            }

            if (expression is null)
            {
                var declaringType = projectableMemberInfo.DeclaringType ?? throw new InvalidOperationException("Expected a valid type here");
                var fullName = string.Join(".", Enumerable.Empty<string>()
                    .Concat(new[] { declaringType.Namespace })
                    .Concat(declaringType.GetNestedTypePath().Select(x => x.Name))
                    .Concat(new[] { projectableMemberInfo.Name }));

                throw new InvalidOperationException($"Unable to resolve generated expression for {fullName}.");
            }

            return expression;

            static LambdaExpression? GetExpressionFromMemberBody(MemberInfo projectableMemberInfo, string memberName)
            {
                var declaringType = projectableMemberInfo.DeclaringType ?? throw new InvalidOperationException("Expected a valid type here");
                var exprProperty = declaringType.GetProperty(memberName, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var lambda = exprProperty?.GetValue(null) as LambdaExpression;

                if (lambda is not null)
                {
                    if (projectableMemberInfo is PropertyInfo property &&
                        lambda.Parameters.Count == 1 &&
                        lambda.Parameters[0].Type == declaringType && lambda.ReturnType == property.PropertyType)
                    {
                        return lambda;
                    }
                    else if (projectableMemberInfo is MethodInfo method &&
                        lambda.Parameters.Count == method.GetParameters().Length + 1 &&
                        lambda.Parameters.Last().Type == declaringType &&
                        !lambda.Parameters.Zip(method.GetParameters(), (a, b) => a.Type != b.ParameterType).Any())
                    {
                        return lambda;
                    }
                }

                return null;
            }

            static LambdaExpression? GetExpressionFromGeneratedType(MemberInfo projectableMemberInfo)
            {
                var declaringType = projectableMemberInfo.DeclaringType ?? throw new InvalidOperationException("Expected a valid type here");
                
                // Keep track of the original declaring type's generic arguments for later use
                var originalDeclaringType = declaringType;
                
                // For generic types, use the generic type definition to match the generated name
                // which is based on the open generic type
                if (declaringType.IsGenericType && !declaringType.IsGenericTypeDefinition)
                {
                    declaringType = declaringType.GetGenericTypeDefinition();
                }
                
                // Get parameter types for method overload disambiguation
                // Use the same format as Roslyn's SymbolDisplayFormat.FullyQualifiedFormat
                // which uses C# keywords for primitive types (int, string, etc.)
                string[]? parameterTypeNames = null;
                if (projectableMemberInfo is MethodInfo method)
                {
                    // For generic methods, use the generic definition to get parameter types
                    // This ensures type parameters like TEntity are used instead of concrete types
                    var methodToInspect = method.IsGenericMethod ? method.GetGenericMethodDefinition() : method;
                    
                    parameterTypeNames = methodToInspect.GetParameters()
                        .Select(p => GetFullTypeName(p.ParameterType))
                        .ToArray();
                }
                
                var generatedClassName = ProjectionExpressionClassNameGenerator.GenerateName(declaringType.Namespace, declaringType.GetNestedTypePath().Select(x => x.Name), projectableMemberInfo.Name, parameterTypeNames);
                var generatedContainingTypeName = $"{ProjectionExpressionClassNameGenerator.Namespace}.{generatedClassName}";

                var expressionFactoryType = declaringType.Assembly.GetType(generatedContainingTypeName);

                if (expressionFactoryType is null)
                {
                    // When the containing class is partial, the generated Expression class is a nested
                    // type inside the declaring type rather than in the Generated namespace.
                    expressionFactoryType = originalDeclaringType.GetNestedType(generatedClassName, BindingFlags.NonPublic | BindingFlags.Public);
                }

                if (expressionFactoryType is not null)
                {
                    if (expressionFactoryType.IsGenericTypeDefinition)
                    {
                        expressionFactoryType = expressionFactoryType.MakeGenericType(originalDeclaringType.GenericTypeArguments);
                    }

                    var expressionFactoryMethod = expressionFactoryType.GetMethod("Expression", BindingFlags.Static | BindingFlags.NonPublic);

                    var methodGenericArguments = projectableMemberInfo switch {
                        MethodInfo methodInfo => methodInfo.GetGenericArguments(),
                        _ => null
                    };

                    if (expressionFactoryMethod is not null)
                    {
                        if (methodGenericArguments is { Length: > 0 })
                        {
                            expressionFactoryMethod = expressionFactoryMethod.MakeGenericMethod(methodGenericArguments);
                        }

                        return expressionFactoryMethod.Invoke(null, null) as LambdaExpression ?? throw new InvalidOperationException("Expected lambda");
                    }
                }

                return null;
            }
            
            static string GetFullTypeName(Type type)
            {
                // Handle generic type parameters (e.g., T, TEntity)
                if (type.IsGenericParameter)
                {
                    return type.Name;
                }
                
                // Handle nullable value types (e.g., int? -> int?)
                var underlyingType = Nullable.GetUnderlyingType(type);
                if (underlyingType != null)
                {
                    return $"{GetFullTypeName(underlyingType)}?";
                }
                
                // Handle array types
                if (type.IsArray)
                {
                    var elementType = type.GetElementType();
                    if (elementType == null)
                    {
                        // Fallback for edge cases where GetElementType() might return null
                        return type.Name;
                    }
                    
                    var rank = type.GetArrayRank();
                    var elementTypeName = GetFullTypeName(elementType);
                    
                    if (rank == 1)
                    {
                        return $"{elementTypeName}[]";
                    }
                    else
                    {
                        var commas = new string(',', rank - 1);
                        return $"{elementTypeName}[{commas}]";
                    }
                }
                
                // Map primitive types to their C# keyword equivalents to match Roslyn's output
                var typeKeyword = GetCSharpKeyword(type);
                if (typeKeyword != null)
                {
                    return typeKeyword;
                }
                
                // For generic types, construct the full name matching Roslyn's format
                if (type.IsGenericType)
                {
                    var genericTypeDef = type.GetGenericTypeDefinition();
                    var genericArgs = type.GetGenericArguments();
                    var baseName = genericTypeDef.FullName ?? genericTypeDef.Name;
                    
                    // Remove the `n suffix (e.g., `1, `2)
                    var backtickIndex = baseName.IndexOf('`');
                    if (backtickIndex > 0)
                    {
                        baseName = baseName.Substring(0, backtickIndex);
                    }
                    
                    var args = string.Join(", ", genericArgs.Select(GetFullTypeName));
                    return $"{baseName}<{args}>";
                }
                
                if (type.FullName != null)
                {
                    // Replace + with . for nested types to match Roslyn's format
                    return type.FullName.Replace('+', '.');
                }
                
                return type.Name;
            }
            
            static string? GetCSharpKeyword(Type type)
            {
                if (type == typeof(bool)) return "bool";
                if (type == typeof(byte)) return "byte";
                if (type == typeof(sbyte)) return "sbyte";
                if (type == typeof(char)) return "char";
                if (type == typeof(decimal)) return "decimal";
                if (type == typeof(double)) return "double";
                if (type == typeof(float)) return "float";
                if (type == typeof(int)) return "int";
                if (type == typeof(uint)) return "uint";
                if (type == typeof(long)) return "long";
                if (type == typeof(ulong)) return "ulong";
                if (type == typeof(short)) return "short";
                if (type == typeof(ushort)) return "ushort";
                if (type == typeof(object)) return "object";
                if (type == typeof(string)) return "string";
                return null;
            }
        }
    }
}
