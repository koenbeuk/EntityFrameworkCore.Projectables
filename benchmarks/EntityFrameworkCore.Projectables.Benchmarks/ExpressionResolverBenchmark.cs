using System.Linq.Expressions;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using EntityFrameworkCore.Projectables.Benchmarks.Helpers;
using EntityFrameworkCore.Projectables.Services;

namespace EntityFrameworkCore.Projectables.Benchmarks
{
    /// <summary>
    /// Micro-benchmarks <see cref="ProjectionExpressionResolver.FindGeneratedExpression"/> in
    /// isolation (no EF Core overhead) to directly compare the registry lookup path against
    /// the previous per-call reflection chain.
    /// </summary>
    [MemoryDiagnoser]
    public class ExpressionResolverBenchmark
    {
        private static readonly MemberInfo _propertyMember =
            typeof(TestEntity).GetProperty(nameof(TestEntity.IdPlus1))!;

        private static readonly MemberInfo _methodMember =
            typeof(TestEntity).GetMethod(nameof(TestEntity.IdPlus1Method))!;

        private static readonly MemberInfo _methodWithParamMember =
            typeof(TestEntity).GetMethod(nameof(TestEntity.IdPlusDelta), new[] { typeof(int) })!;

        private readonly ProjectionExpressionResolver _resolver = new();

        [Benchmark(Baseline = true)]
        public LambdaExpression? ResolveProperty()
            => _resolver.FindGeneratedExpression(_propertyMember);

        [Benchmark]
        public LambdaExpression? ResolveMethod()
            => _resolver.FindGeneratedExpression(_methodMember);

        [Benchmark]
        public LambdaExpression? ResolveMethodWithParam()
            => _resolver.FindGeneratedExpression(_methodWithParamMember);
    }
}
