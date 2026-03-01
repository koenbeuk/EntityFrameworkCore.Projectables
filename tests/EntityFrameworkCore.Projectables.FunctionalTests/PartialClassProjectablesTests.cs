using System;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using VerifyXunit;
using Xunit;

// Entity definitions at namespace scope to allow partial class generation.
// Partial classes are required for [Projectable] members that access
// private or protected members, so the generated expression class can
// be nested inside the declaring type.
namespace EntityFrameworkCore.Projectables.FunctionalTests.PartialEntities
{
    /// <summary>
    /// Demonstrates that a private [Projectable] helper method can be called from
    /// a public [Projectable] method when the class is partial.
    /// </summary>
    public partial record PrivateHelperEntity
    {
        public int Id { get; set; }

        public int Value { get; set; }

        // A private [Projectable] helper that operates on mapped public properties.
        [Projectable]
        private int PrivateDouble(int x) => x * 2;

        // A public [Projectable] method that calls the private helper.
        [Projectable]
        public int ComputedDouble => PrivateDouble(Value);
    }

    /// <summary>
    /// Demonstrates that a protected [Projectable] helper method can be called from
    /// a public [Projectable] method when the class is partial.
    /// </summary>
    public partial record ProtectedHelperEntity
    {
        public int Id { get; set; }

        public int Value { get; set; }

        // A protected [Projectable] helper that operates on mapped public properties.
        [Projectable]
        protected int ProtectedTriple(int x) => x * 3;

        // A public [Projectable] method that calls the protected helper.
        [Projectable]
        public int ComputedTriple => ProtectedTriple(Value);
    }

    /// <summary>
    /// Demonstrates that an internal [Projectable] method is accessible from the generated
    /// standalone expression class without needing a partial class (same-assembly access).
    /// </summary>
    public record InternalHelperEntity
    {
        public int Id { get; set; }

        public int Value { get; set; }

        // An internal [Projectable] helper - accessible from the generated class (same assembly)
        // without requiring the class to be partial.
        [Projectable]
        internal int InternalDouble(int x) => x * 2;

        // A public [Projectable] method that calls the internal helper.
        [Projectable]
        public int ComputedDouble => InternalDouble(Value);
    }
}

namespace EntityFrameworkCore.Projectables.FunctionalTests
{
    using EntityFrameworkCore.Projectables.FunctionalTests.PartialEntities;

    [UsesVerify]
    public class PartialClassProjectablesTests
    {
        /// <summary>
        /// Tests that a private [Projectable] method can be used in a projection when the
        /// containing class is partial (the generated class is nested for private access).
        /// </summary>
        [Fact]
        public Task PrivateHelper_CanBeUsedInProjection()
        {
            using var dbContext = new SampleDbContext<PrivateHelperEntity>();

            var query = dbContext.Set<PrivateHelperEntity>()
                .Select(x => x.ComputedDouble);

            return Verifier.Verify(query.ToQueryString());
        }

        /// <summary>
        /// Tests that a protected [Projectable] method can be used in a projection when
        /// the containing class is partial.
        /// </summary>
        [Fact]
        public Task ProtectedHelper_CanBeUsedInProjection()
        {
            using var dbContext = new SampleDbContext<ProtectedHelperEntity>();

            var query = dbContext.Set<ProtectedHelperEntity>()
                .Select(x => x.ComputedTriple);

            return Verifier.Verify(query.ToQueryString());
        }

        /// <summary>
        /// Tests that an internal [Projectable] method is accessible from the generated
        /// expression class without requiring partial class support (same-assembly access).
        /// </summary>
        [Fact]
        public Task InternalMember_AccessibleWithoutPartialClass()
        {
            using var dbContext = new SampleDbContext<InternalHelperEntity>();

            var query = dbContext.Set<InternalHelperEntity>()
                .Select(x => x.ComputedDouble);

            return Verifier.Verify(query.ToQueryString());
        }
    }
}
