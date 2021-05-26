using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projections.Generator
{
    public static class ProjectableInterpreter
    {
        static IEnumerable<string> GetNestedInClassPath(INamedTypeSymbol namedTypeSymbol)
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

            var descriptor = new ProjectableDescriptor
            {
                ClassName = memberSymbol.ContainingType.Name,
                ClassNamespace = memberSymbol.ContainingType.ContainingNamespace.IsGlobalNamespace ? null : memberSymbol.ContainingType.ContainingNamespace.ToDisplayString(),
                MemberName = memberSymbol.Name,
                NestedInClassNames = GetNestedInClassPath(memberSymbol.ContainingType)
            };

            if (memberDeclarationSyntax is MethodDeclarationSyntax methodDeclarationSyntax)
            {
                descriptor.ReturnTypeName = methodDeclarationSyntax.ReturnType.ToString();
                descriptor.Body = expressionSyntaxRewriter.Visit(methodDeclarationSyntax.ExpressionBody.Expression);
                descriptor.ParametersListString = methodDeclarationSyntax.ParameterList.ToString();
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
