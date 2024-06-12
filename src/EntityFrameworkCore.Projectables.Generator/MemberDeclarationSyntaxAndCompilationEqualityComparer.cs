using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator;

public class MemberDeclarationSyntaxAndCompilationEqualityComparer : IEqualityComparer<(MemberDeclarationSyntax, Compilation)>
{
    public bool Equals((MemberDeclarationSyntax, Compilation) x, (MemberDeclarationSyntax, Compilation) y)
    {
        return GetMemberDeclarationSyntaxAndCompilationName(x.Item1, x.Item2) == GetMemberDeclarationSyntaxAndCompilationName(y.Item1, y.Item2);
    }

    public int GetHashCode((MemberDeclarationSyntax, Compilation) obj)
    {
        return GetMemberDeclarationSyntaxAndCompilationName(obj.Item1, obj.Item2).GetHashCode();
    }

    public static string GetMemberDeclarationSyntaxAndCompilationName(MemberDeclarationSyntax memberDeclarationSyntax, Compilation compilation)
    {
        return $"{compilation.AssemblyName}:{MemberDeclarationSyntaxEqualityComparer.GetMemberDeclarationSyntaxName(memberDeclarationSyntax)}";
    }
}
