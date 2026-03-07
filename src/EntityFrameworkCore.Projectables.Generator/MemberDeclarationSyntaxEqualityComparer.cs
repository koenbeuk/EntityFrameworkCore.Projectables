using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator;

public class MemberDeclarationSyntaxEqualityComparer : IEqualityComparer<MemberDeclarationSyntax>
{
    public bool Equals(MemberDeclarationSyntax x, MemberDeclarationSyntax y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        // Must be in the same file — if the syntax tree changed, treat as different
        // (Roslyn reuses SyntaxTree objects for unchanged files, so a new SyntaxTree
        // means the file was edited, even if this specific node text looks the same)
        if (!ReferenceEquals(x.SyntaxTree, y.SyntaxTree))
        {
            return false;
        }

        // Same file, same node text = truly unchanged
        return x.ToFullString() == y.ToFullString();
    }

    public int GetHashCode(MemberDeclarationSyntax obj)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + RuntimeHelpers.GetHashCode(obj.SyntaxTree);
            hash = hash * 31 + obj.ToFullString().GetHashCode();
            return hash;
        }
    }
}