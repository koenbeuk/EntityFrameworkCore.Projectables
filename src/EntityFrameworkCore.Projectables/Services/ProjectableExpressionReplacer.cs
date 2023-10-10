using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using EntityFrameworkCore.Projectables.Extensions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Projectables.Services
{
    public sealed class ProjectableExpressionReplacer : ExpressionVisitor
    {
        readonly IProjectionExpressionResolver _resolver;
        readonly ExpressionArgumentReplacer _expressionArgumentReplacer = new();
        readonly Dictionary<MemberInfo, LambdaExpression?> _projectableMemberCache = new();
        private bool _disableRootRewrite;
        private IEntityType? _entityType;

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
                    : null;

                _projectableMemberCache.Add(memberInfo, reflectedExpression);
            }

            return reflectedExpression is not null;
        }

        [return: NotNullIfNotNull(nameof(node))]
        public Expression? Replace(Expression? node)
        {
            var ret = Visit(node);

            if (_disableRootRewrite)
            {
                return ret;
            }

            switch (ret)
            {
                // Probably a First() or ToList()
                case MethodCallExpression { Arguments.Count: > 0, Object: null } call when _entityType != null:
                {
                    var self = _AddProjectableSelect(call.Arguments.First(), _entityType);
                    return call.Update(null, call.Arguments.Skip(1).Prepend(self));
                }
                // Probably a foreach call
                case QueryRootExpression root:
                    return _AddProjectableSelect(root, root.EntityType);
                default:
                    return ret;
            }
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Get the overriding methodInfo based on te type of the received of this expression
            var methodInfo = node.Object?.Type.GetConcreteMethod(node.Method) ?? node.Method;

            if (methodInfo.Name == nameof(Queryable.Select))
            {
                _disableRootRewrite = true;
            }

            if (TryGetReflectedExpression(methodInfo, out var reflectedExpression))
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

                return base.Visit(
                    updatedBody
                );
            }

            return base.VisitMethodCall(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var nodeExpression = node.Expression switch {
                UnaryExpression { NodeType: ExpressionType.Convert, Type: { IsInterface: true } type, Operand: { } operand }
                    when type.IsAssignableFrom(operand.Type)
                    // This is an interface member. Operand contains the concrete (or at least more concrete) expression,
                    // from which we can try to find the concrete member.
                    => operand,
                _ => node.Expression
            };
            var nodeMember = node.Member switch {
                PropertyInfo property when nodeExpression is not null
                    => nodeExpression.Type.GetConcreteProperty(property),
                _ => node.Member
            };

            if (TryGetReflectedExpression(nodeMember, out var reflectedExpression))
            {
                if (nodeExpression is not null)
                {
                    _expressionArgumentReplacer.ParameterArgumentMapping.Add(reflectedExpression.Parameters[0], nodeExpression);
                    var updatedBody = _expressionArgumentReplacer.Visit(reflectedExpression.Body);
                    _expressionArgumentReplacer.ParameterArgumentMapping.Clear();

                    return base.Visit(
                        updatedBody
                    );
                }
                else
                {
                    return base.Visit(
                        reflectedExpression.Body
                    );
                }
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is QueryRootExpression root)
            {
                _entityType = root.EntityType;
            }
            return base.VisitExtension(node);
        }

        private Expression _AddProjectableSelect(Expression node, IEntityType entityType)
        {
            var projectableProperties = entityType.ClrType.GetProperties()
                .Where(x => x.IsDefined(typeof(ProjectableAttribute), false))
                .Where(x => x.CanWrite)
                .ToList();

            if (!projectableProperties.Any())
            {
                return node;
            }

            var properties = entityType.GetProperties()
                .Where(x => !x.IsShadowProperty())
                .Select(x => x.GetMemberInfo(false, false))
                // Remove projectable properties from the ef properties. Since properties returned here for auto
                // properties (like `public string Test {get;set;}`) are generated fields, we also need to take them into account.
                .Where(x => projectableProperties.All(y => x.Name != y.Name && x.Name != $"<{y.Name}>k__BackingField"));

            // Replace db.Entities to db.Entities.Select(x => new Entity { Property1 = x.Property1, Rewritted = rewrittedProperty })
            var select = typeof(Queryable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Where(x => x.Name == nameof(Queryable.Select))
                .First(x =>
                    x.GetParameters().Last().ParameterType // Expression<Func<T, Ret>>
                        .GetGenericArguments().First() // Func<T, Ret>
                        .GetGenericArguments().Length == 2 // Separate between Func<T, Ret> and Func<T, int, Ret>
                )
                .MakeGenericMethod(entityType.ClrType, entityType.ClrType);
            var xParam = Expression.Parameter(entityType.ClrType);
            return Expression.Call(
                null,
                select,
                node,
                Expression.Lambda(
                    Expression.MemberInit(
                        Expression.New(entityType.ClrType),
                        properties.Select(x => Expression.Bind(x, Expression.MakeMemberAccess(xParam, x)))
                            .Concat(projectableProperties
                                .Select(x => Expression.Bind(x, _GetAccessor(x, xParam)))
                            )
                    ),
                    xParam
                )
            );
        }

        private Expression _GetAccessor(PropertyInfo property, ParameterExpression para)
        {
            var lambda = _resolver.FindGeneratedExpression(property);
            _expressionArgumentReplacer.ParameterArgumentMapping.Add(lambda.Parameters[0], para);
            var updatedBody = _expressionArgumentReplacer.Visit(lambda.Body);
            _expressionArgumentReplacer.ParameterArgumentMapping.Clear();
            return base.Visit(updatedBody);
        }
    }
}
