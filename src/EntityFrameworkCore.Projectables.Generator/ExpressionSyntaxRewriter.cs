using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
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

        private SyntaxNode? VisitThisBaseExpression(CSharpSyntaxNode node)
        {
            // Swap out the use of this and base to @this and keep leading and trailing trivias
            return SyntaxFactory.IdentifierName("@this")
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());
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

        public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node)
        {
            // Swap out the use of this to @this
            return VisitThisBaseExpression(node);
        }

        public override SyntaxNode? VisitBaseExpression(BaseExpressionSyntax node)
        {
            // Swap out the use of this to @this
            return VisitThisBaseExpression(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is not null)
            {
                var operation = node switch { 
                    { Parent: { } parent } when parent.IsKind(SyntaxKind.InvocationExpression) => _semanticModel.GetOperation(node.Parent),
                    _ => _semanticModel.GetOperation(node!)
                };

                if (operation is IMemberReferenceOperation memberReferenceOperation)
                {
                    var memberAccessCanBeQualified = node switch { 
                        { Parent: { Parent: {  } parent } } when parent.IsKind(SyntaxKind.ObjectInitializerExpression) => false,
                        _ => true
                    };

                    if (memberAccessCanBeQualified)
                    {
                        // if this operation is targeting an instance member on our targetType implicitly
                        if (memberReferenceOperation.Instance is { IsImplicit: true } && SymbolEqualityComparer.Default.Equals(memberReferenceOperation.Instance.Type, _targetTypeSymbol))
                        {
                            return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName("@this"),
                                node.WithoutLeadingTrivia()
                            ).WithLeadingTrivia(node.GetLeadingTrivia());
                        }
                
                        // if this operation is targeting a static member on our targetType implicitly
                        if (memberReferenceOperation.Instance is null && SymbolEqualityComparer.Default.Equals(memberReferenceOperation.Member.ContainingType, _targetTypeSymbol))
                        {
                            return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.ParseTypeName(_targetTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                                node.WithoutLeadingTrivia()
                            ).WithLeadingTrivia(node.GetLeadingTrivia());
                        }
                    }
                }
                else if (operation is IInvocationOperation invocationOperation)
                {
                    // if this operation is targeting an instance method on our targetType implicitly
                    if (invocationOperation.Instance is { IsImplicit: true } && SymbolEqualityComparer.Default.Equals(invocationOperation.Instance.Type, _targetTypeSymbol))
                    {
                        return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("@this"),
                            node.WithoutLeadingTrivia()
                        ).WithLeadingTrivia(node.GetLeadingTrivia());
                    }

                    // if this operation is targeting a static method on our targetType implicitly
                    if (invocationOperation.Instance is null && SymbolEqualityComparer.Default.Equals(invocationOperation.TargetMethod.ContainingType, _targetTypeSymbol))
                    {
                        return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParseTypeName(_targetTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                            node.WithoutLeadingTrivia()
                        ).WithLeadingTrivia(node.GetLeadingTrivia());
                    }
                }

                // if this node refers to a named type which is not yet fully qualified, we want to fully qualify it
                if (symbol.Kind is SymbolKind.NamedType && node.Parent?.Kind() is not SyntaxKind.QualifiedName)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);

                    if (typeInfo.Type is not null)
                    {
                        return SyntaxFactory.ParseTypeName(
                            typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        ).WithLeadingTrivia(node.GetLeadingTrivia());
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
