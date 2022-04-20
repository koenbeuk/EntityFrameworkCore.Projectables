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
        readonly Compilation _compilation;
        readonly SourceProductionContext _context;
        readonly Stack<ExpressionSyntax> _conditionalAccessExpressionsStack = new();

        public ExpressionSyntaxRewriter(INamedTypeSymbol targetTypeSymbol, NullConditionalRewriteSupport nullConditionalRewriteSupport, Compilation compilation, SemanticModel semanticModel, SourceProductionContext context)
        {
            _targetTypeSymbol = targetTypeSymbol;
            _nullConditionalRewriteSupport = nullConditionalRewriteSupport;
            _semanticModel = semanticModel;
            _compilation = compilation;
            _context = context;
        }

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            var targetExpression = (ExpressionSyntax)Visit(node.Expression);

            _conditionalAccessExpressionsStack.Push(targetExpression);

            if (_nullConditionalRewriteSupport == NullConditionalRewriteSupport.None)
            {
                var diagnostic = Diagnostic.Create(Diagnostics.NullConditionalRewriteUnsupported, node.GetLocation(), node);
                _context.ReportDiagnostic(diagnostic);
            }

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

            if (symbolInfo.Symbol is not null)
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
            var symbol = _semanticModel.GetSymbolInfo(node).Symbol;

            if (symbol is not null)
            {
                if (symbol is IMethodSymbol methodSymbol && methodSymbol.IsExtensionMethod)
                {
                    // Ignore extension methods
                }
                else if (symbol.Kind is SymbolKind.Property or SymbolKind.Method or SymbolKind.Field)
                {
                    // We may need to rewrite this expression such that it refers to our @this argument
                    bool rewrite = true;

                    if (node.Parent is MemberAccessExpressionSyntax parentMemberAccessNode)
                    {
                        var targetSymbolInfo = _semanticModel.GetSymbolInfo(parentMemberAccessNode.Expression);
                        if (targetSymbolInfo.Symbol is null)
                        {
                            rewrite = false;
                        }
                        else if (targetSymbolInfo.Symbol is { Kind: SymbolKind.Parameter or SymbolKind.NamedType })
                        {
                            rewrite = false;
                        }
                        else if (targetSymbolInfo.Symbol?.ContainingType is not null)
                        {
                            if (!_compilation.HasImplicitConversion(targetSymbolInfo.Symbol.ContainingType, _targetTypeSymbol) ||
                                !SymbolEqualityComparer.Default.Equals(symbol.ContainingType, _targetTypeSymbol))
                            {
                                rewrite = false;
                            }
                        }
                    }
                    else if (node.Parent.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        rewrite = false;
                    }
                    else if (node.Parent.IsKind(SyntaxKind.InvocationExpression))
                    {
                        rewrite = true;
                    }

                    if (rewrite)
                    {
                        var expressionSyntax = symbol.IsStatic
                            ? SyntaxFactory.ParseTypeName(_targetTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                            : SyntaxFactory.IdentifierName("@this");

                        return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            expressionSyntax,
                            node
                        );
                    }
                    
                }
                else if (symbol.Kind is SymbolKind.NamedType && node.Parent?.Kind() is not SyntaxKind.QualifiedName)
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
