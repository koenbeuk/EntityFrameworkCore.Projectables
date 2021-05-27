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

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            // todo: Fully qualify these
            return node;
        }

        public override SyntaxNode? VisitParameter(ParameterSyntax node)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(node);

            if (symbol is not null)
            {
            }

            var thisKeywordIndex = node.Modifiers.IndexOf(SyntaxKind.ThisKeyword);
            if (thisKeywordIndex != -1)
            {
                node = node.WithModifiers(node.Modifiers.RemoveAt(thisKeywordIndex));
            }


            return node;
        }
    }
}
