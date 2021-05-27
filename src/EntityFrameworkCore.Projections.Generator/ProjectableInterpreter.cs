using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projections.Generator
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


            var projectableAttributeTypeSymbol = context.Compilation.GetTypeByMetadataName("EntityFrameworkCore.Projections.ProjectableAttribute");

            var projectableAttributeClass = memberSymbol.GetAttributes()
                .Where(x => x.AttributeClass.Name == "ProjectableAttribute")
                .FirstOrDefault();

            if (projectableAttributeClass is null || !SymbolEqualityComparer.Default.Equals(projectableAttributeClass.AttributeClass, projectableAttributeTypeSymbol))
            {
                return null;
            }

            var expressionSyntaxRewriter = new ExpressionSyntaxRewriter(memberSymbol.ContainingType, semanticModel);
            var parameterSyntaxRewriter = new ParameterSyntaxRewriter(semanticModel);

            var descriptor = new ProjectableDescriptor {
                ClassName = memberSymbol.ContainingType.Name,
                ClassNamespace = memberSymbol.ContainingType.ContainingNamespace.IsGlobalNamespace ? null : memberSymbol.ContainingType.ContainingNamespace.ToDisplayString(),
                MemberName = memberSymbol.Name,
                NestedInClassNames = GetNestedInClassPath(memberSymbol.ContainingType)
            };

            if (memberSymbol is IMethodSymbol methodSymbol && methodSymbol.IsExtensionMethod)
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
                descriptor.ReturnTypeName = methodDeclarationSyntax.ReturnType.ToString();
                descriptor.Body = expressionSyntaxRewriter.Visit(methodDeclarationSyntax.ExpressionBody.Expression);
                descriptor.ParametersListString = parameterSyntaxRewriter.Visit(methodDeclarationSyntax.ParameterList).ToString();
            }
            else if (memberDeclarationSyntax is PropertyDeclarationSyntax propertyDeclarationSyntax)
            {
                descriptor.ReturnTypeName = propertyDeclarationSyntax.Type.ToString();
                descriptor.Body = expressionSyntaxRewriter.Visit(propertyDeclarationSyntax.ExpressionBody.Expression);
                descriptor.ParametersListString = "()";
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
