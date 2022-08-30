using EntityFrameworkCore.Projectables.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables.Generator
{
    [Generator]
    public class ProjectionExpressionGenerator : IIncrementalGenerator
    {
        private const string ProjectablesAttributeName = "EntityFrameworkCore.Projectables.ProjectableAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Do a simple filter for members
            IncrementalValuesProvider<MemberDeclarationSyntax> memberDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (s, _) => s is MemberDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: static (c, _) => GetSemanticTargetForGeneration(c)) 
                .Where(static m => m is not null)!; // filter out attributed enums that we don't care about

            // Combine the selected enums with the `Compilation`
            IncrementalValueProvider<(Compilation, ImmutableArray<MemberDeclarationSyntax>)> compilationAndEnums
                = context.CompilationProvider.Combine(memberDeclarations.Collect());

            // Generate the source using the compilation and enums
            context.RegisterSourceOutput(compilationAndEnums,
                static (spc, source) => Execute(source.Item1, source.Item2, spc));
        }

        static MemberDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
        {
            // we know the node is a MemberDeclarationSyntax
            var memberDeclarationSyntax = (MemberDeclarationSyntax)context.Node;

            // loop through all the attributes on the method
            foreach (var attributeListSyntax in memberDeclarationSyntax.AttributeLists)
            {
                foreach (var attributeSyntax in attributeListSyntax.Attributes)
                {
                    if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
                    {
                        // weird, we couldn't get the symbol, ignore it
                        continue;
                    }

                    var attributeContainingTypeSymbol = attributeSymbol.ContainingType;
                    var fullName = attributeContainingTypeSymbol.ToDisplayString();

                    // Is the attribute the [Projcetable] attribute?
                    if (fullName == ProjectablesAttributeName)
                    {
                        // return the enum
                        return memberDeclarationSyntax;
                    }
                }
            }

            // we didn't find the attribute we were looking for
            return null;
        }

        static void Execute(Compilation compilation, ImmutableArray<MemberDeclarationSyntax> members, SourceProductionContext context)
        {
            if (members.IsDefaultOrEmpty)
            {
                // nothing to do yet
                return;
            }

            var projectables = members
                .Select(x => ProjectableInterpreter.GetDescriptor(compilation, x, context))
                .Where(x => x is not null)
                .Select(x => x!);

            var resultBuilder = new StringBuilder();

            foreach (var projectable in projectables)
            {
                if (projectable.MemberName is null)
                {
                    throw new InvalidOperationException("Expected a memberName here");
                }

                resultBuilder.Clear();

                if (projectable.UsingDirectives is not null)
                {
                    foreach (var usingDirective in projectable.UsingDirectives.Distinct())
                    {
                        resultBuilder.AppendLine(usingDirective);
                    }
                }

                if (projectable.TargetClassNamespace is not null)
                {
                    var targetClassUsingDirective = $"using {projectable.TargetClassNamespace};";

                    if (!projectable.UsingDirectives.Contains(targetClassUsingDirective))
                    {
                        resultBuilder.AppendLine(targetClassUsingDirective);
                    }
                }

                if (projectable.ClassNamespace is not null && projectable.ClassNamespace != projectable.TargetClassNamespace)
                {
                    var classUsingDirective = $"using {projectable.ClassNamespace};";

                    if (!projectable.UsingDirectives.Contains(classUsingDirective))
                    {
                        resultBuilder.AppendLine(classUsingDirective);
                    }
                }

                var generatedClassName = ProjectionExpressionClassNameGenerator.GenerateName(projectable.ClassNamespace, projectable.NestedInClassNames, projectable.MemberName);

                var lambdaTypeArguments = SyntaxFactory.TypeArgumentList(
                    SyntaxFactory.SeparatedList(
                        projectable.ParametersList?.Parameters.Where(p => p.Type is not null).Select(p => p.Type!)
                    )
                );

                resultBuilder.Append($@"
// <auto-generated/>
namespace EntityFrameworkCore.Projectables.Generated
#nullable disable
{{
    public static class {generatedClassName}
    {{
        public static System.Linq.Expressions.Expression<System.Func<{lambdaTypeArguments.Arguments}, {projectable.ReturnTypeName}>> Expression{(projectable.TypeParameterList?.Parameters.Any() == true ? projectable.TypeParameterList.ToString() : string.Empty)}()");

                if (projectable.ConstraintClauses is not null)
                {
                    foreach (var constraintClause in projectable.ConstraintClauses)
                    {
                        resultBuilder.Append($@"
            {constraintClause}");
                    }
                }

                resultBuilder.Append($@"
        {{
            return {projectable.ParametersList} => 
                {projectable.Body};
        }}
    }}
}}");


                context.AddSource($"{generatedClassName}_Generated", SourceText.From(resultBuilder.ToString(), Encoding.UTF8));
            }
        }

    }
}
