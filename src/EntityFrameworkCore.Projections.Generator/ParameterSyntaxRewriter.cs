using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projections.Generator
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
            var symbol = _semanticModel.GetDeclaredSymbol(node);

            if (symbol is not null)
            {
                node = SyntaxFactory.IdentifierName(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitParameter(ParameterSyntax node)
        {
            var thisKeywordIndex = node.Modifiers.IndexOf(SyntaxKind.ThisKeyword);
            if (thisKeywordIndex != -1)
            {
                node = node.WithModifiers(node.Modifiers.RemoveAt(thisKeywordIndex));
            }

            return base.VisitParameter(node);
        }
    }
}
