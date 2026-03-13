using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using EntityFrameworkCore.Projectables.Extensions;

namespace EntityFrameworkCore.Projectables.Services
{
    public sealed class ProjectionExpressionResolver : IProjectionExpressionResolver
    {
        // We never store null in the dictionary; assemblies without a registry use a sentinel delegate.
        private static readonly Func<MemberInfo, LambdaExpression> _nullRegistry = static _ => null!;
        private static readonly ConcurrentDictionary<Assembly, Func<MemberInfo, LambdaExpression>> _assemblyRegistries = new();

        /// <summary>
        /// Caches the fully-resolved <see cref="LambdaExpression"/> per <see cref="MemberInfo"/> so that
        /// EF Core never repeats reflection work for the same member across queries.
        /// </summary>
        private static readonly ConcurrentDictionary<MemberInfo, LambdaExpression> _expressionCache = new();

        /// <summary>
        /// Caches <see cref="Type"/> → C#-formatted name strings, since the same parameter types
        /// appear repeatedly across different projectable members.
        /// </summary>
        private static readonly ConditionalWeakTable<Type, string> _typeNameCache = new();

        /// <summary>
        /// O(1) hash-table lookup replacing the original 16 sequential <c>if</c> checks.
        /// Rearranging the entries has no effect on lookup cost (hash-based), but the most common
        /// EF Core types (<c>int</c>, <c>string</c>, <c>bool</c>) are listed first for readability.
        /// </summary>
        private static readonly Dictionary<Type, string> _csharpKeywords = new(16)
        {
            [typeof(int)]     = "int",
            [typeof(string)]  = "string",
            [typeof(bool)]    = "bool",
            [typeof(long)]    = "long",
            [typeof(double)]  = "double",
            [typeof(decimal)] = "decimal",
            [typeof(float)]   = "float",
            [typeof(byte)]    = "byte",
            [typeof(sbyte)]   = "sbyte",
            [typeof(char)]    = "char",
            [typeof(uint)]    = "uint",
            [typeof(ulong)]   = "ulong",
            [typeof(short)]   = "short",
            [typeof(ushort)]  = "ushort",
            [typeof(object)]  = "object",
        };

        /// <summary>
        /// Looks up the generated <c>ProjectionRegistry</c> class in an assembly (once, then caches it).
        /// Returns a delegate that calls <c>TryGet(MemberInfo)</c> on the registry, or null if the registry
        /// is not present in that assembly (e.g. if the source generator was not run against it).
        /// </summary>
        private static Func<MemberInfo, LambdaExpression>? GetAssemblyRegistry(Assembly assembly)
        {
            var registry = _assemblyRegistries.GetOrAdd(assembly, static asm =>
            {
                var registryType = asm.GetType("EntityFrameworkCore.Projectables.Generated.ProjectionRegistry");
                var tryGetMethod = registryType?.GetMethod("TryGet", BindingFlags.Static | BindingFlags.Public);

                if (tryGetMethod is null)
                {
                    // Use sentinel to indicate "no registry for this assembly"
                    return _nullRegistry;
                }

                return (Func<MemberInfo, LambdaExpression>)Delegate.CreateDelegate(typeof(Func<MemberInfo, LambdaExpression>), tryGetMethod);
            });

            // Translate sentinel back to null for callers, preserving existing behavior.
            return ReferenceEquals(registry, _nullRegistry) ? null : registry;
        }

        public LambdaExpression FindGeneratedExpression(MemberInfo projectableMemberInfo)
            => _expressionCache.GetOrAdd(projectableMemberInfo, static mi => ResolveExpressionCore(mi));

        private static LambdaExpression ResolveExpressionCore(MemberInfo projectableMemberInfo)
        {
            var projectableAttribute = projectableMemberInfo.GetCustomAttribute<ProjectableAttribute>()
                ?? throw new InvalidOperationException("Expected member to have a Projectable attribute. None found");

            var expression = GetExpressionFromGeneratedType(projectableMemberInfo);

            if (expression is null && projectableAttribute.UseMemberBody is not null)
            {
                expression = GetExpressionFromMemberBody(projectableMemberInfo, projectableAttribute.UseMemberBody);
            }

            if (expression is not null)
            {
                return expression;
            }

            var declaringType = projectableMemberInfo.DeclaringType ?? throw new InvalidOperationException("Expected a valid type here");
            var fullName = string.Join(".", Enumerable.Empty<string>()
                .Concat(new[] { declaringType.Namespace })
                .Concat(declaringType.GetNestedTypePath().Select(x => x.Name))
                .Concat(new[] { projectableMemberInfo.Name }));

            throw new InvalidOperationException($"Unable to resolve generated expression for {fullName}.");
        }

        private static LambdaExpression? GetExpressionFromGeneratedType(MemberInfo projectableMemberInfo)
        {
            var declaringType = projectableMemberInfo.DeclaringType ?? throw new InvalidOperationException("Expected a valid type here");

            // Fast path: check the per-assembly static registry (generated by source generator).
            // The first call per assembly does a reflection lookup to find the registry class and
            // caches it as a delegate; subsequent calls use the cached delegate for an O(1) dictionary lookup.
            var registry = GetAssemblyRegistry(declaringType.Assembly);
            var registeredExpr = registry?.Invoke(projectableMemberInfo);
                
            return registeredExpr ??
                   // Slow path: reflection fallback for open-generic class members and generic methods
                   // that are not yet in the registry.
                   FindGeneratedExpressionViaReflection(projectableMemberInfo);
        }

