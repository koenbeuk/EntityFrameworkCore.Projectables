using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator;

public class MemberDeclarationSyntaxEqualityComparer : IEqualityComparer<MemberDeclarationSyntax>
{
    public bool Equals(MemberDeclarationSyntax x, MemberDeclarationSyntax y)
    {
        return GetMemberDeclarationSyntaxName(x) == GetMemberDeclarationSyntaxName(y);
    }

    public int GetHashCode(MemberDeclarationSyntax obj)
    {
        return GetMemberDeclarationSyntaxName(obj).GetHashCode();
    }

    public static string GetMemberDeclarationSyntaxName(MemberDeclarationSyntax memberDeclaration)
    {
        var sb = new StringBuilder();

        // Get the member name
        if (memberDeclaration is MethodDeclarationSyntax methodDeclaration)
        {
            sb.Append(methodDeclaration.Identifier.Text);
        }
        else if (memberDeclaration is PropertyDeclarationSyntax propertyDeclaration)
        {
            sb.Append(propertyDeclaration.Identifier.Text);
        }
        else if (memberDeclaration is FieldDeclarationSyntax fieldDeclaration)
        {
            sb.Append(string.Join(", ", fieldDeclaration.Declaration.Variables.Select(v => v.Identifier.Text)));
        }

        // Traverse up the tree to get containing type names
        var parent = memberDeclaration.Parent;
        while (parent != null)
        {
            switch (parent)
            {
                case NamespaceDeclarationSyntax namespaceDeclaration:
                    sb.Insert(0, namespaceDeclaration.Name + ".");
                    break;
                case ClassDeclarationSyntax classDeclaration:
                    sb.Insert(0, classDeclaration.Identifier.Text + ".");
                    break;
                case StructDeclarationSyntax structDeclaration:
                    sb.Insert(0, structDeclaration.Identifier.Text + ".");
                    break;
                case InterfaceDeclarationSyntax interfaceDeclaration:
                    sb.Insert(0, interfaceDeclaration.Identifier.Text + ".");
                    break;
                case EnumDeclarationSyntax enumDeclaration:
                    sb.Insert(0, enumDeclaration.Identifier.Text + ".");
                    break;
            }
            parent = parent.Parent;
        }

        return sb.ToString();
    }
}
