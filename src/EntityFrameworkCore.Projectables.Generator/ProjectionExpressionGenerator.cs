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
            // Extract only pure stable data from the attribute in the transform.
            // No live Roslyn objects (no AttributeData, SemanticModel, Compilation, ISymbol) —
            // those are always new instances and defeat incremental caching entirely.
            var memberDeclarations = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    ProjectablesAttributeName,
                    predicate: static (s, _) => s is MemberDeclarationSyntax,
                    transform: static (c, _) => (
                        Member: (MemberDeclarationSyntax)c.TargetNode,
                        Attribute: new ProjectableAttributeData(c.Attributes[0])
                    ));

            var compilationAndMemberPairs = memberDeclarations
                .Combine(context.CompilationProvider)
                .WithComparer(new MemberDeclarationSyntaxAndCompilationEqualityComparer());
            
            context.RegisterSourceOutput(compilationAndMemberPairs,
                static (spc, source) =>
                {
                    var ((member, attribute), compilation) = source;
                    var semanticModel = compilation.GetSemanticModel(member.SyntaxTree);
                    var memberSymbol = semanticModel.GetDeclaredSymbol(member);
                    
                    if (memberSymbol is null)
                    {
                        return;
                    }

                    Execute(member, semanticModel, memberSymbol, attribute, compilation, spc);
                });
        }

        static SyntaxTriviaList BuildSourceDocComment(ConstructorDeclarationSyntax ctor, Compilation compilation)
        {
            var chain = CollectConstructorChain(ctor, compilation);

            var lines = new List<SyntaxTrivia>();

            void AddLine(string text)
            {
                lines.Add(Comment(text));
                lines.Add(CarriageReturnLineFeed);
            }

            AddLine("/// <summary>");
            AddLine("/// <para>Generated from:</para>");

            foreach (var ctorSyntax in chain)
            {
                AddLine("/// <code>");
                var originalSource = ctorSyntax.NormalizeWhitespace().ToFullString();
                foreach (var rawLine in originalSource.Split('\n'))
                {
                    var lineText = rawLine.TrimEnd('\r')
                        .Replace("&", "&amp;")
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;");
                    AddLine($"/// {lineText}");
                }
                AddLine("/// </code>");
            }

            AddLine("/// </summary>");

            return TriviaList(lines);
        }

        /// <summary>
        /// Collects the constructor and every constructor it delegates to via <c>this(...)</c> or
        /// <c>base(...)</c>, in declaration order (annotated constructor first, then its delegate,
        /// then its delegate's delegate, …). Stops when a delegated constructor has no source
        /// available in the compilation (e.g. a compiler-synthesised parameterless constructor).
        /// </summary>
        static IReadOnlyList<ConstructorDeclarationSyntax> CollectConstructorChain(
            ConstructorDeclarationSyntax ctor, Compilation compilation)
        {
            var result = new List<ConstructorDeclarationSyntax> { ctor };
            var visited = new HashSet<SyntaxNode>() { ctor };

            var current = ctor;
            while (current.Initializer is { } initializer)
            {
                var semanticModel = compilation.GetSemanticModel(current.SyntaxTree);
                if (semanticModel.GetSymbolInfo(initializer).Symbol is not IMethodSymbol delegated)
                    break;

                var delegatedSyntax = delegated.DeclaringSyntaxReferences
                    .Select(r => r.GetSyntax())
                    .OfType<ConstructorDeclarationSyntax>()
                    .FirstOrDefault();

                if (delegatedSyntax is null || !visited.Add(delegatedSyntax))
                    break;

                result.Add(delegatedSyntax);
                current = delegatedSyntax;
            }

            return result;
        }

        static void Execute(
            MemberDeclarationSyntax member,
            SemanticModel semanticModel,
            ISymbol memberSymbol,
            ProjectableAttributeData projectableAttribute,
            Compilation? compilation,
            SourceProductionContext context)
        {
            var projectable = ProjectableInterpreter.GetDescriptor(
                semanticModel, member, memberSymbol, projectableAttribute, context, compilation);

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

            var classSyntax = ClassDeclaration(generatedClassName)
                .WithModifiers(TokenList(Token(SyntaxKind.StaticKeyword)))
                .WithTypeParameterList(projectable.ClassTypeParameterList)
                .WithConstraintClauses(projectable.ClassConstraintClauses ?? List<TypeParameterConstraintClauseSyntax>())
                .AddAttributeLists(
                    AttributeList()
                        .AddAttributes(_editorBrowsableAttribute)
                )
                .WithLeadingTrivia(member is ConstructorDeclarationSyntax ctor && compilation is not null ? BuildSourceDocComment(ctor, compilation) : TriviaList())
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
                        // Uncomment line below, for debugging purposes, to see when the generator is run on source generated files
                        // CarriageReturnLineFeed, Comment($"// Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC for '{memberSymbol.Name}' in '{memberSymbol.ContainingType?.Name}'"),
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
