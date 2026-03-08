using System.Collections.Immutable;
using System.Linq;

namespace EntityFrameworkCore.Projectables.Generator
{
    /// <summary>
    /// Incremental-pipeline-safe representation of a single projectable member.
    /// Contains only primitive types and an equatable wrapper around <see cref="ImmutableArray{T}"/>
    /// so that structural value equality works correctly across incremental generation steps.
    /// </summary>
    sealed internal record ProjectableRegistryEntry(
        string DeclaringTypeFullName,
        string MemberKind,
        string MemberLookupName,
        string GeneratedClassFullName,
        bool IsGenericClass,
        bool IsGenericMethod,
        EquatableImmutableArray ParameterTypeNames
    );

    /// <summary>
    /// A structural-equality wrapper around <see cref="ImmutableArray{T}"/> of strings.
    /// <see cref="ImmutableArray{T}"/> uses reference equality by default, which breaks
    /// Roslyn's incremental-source-generator caching when the same logical array is
    /// produced by two different steps. This wrapper provides element-wise equality so
    /// that incremental steps are correctly cached and skipped.
    /// </summary>
    internal readonly struct EquatableImmutableArray : System.IEquatable<EquatableImmutableArray>
    {
        public static readonly EquatableImmutableArray Empty = new(ImmutableArray<string>.Empty);

        public readonly ImmutableArray<string> Array;

        public EquatableImmutableArray(ImmutableArray<string> array)
        {
            Array = array;
        }

        public bool IsDefaultOrEmpty => Array.IsDefaultOrEmpty;

        public bool Equals(EquatableImmutableArray other) =>
            Array.SequenceEqual(other.Array);

        public override bool Equals(object? obj) =>
            obj is EquatableImmutableArray other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                foreach (var s in Array)
                    hash = hash * 31 + (s?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public static implicit operator ImmutableArray<string>(EquatableImmutableArray e) => e.Array;
        public static implicit operator EquatableImmutableArray(ImmutableArray<string> a) => new(a);
    }
}
