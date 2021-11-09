using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator
{
    public static class ProjectableInterpreter
    {
        static IEnumerable<string> GetNestedInClassPath(ITypeSymbol namedTypeSymbol)
        {
            if (namedTypeSymbol.ContainingType is not null)
            {
                foreach (var nestedInClassName in GetNestedInClassPath(namedTypeSymbol.ContainingType))
                {
                    yield return nestedInClassName;
                }
            }

            yield return namedTypeSymbol.Name;
        }

        public static ProjectableDescriptor? GetDescriptor(MemberDeclarationSyntax memberDeclarationSyntax, GeneratorExecutionContext context)
        {
            var semanticModel = context.Compilation.GetSemanticModel(memberDeclarationSyntax.SyntaxTree);
            var memberSymbol = semanticModel.GetDeclaredSymbol(memberDeclarationSyntax);

            if (memberSymbol is null)
            {
                return null;
            }

            var projectableAttributeTypeSymbol = context.Compilation.GetTypeByMetadataName("EntityFrameworkCore.Projectables.ProjectableAttribute");

            var projectableAttributeClass = memberSymbol.GetAttributes()
                .Where(x => x.AttributeClass?.Name == "ProjectableAttribute")
                .FirstOrDefault();

            if (projectableAttributeClass is null || !SymbolEqualityComparer.Default.Equals(projectableAttributeClass.AttributeClass, projectableAttributeTypeSymbol))
            {
                return null;
            }

            var nullConditionalRewriteSupport = projectableAttributeClass.NamedArguments
                .Where(x => x.Key == "NullConditionalRewriteSupport")
                .Where(x => x.Value.Kind == TypedConstantKind.Enum)
                .Select(x => x.Value.Value)
                .Where(x => Enum.IsDefined(typeof(NullConditionalRewriteSupport), x))
                .Cast<NullConditionalRewriteSupport>()
                .FirstOrDefault();

            var expressionSyntaxRewriter = new ExpressionSyntaxRewriter(memberSymbol.ContainingType, semanticModel, nullConditionalRewriteSupport, context);
            var declarationSyntaxRewriter = new DeclarationSyntaxRewriter(semanticModel);

            var descriptor = new ProjectableDescriptor {
                ClassName = memberSymbol.ContainingType.Name,
                ClassNamespace = memberSymbol.ContainingType.ContainingNamespace.IsGlobalNamespace ? null : memberSymbol.ContainingType.ContainingNamespace.ToDisplayString(),
                MemberName = memberSymbol.Name,
                NestedInClassNames = GetNestedInClassPath(memberSymbol.ContainingType),
                ParametersList = SyntaxFactory.ParameterList(),
                TypeParameterList = SyntaxFactory.TypeParameterList()
            };

            if (!memberDeclarationSyntax.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                descriptor.ParametersList = descriptor.ParametersList.AddParameters(
                    SyntaxFactory.Parameter(
                            SyntaxFactory.Identifier("@this")
                        ).WithType(
                            SyntaxFactory.ParseTypeName(
                                memberSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                            )
                            .WithTrailingTrivia(
                                SyntaxFactory.SyntaxTrivia(SyntaxKind.WhitespaceTrivia, " ")
                            )
                        )
                );
            }

            var methodSymbol = memberSymbol as IMethodSymbol;

            if (methodSymbol is { IsExtensionMethod: true })
            {
                var targetTypeSymbol = methodSymbol.Parameters.First().Type;
                descriptor.TargetClassNamespace = targetTypeSymbol.ContainingNamespace.IsGlobalNamespace ? null : targetTypeSymbol.ContainingNamespace.ToDisplayString();
                descriptor.TargetNestedInClassNames = GetNestedInClassPath(targetTypeSymbol);
            }
            else
            {
                descriptor.TargetClassNamespace = descriptor.ClassNamespace;
                descriptor.TargetNestedInClassNames = descriptor.NestedInClassNames;
            }

            if (memberDeclarationSyntax is MethodDeclarationSyntax methodDeclarationSyntax)
            {
                if (methodDeclarationSyntax.ExpressionBody is null)
                {
                    var diagnostic = Diagnostic.Create(Diagnostics.RequiresExpressionBodyDefinition, methodDeclarationSyntax.GetLocation(), memberSymbol.Name);
                    context.ReportDiagnostic(diagnostic);
                    return null;
                }

                var returnType = declarationSyntaxRewriter.Visit(methodDeclarationSyntax.ReturnType);

                descriptor.ReturnTypeName = returnType.ToString();
                descriptor.Body = expressionSyntaxRewriter.Visit(methodDeclarationSyntax.ExpressionBody.Expression);
                foreach (var additionalParameter in ((ParameterListSyntax)declarationSyntaxRewriter.Visit(methodDeclarationSyntax.ParameterList)).Parameters)
                {
                    descriptor.ParametersList = descriptor.ParametersList.AddParameters(additionalParameter);
                }

                if (methodDeclarationSyntax.TypeParameterList is not null)
                {
                    foreach (var additionalTypeParameter in ((TypeParameterListSyntax)declarationSyntaxRewriter.Visit(methodDeclarationSyntax.TypeParameterList)).Parameters)
                    {
                        descriptor.TypeParameterList = descriptor.TypeParameterList.AddParameters(additionalTypeParameter);
                    }
                }

                if (methodDeclarationSyntax.ConstraintClauses.Any())
                {
                    descriptor.ConstraintClauses = methodDeclarationSyntax.ConstraintClauses
                        .Select(x => (TypeParameterConstraintClauseSyntax)declarationSyntaxRewriter.Visit(x));
                }
            }
            else if (memberDeclarationSyntax is PropertyDeclarationSyntax propertyDeclarationSyntax)
            {
                if (propertyDeclarationSyntax.ExpressionBody is null)
                {
                    var diagnostic = Diagnostic.Create(Diagnostics.RequiresExpressionBodyDefinition, propertyDeclarationSyntax.GetLocation(), memberSymbol.Name);
                    context.ReportDiagnostic(diagnostic);
                    return null;
                }

                var returnType = declarationSyntaxRewriter.Visit(propertyDeclarationSyntax.Type);

                descriptor.ReturnTypeName = returnType.ToString();
                descriptor.Body = expressionSyntaxRewriter.Visit(propertyDeclarationSyntax.ExpressionBody.Expression);
            }
            else
            {
                return null;
            }

            descriptor.UsingDirectives =
                memberDeclarationSyntax.SyntaxTree
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<UsingDirectiveSyntax>()
                    .Select(x => x.ToString());

            return descriptor;
        }
    }
}
