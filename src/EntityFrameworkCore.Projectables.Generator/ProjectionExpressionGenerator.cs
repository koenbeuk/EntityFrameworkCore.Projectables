using EntityFrameworkCore.Projectables.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;
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
                .ForAttributeWithMetadataName(
                    ProjectablesAttributeName,
                    predicate: static (s, _) => s is MemberDeclarationSyntax,
                    transform: static (c, _) => (MemberDeclarationSyntax)c.TargetNode)
                .WithComparer(new MemberDeclarationSyntaxEqualityComparer());

            // Combine the selected enums with the `Compilation`
            IncrementalValuesProvider<(MemberDeclarationSyntax, Compilation)> compilationAndMemberPairs = memberDeclarations
                .Combine(context.CompilationProvider)
                .WithComparer(new MemberDeclarationSyntaxAndCompilationEqualityComparer());

            // Generate the source using the compilation and enums
            context.RegisterImplementationSourceOutput(compilationAndMemberPairs,
                static (spc, source) => Execute(source.Item1, source.Item2, spc));
        }

        static void Execute(MemberDeclarationSyntax member, Compilation compilation, SourceProductionContext context)
        {
            var projectable = ProjectableInterpreter.GetDescriptor(compilation, member, context);

            if (projectable is null)
            {
                return;
            }

            if (projectable.MemberName is null)
            {
                throw new InvalidOperationException("Expected a memberName here");
            }

            var generatedClassName = ProjectionExpressionClassNameGenerator.GenerateName(projectable.ClassNamespace, projectable.NestedInClassNames, projectable.MemberName, projectable.ParameterTypeNames);
            var generatedFileName = projectable.ClassTypeParameterList is not null ? $"{generatedClassName}-{projectable.ClassTypeParameterList.ChildNodes().Count()}.g.cs" : $"{generatedClassName}.g.cs";

            // Build the Expression method (shared between partial and non-partial generation)
            var expressionMethod = MethodDeclaration(
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
                );

#nullable disable

            var compilationUnit = CompilationUnit();

            foreach (var usingDirective in projectable.UsingDirectives)
            {
                compilationUnit = compilationUnit.AddUsings(usingDirective);
            }

            if (projectable.IsContainingClassPartial && projectable.ContainingTypeChain is not null)
            {
                // Generate the Expression class nested inside the partial containing class(es).
                // This allows access to private/protected members of the containing class.
                // The nested class does NOT redeclare the outer class's type parameters.
                var nestedClassSyntax = ClassDeclaration(generatedClassName)
                    .WithModifiers(TokenList(Token(SyntaxKind.StaticKeyword)))
                    .AddAttributeLists(
                        AttributeList()
                            .AddAttributes(_editorBrowsableAttribute)
                    )
                    .AddMembers(expressionMethod);

                // Wrap in the chain of partial class declarations (innermost first, then we reverse)
                MemberDeclarationSyntax wrapped = nestedClassSyntax;
                foreach (var typeDecl in projectable.ContainingTypeChain.Reverse())
                {
                    wrapped = typeDecl
                        .WithAttributeLists(List<AttributeListSyntax>())
                        .WithBaseList(null)
                        .WithMembers(SingletonList<MemberDeclarationSyntax>(wrapped));
                }

                if (projectable.ClassNamespace is not null)
                {
                    compilationUnit = compilationUnit.AddMembers(
                        NamespaceDeclaration(
                            ParseName(projectable.ClassNamespace)
                        ).AddMembers(wrapped)
                    );
                }
                else
                {
                    compilationUnit = compilationUnit.AddMembers(wrapped);
                }
            }
            else
            {
                if (projectable.ClassNamespace is not null)
                {
                    // Only add the class namespace using if not already present
                    var classNamespace = projectable.ClassNamespace;
                    if (!projectable.UsingDirectives.Any(u => u.Name?.ToString() == classNamespace))
                    {
                        compilationUnit = compilationUnit.AddUsings(
                            UsingDirective(
                                ParseName(classNamespace)
                            )
                        );
                    }
                }

                var classSyntax = ClassDeclaration(generatedClassName)
                    .WithModifiers(TokenList(Token(SyntaxKind.StaticKeyword)))
                    .WithTypeParameterList(projectable.ClassTypeParameterList)
                    .WithConstraintClauses(projectable.ClassConstraintClauses ?? List<TypeParameterConstraintClauseSyntax>())
                    .AddAttributeLists(
                        AttributeList()
                            .AddAttributes(_editorBrowsableAttribute)
                    )
                    .AddMembers(expressionMethod);

                compilationUnit = compilationUnit
                    .AddMembers(
                        NamespaceDeclaration(
                            ParseName("EntityFrameworkCore.Projectables.Generated")
                        ).AddMembers(classSyntax)
                    );
            }

            compilationUnit = compilationUnit
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