        private static LambdaExpression? GetExpressionFromMemberBody(MemberInfo projectableMemberInfo, string memberName)
        {
            var declaringType = projectableMemberInfo.DeclaringType ?? throw new InvalidOperationException("Expected a valid type here");
            var exprProperty = declaringType.GetProperty(memberName, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (exprProperty?.GetValue(null) is not LambdaExpression lambda)
            {
                return null;
            }

            switch (projectableMemberInfo)
            {
                case PropertyInfo property when
                    lambda.Parameters.Count == 1 &&
                    lambda.Parameters[0].Type == declaringType && lambda.ReturnType == property.PropertyType:
                    return lambda;
                    
                case MethodInfo method:
                {
                    var methodParams = method.GetParameters();

                    // The lambda's return type must match the method's return type.
                    if (lambda.ReturnType != method.ReturnType)
                    {
                        return null;
                    }

                    if (method.IsStatic)
                    {
                        // Static methods (including extension methods): all parameters are explicit.
                        if (lambda.Parameters.Count == methodParams.Length &&
                            ParameterTypesMatch(lambda.Parameters, 0, methodParams))
                        {
                            return lambda;
                        }
                    }
                    else
                    {
                        // Instance methods: lambda's first parameter is the implicit 'this'.
                        if (lambda.Parameters.Count == methodParams.Length + 1 &&
                            lambda.Parameters[0].Type == declaringType &&
                            ParameterTypesMatch(lambda.Parameters, 1, methodParams))
                        {
                            return lambda;
                        }
                    }

                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Compares lambda parameter types against method parameter types starting at <paramref name="offset"/>,
        /// avoiding LINQ allocations (no Zip/Any enumerators or delegates).
        /// </summary>
        private static bool ParameterTypesMatch(
            ReadOnlyCollection<ParameterExpression> lambdaParams,
            int offset,
            ParameterInfo[] methodParams)
        {
            for (var i = 0; i < methodParams.Length; i++)
            {
                if (lambdaParams[offset + i].Type != methodParams[i].ParameterType)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Resolves the <see cref="LambdaExpression"/> for a <c>[Projectable]</c> member using the
        /// reflection-based slow path only, bypassing the static registry.
        /// Useful for benchmarking and for members not yet in the registry (e.g. open-generic types).
        /// </summary>
        public static LambdaExpression? FindGeneratedExpressionViaReflection(MemberInfo projectableMemberInfo)
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
            string memberLookupName = projectableMemberInfo.Name;
            if (projectableMemberInfo is MethodInfo method)
            {
                // For generic methods, use the generic definition to get parameter types
                // This ensures type parameters like TEntity are used instead of concrete types
                var methodToInspect = method.IsGenericMethod ? method.GetGenericMethodDefinition() : method;

                parameterTypeNames = methodToInspect.GetParameters()
                    .Select(p => GetFullTypeName(p.ParameterType))
                    .ToArray();
            }
            else if (projectableMemberInfo is ConstructorInfo ctor)
            {
                // Constructors are stored under the synthetic name "_ctor"
                memberLookupName = "_ctor";
                parameterTypeNames = ctor.GetParameters()
                    .Select(p => GetFullTypeName(p.ParameterType))
                    .ToArray();
            }

            var generatedContainingTypeName = ProjectionExpressionClassNameGenerator.GenerateFullName(declaringType.Namespace, declaringType.GetNestedTypePath().Select(x => x.Name), memberLookupName, parameterTypeNames);

            var expressionFactoryType = declaringType.Assembly.GetType(generatedContainingTypeName);

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

        /// <summary>
        /// Returns the C#-formatted full name of <paramref name="type"/>.
        /// Results are memoised in <see cref="_typeNameCache"/>; the same <see cref="Type"/> object
        /// is encountered repeatedly across projectable members (e.g. <c>int</c>, <c>string</c>).
        /// </summary>
        private static string GetFullTypeName(Type type)
            => _typeNameCache.GetValue(type, static t => ComputeFullTypeName(t));

        private static string ComputeFullTypeName(Type type)
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

        /// <summary>
        /// O(1) dictionary lookup — replaces the original 16 sequential <c>if</c> checks.
        /// Note: reordering the entries in <see cref="_csharpKeywords"/> has <em>no effect</em> on
        /// performance because <see cref="Dictionary{TKey,TValue}"/> uses hashing, not linear scan.
        /// (Reordering only mattered with the old <c>if</c>-chain, where placing <c>int</c> / <c>string</c>
        /// / <c>bool</c> first would have reduced average comparisons from ~8 to ~1.)
        /// </summary>
        private static string? GetCSharpKeyword(Type type) => _csharpKeywords.GetValueOrDefault(type);
    }
}
