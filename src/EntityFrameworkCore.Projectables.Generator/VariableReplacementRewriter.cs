using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace EntityFrameworkCore.Projectables.Generator;

public class VariableReplacementRewriter : CSharpSyntaxRewriter
{
    private readonly string _variableName;
    private readonly ExpressionSyntax _replacement;

    public VariableReplacementRewriter(string variableName, ExpressionSyntax replacement)
    {
        _variableName = variableName;
        _replacement = replacement;
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Identifier.ValueText == _variableName)
        {
            return _replacement;
        }

        return base.VisitIdentifierName(node);
    }
    
    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (node.Expression is IdentifierNameSyntax identifier && 
            identifier.Identifier.ValueText == _variableName)
        {
            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                _replacement,
                node.Name
            );
        }

        return base.VisitMemberAccessExpression(node);
    }
}
