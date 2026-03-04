using System.Collections.Immutable;

namespace EntityFrameworkCore.Projectables.Generator
{
    /// <summary>
    /// Incremental-pipeline-safe representation of a single projectable member.
    /// Contains only primitive types and ImmutableArray&lt;string&gt; so that value equality
    /// works correctly across incremental generation steps.
    /// </summary>
    sealed internal record ProjectableRegistryEntry(
        string DeclaringTypeFullName,
        string MemberKind,
        string MemberLookupName,
        string GeneratedClassFullName,
        bool IsGenericClass,
        bool IsGenericMethod,
        ImmutableArray<string> ParameterTypeNames
    );
}
