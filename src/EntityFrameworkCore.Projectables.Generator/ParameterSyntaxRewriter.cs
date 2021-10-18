using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator
{
    public class ParameterSyntaxRewriter : CSharpSyntaxRewriter
    {
        readonly SemanticModel _semanticModel;

        public ParameterSyntaxRewriter(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var visitedNode = base.VisitIdentifierName(node);

            var symbol = _semanticModel.GetDeclaredSymbol(visitedNode);

            if (symbol is not null)
            {
                return SyntaxFactory.IdentifierName(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            return visitedNode;
        }

        public override SyntaxNode? VisitParameter(ParameterSyntax node)
        {
            var visitedNode = base.VisitParameter(node);

            if (visitedNode is ParameterSyntax visitedParameterSyntax)
            {
                var thisKeywordIndex = visitedParameterSyntax.Modifiers.IndexOf(SyntaxKind.ThisKeyword);
                if (thisKeywordIndex != -1)
                {
                    return visitedParameterSyntax.WithModifiers(node.Modifiers.RemoveAt(thisKeywordIndex));
                }
            }

            return visitedNode;
        }

        public override SyntaxNode? VisitNullableType(NullableTypeSyntax node)
        {
            var typeInfo = _semanticModel.GetTypeInfo(node);
            if (typeInfo.Type is not null)
            {
                if (typeInfo.Type.TypeKind is not TypeKind.Struct)
                {
                    return Visit(node.ElementType)
                        .WithLeadingTrivia(node.GetLeadingTrivia())
                        .WithTrailingTrivia(node.GetTrailingTrivia());
                }
            }

            return base.VisitNullableType(node);
        }
    }
}
