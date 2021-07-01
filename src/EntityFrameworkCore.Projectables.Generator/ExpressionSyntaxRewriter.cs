using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables.Generator
{

    public class ExpressionSyntaxRewriter : CSharpSyntaxRewriter
    {
        readonly INamedTypeSymbol _targetTypeSymbol;
        readonly SemanticModel _semanticModel;

        public ExpressionSyntaxRewriter(INamedTypeSymbol targetTypeSymbol, SemanticModel semanticModel)
        {
            _targetTypeSymbol = targetTypeSymbol;
            _semanticModel = semanticModel;
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);

            if (symbolInfo.Symbol is not null && SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol.ContainingType, _targetTypeSymbol))
            {
                var scopedNode = node.ChildNodes().FirstOrDefault();
                if (scopedNode is ThisExpressionSyntax)
                {
                    var nextNode = node.ChildNodes().Skip(1).FirstOrDefault() as SimpleNameSyntax;

                    if (nextNode is not null)
                    {
                        return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("@this"),
                                nextNode
                            );
                    }
                }
            }

            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);

            if (symbolInfo.Symbol is not null)
            {
                if (symbolInfo.Symbol is IMethodSymbol methodSymbol && methodSymbol.IsExtensionMethod)
                {
                    if (SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol.ContainingType, _targetTypeSymbol))
                    {
                        //return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        //    methodSymbol.ReducedFrom.Sym
                        //);
                        //throw new Exception("foo");
                    }
                }
                else if (symbolInfo.Symbol.Kind is SymbolKind.Property or SymbolKind.Method or SymbolKind.Field && SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol.ContainingType, _targetTypeSymbol))
                {
                    return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("@this"),
                        node
                    );
                }
                else if (symbolInfo.Symbol.Kind is SymbolKind.NamedType && node.Parent.Kind() is not SyntaxKind.QualifiedName)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);

                    if (typeInfo.Type is not null)
                    {
                        return SyntaxFactory.ParseTypeName(
                            typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        );
                    }
                }
            }
         
            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);

            if (symbolInfo.Symbol is not null)
            {
                if (symbolInfo.Symbol.Kind is SymbolKind.NamedType)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);

                    if (typeInfo.Type is not null)
                    {
                        return SyntaxFactory.ParseTypeName(
                            typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        );
                    }
                }
            }

            return base.VisitQualifiedName(node);
        }
    }
}
