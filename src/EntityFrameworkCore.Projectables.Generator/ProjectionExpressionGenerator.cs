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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace EntityFrameworkCore.Projectables.Generator
{
    [Generator]
    public class ProjectionExpressionGenerator : IIncrementalGenerator
    {
        private const string ProjectablesAttributeName = "EntityFrameworkCore.Projectables.ProjectableAttribute";

        static readonly AttributeSyntax _editorBrowsableAttribute = 
            Attribute(
                ParseName("global::System.ComponentModel.EditorBrowsable"),
                AttributeArgumentList(
                    SingletonSeparatedList(
                        AttributeArgument(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("global::System.ComponentModel.EditorBrowsableState"),
                                IdentifierName("Never")
                            )
                        )
                    )
                )
            );

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

                var generatedClassName = ProjectionExpressionClassNameGenerator.GenerateName(projectable.ClassNamespace, projectable.NestedInClassNames, projectable.MemberName);
                var generatedFileName = projectable.ClassTypeParameterList is not null ? $"{generatedClassName}-{projectable.ClassTypeParameterList.ChildNodes().Count()}.g.cs" : $"{generatedClassName}.g.cs";

                var classSyntax = ClassDeclaration(generatedClassName)
                    .WithModifiers(TokenList(Token(SyntaxKind.StaticKeyword)))
                    .WithTypeParameterList(projectable.ClassTypeParameterList)
                    .WithConstraintClauses(projectable.ClassConstraintClauses ?? List<TypeParameterConstraintClauseSyntax>())
                    .AddAttributeLists(
                        AttributeList()
                            .AddAttributes(_editorBrowsableAttribute)
                    )
                    .AddMembers(
                        MethodDeclaration(
                            GenericName(
                               Identifier("global::System.Linq.Expressions.Expression"),
                               TypeArgumentList(
                                    SingletonSeparatedList(
                                        (TypeSyntax)GenericName(
                                            Identifier("global::System.Func"),
                                            GetLambdaTypeArgumentListSyntax(projectable)
                                        )
                                    )
                                )
                            ),
                            "Expression"
                        )
                        .WithModifiers(TokenList(Token(SyntaxKind.StaticKeyword)))
                        .WithTypeParameterList(projectable.TypeParameterList)
                        .WithConstraintClauses(projectable.ConstraintClauses ?? List<TypeParameterConstraintClauseSyntax>())
                        .WithBody(
                            Block(
                                ReturnStatement(
                                    ParenthesizedLambdaExpression(
                                        projectable.ParametersList ?? ParameterList(),
                                        null,
                                        projectable.ExpressionBody
                                    )
                                )
                            )
                         )
                    );

#nullable disable

                var compilationUnit = CompilationUnit();

                foreach (var usingDirective in projectable.UsingDirectives)
                {
                    compilationUnit = compilationUnit.AddUsings(usingDirective);
                }

                if (projectable.ClassNamespace is not null)
                {
                    compilationUnit = compilationUnit.AddUsings(
                        UsingDirective(
                            ParseName(projectable.ClassNamespace)
                        )
                    );
                }

                compilationUnit = compilationUnit
                    .AddMembers(
                        NamespaceDeclaration(
                            ParseName("EntityFrameworkCore.Projectables.Generated")
                        ).AddMembers(classSyntax)
                    )
                    .WithLeadingTrivia(
                        TriviaList(
                            Comment("// <auto-generated/>"),
                            Trivia(NullableDirectiveTrivia(Token(SyntaxKind.DisableKeyword), true))
                        )
                    );


                context.AddSource(generatedFileName, SourceText.From(compilationUnit.NormalizeWhitespace().ToFullString(), Encoding.UTF8));


                static TypeArgumentListSyntax GetLambdaTypeArgumentListSyntax(ProjectableDescriptor projectable)
                {
                    var lambdaTypeArguments = TypeArgumentList(
                        SeparatedList(
                            // TODO: Document where clause
                            projectable.ParametersList?.Parameters.Where(p => p.Type is not null).Select(p => p.Type!)
                        )
                    );

                    if (projectable.ReturnTypeName is not null)
                    {
                        lambdaTypeArguments = lambdaTypeArguments.AddArguments(ParseTypeName(projectable.ReturnTypeName));
                    }

                    return lambdaTypeArguments;
                }
            }
        }
    }
}
