using System.Collections.Immutable;

namespace EntityFrameworkCore.Projectables.Generator
{
    /// <summary>
    /// Incremental-pipeline-safe representation of a single projectable member.
    /// Contains only primitive types and ImmutableArray&lt;string&gt; so that value equality
    /// works correctly across incremental generation steps.
    /// </summary>
    internal sealed record ProjectableRegistryEntry(
        string DeclaringTypeFullName,
        string MemberKind,
        string MemberLookupName,
        string GeneratedClassFullName,
        bool IsGenericClass,
        int ClassTypeParamCount,
        bool IsGenericMethod,
        int MethodTypeParamCount,
        ImmutableArray<string> ParameterTypeNames
    );
}
