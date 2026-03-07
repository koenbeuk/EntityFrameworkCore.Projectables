using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator;

/// <summary>
/// Compares two <see cref="SyntaxTree"/> instances by reference identity.
/// Roslyn reuses the same SyntaxTree object for a file that has not been modified,
/// so reference equality is a cheap and correct way to detect source-file changes.
/// </summary>
file sealed class ReferenceEqualityTreeComparer : IEqualityComparer<SyntaxTree>
{
    public static readonly ReferenceEqualityTreeComparer Instance = new();
    private ReferenceEqualityTreeComparer() { }

    public bool Equals(SyntaxTree? x, SyntaxTree? y) => ReferenceEquals(x, y);
    public int GetHashCode(SyntaxTree obj) => RuntimeHelpers.GetHashCode(obj);
}

public class MemberDeclarationSyntaxAndCompilationEqualityComparer
    : IEqualityComparer<((MemberDeclarationSyntax Member, ProjectableAttributeData Attribute), Compilation)>
{
    private readonly static MemberDeclarationSyntaxEqualityComparer _memberComparer = new();

    public bool Equals(
        ((MemberDeclarationSyntax Member, ProjectableAttributeData Attribute), Compilation) x,
        ((MemberDeclarationSyntax Member, ProjectableAttributeData Attribute), Compilation) y)
    {
        var (xLeft, xCompilation) = x;
        var (yLeft, yCompilation) = y;

        // 1. Fast reference equality short-circuit
        if (ReferenceEquals(xLeft.Member, yLeft.Member) &&
            ReferenceEquals(xCompilation, yCompilation))
        {
            return true;
        }

        // 2. The syntax tree of the member's own file must be the same object
        //    (Roslyn reuses SyntaxTree instances for unchanged files, even when
        //    the Compilation object itself is new due to edits elsewhere)
        //    Single pointer comparison — very cheap.
        if (!ReferenceEquals(xLeft.Member.SyntaxTree, yLeft.Member.SyntaxTree))
        {
            return false;
        }

        // 3. Attribute arguments (primitive record struct) — cheap value comparison
        if (xLeft.Attribute != yLeft.Attribute)
        {
            return false;
        }

        // 4. Member text — string allocation, only reached when the SyntaxTree is shared
        if (!_memberComparer.Equals(xLeft.Member, yLeft.Member))
        {
            return false;
        }

        // 5. Check that all source syntax trees in the compilation are the same references.
        //    Roslyn reuses SyntaxTree instances for unchanged files, so reference comparison
        //    is cheap and sufficient to detect edits in other files within the same project.
        //    This catches cases where a referenced type in another source file changes, which
        //    would affect the semantic model used during code generation.
        //    Use SequenceEqual with a simple reference-equality predicate, compatible with
        //    netstandard2.0 where SyntaxTrees is IEnumerable<SyntaxTree>.
        if (!xCompilation.SyntaxTrees.SequenceEqual(yCompilation.SyntaxTrees, ReferenceEqualityTreeComparer.Instance))
        {
            return false;
        }

        // 6. Assembly-level references — most expensive (ImmutableArray enumeration)
        return xCompilation.ExternalReferences.SequenceEqual(yCompilation.ExternalReferences);
    }

    public int GetHashCode(((MemberDeclarationSyntax Member, ProjectableAttributeData Attribute), Compilation) obj)
    {
        var (left, _) = obj;
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + _memberComparer.GetHashCode(left.Member);
            hash = hash * 31 + RuntimeHelpers.GetHashCode(left.Member.SyntaxTree);
            hash = hash * 31 + left.Attribute.GetHashCode();
            return hash;
        }
    }
}
