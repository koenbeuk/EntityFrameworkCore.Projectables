using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator
{
    public class DeclarationSyntaxRewriter : CSharpSyntaxRewriter
    {
        readonly SemanticModel _semanticModel;

        public DeclarationSyntaxRewriter(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
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
                        .WithTriviaFrom(node);
                }
            }

            return base.VisitNullableType(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var typeInfo = _semanticModel.GetTypeInfo(node);
            if (typeInfo.Type is not null)
            {
                return SyntaxFactory.ParseTypeName(
                    typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                ).WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            var typeInfo = _semanticModel.GetTypeInfo(node);
            if (typeInfo.Type is not null)
            {
                return SyntaxFactory.ParseTypeName(
                    typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                ).WithTriviaFrom(node);
            }

            return base.VisitQualifiedName(node);
        }
    }
}
