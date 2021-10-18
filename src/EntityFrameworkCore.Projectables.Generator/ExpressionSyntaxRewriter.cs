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
        readonly NullConditionalRewriteSupport _nullConditionalRewriteSupport;
        readonly Stack<ExpressionSyntax> _conditionalAccessExpressionsStack = new();

        public ExpressionSyntaxRewriter(INamedTypeSymbol targetTypeSymbol, SemanticModel semanticModel, NullConditionalRewriteSupport nullConditionalRewriteSupport)
        {
            _targetTypeSymbol = targetTypeSymbol;
            _semanticModel = semanticModel;
            _nullConditionalRewriteSupport = nullConditionalRewriteSupport;
        }

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            var targetExpression = (ExpressionSyntax)Visit(node.Expression);

            _conditionalAccessExpressionsStack.Push(targetExpression);

            return _nullConditionalRewriteSupport switch {
                NullConditionalRewriteSupport.Ignore => Visit(node.WhenNotNull),
                NullConditionalRewriteSupport.Rewrite =>
                    SyntaxFactory.ConditionalExpression(
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.NotEqualsExpression,
                            targetExpression
                                .WithTrailingTrivia(SyntaxFactory.Whitespace(" ")),
                            SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                                .WithLeadingTrivia(SyntaxFactory.Whitespace(" "))
                        )
                            .WithTrailingTrivia(SyntaxFactory.Whitespace(" ")),
                        SyntaxFactory.ParenthesizedExpression(
                            (ExpressionSyntax)Visit(node.WhenNotNull)
                        )
                            .WithLeadingTrivia(SyntaxFactory.Whitespace(" "))
                            .WithTrailingTrivia(SyntaxFactory.Whitespace(" ")),
                        SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                            .WithLeadingTrivia(SyntaxFactory.Whitespace(" "))
                    ),
                _ => base.VisitConditionalAccessExpression(node)
            };
        }

        public override SyntaxNode? VisitMemberBindingExpression(MemberBindingExpressionSyntax node)
        {
            if (_conditionalAccessExpressionsStack.Count == 0)
            {
                throw new InvalidOperationException("Expected at least one conditional expression on the stack");
            }

            var targetExpression = _conditionalAccessExpressionsStack.Pop();

            return _nullConditionalRewriteSupport switch {
                NullConditionalRewriteSupport.Ignore => SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, targetExpression, node.Name),
                NullConditionalRewriteSupport.Rewrite => SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, targetExpression, node.Name),
                _ => node
            };
        }

        public override SyntaxNode? VisitElementBindingExpression(ElementBindingExpressionSyntax node)
        {
            if (_conditionalAccessExpressionsStack.Count == 0)
            {
                throw new InvalidOperationException("Expected at least one conditional expression on the stack");
            }

            var targetExpression = _conditionalAccessExpressionsStack.Pop();

            return _nullConditionalRewriteSupport switch {
                NullConditionalRewriteSupport.Ignore => SyntaxFactory.ElementAccessExpression(targetExpression, node.ArgumentList),
                NullConditionalRewriteSupport.Rewrite => SyntaxFactory.ElementAccessExpression(targetExpression, node.ArgumentList),
                _ => Visit(node)
            };
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
