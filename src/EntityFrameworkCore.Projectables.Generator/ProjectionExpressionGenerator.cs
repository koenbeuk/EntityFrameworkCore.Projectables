using EntityFrameworkCore.Projectables.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

            // Build the projection registry: collect all entries and emit a single registry file
            IncrementalValuesProvider<ProjectableRegistryEntry?> registryEntries =
                compilationAndMemberPairs.Select(
                    static (pair, _) => ExtractRegistryEntry(pair.Item1, pair.Item2));

            IncrementalValueProvider<ImmutableArray<ProjectableRegistryEntry?>> allEntries =
                registryEntries.Collect();

            context.RegisterImplementationSourceOutput(
                allEntries,
                static (spc, entries) => EmitRegistry(entries, spc));
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

            var classSyntax = ClassDeclaration(generatedClassName)
                .WithModifiers(TokenList(Token(SyntaxKind.StaticKeyword)))
                .WithTypeParameterList(projectable.ClassTypeParameterList)
                .WithConstraintClauses(projectable.ClassConstraintClauses ?? List<TypeParameterConstraintClauseSyntax>())
                .AddAttributeLists(
                    AttributeList()
                        .AddAttributes(_editorBrowsableAttribute)
                )
                .WithLeadingTrivia(member is ConstructorDeclarationSyntax ctor ? BuildSourceDocComment(ctor, compilation) : TriviaList())
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

#nullable restore

        /// <summary>
        /// Extracts a <see cref="ProjectableRegistryEntry"/> from a member declaration.
        /// Returns null when the member does not have [Projectable], is an extension member,
        /// or cannot be represented in the registry (e.g. a generic class member or generic method).
        /// </summary>
        static ProjectableRegistryEntry? ExtractRegistryEntry(MemberDeclarationSyntax member, Compilation compilation)
        {
            var semanticModel = compilation.GetSemanticModel(member.SyntaxTree);
            var memberSymbol = semanticModel.GetDeclaredSymbol(member);

            if (memberSymbol is null)
                return null;

            // Verify [Projectable] attribute
            var projectableAttributeTypeSymbol = compilation.GetTypeByMetadataName("EntityFrameworkCore.Projectables.ProjectableAttribute");
            var projectableAttribute = memberSymbol.GetAttributes()
                .FirstOrDefault(x => x.AttributeClass?.Name == "ProjectableAttribute");

            if (projectableAttribute is null ||
                !SymbolEqualityComparer.Default.Equals(projectableAttribute.AttributeClass, projectableAttributeTypeSymbol))
                return null;

            // Skip C# 14 extension type members — they require special handling (fall back to reflection)
            if (memberSymbol.ContainingType is { IsExtension: true })
                return null;

            var containingType = memberSymbol.ContainingType;
            bool isGenericClass = containingType.TypeParameters.Length > 0;

            // Determine member kind and lookup name
            string memberKind;
            string memberLookupName;
            ImmutableArray<string> parameterTypeNames = ImmutableArray<string>.Empty;
            int methodTypeParamCount = 0;
            bool isGenericMethod = false;

            if (memberSymbol is IMethodSymbol methodSymbol)
            {
                isGenericMethod = methodSymbol.TypeParameters.Length > 0;
                methodTypeParamCount = methodSymbol.TypeParameters.Length;

                if (methodSymbol.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor)
                {
                    memberKind = "Constructor";
                    memberLookupName = "_ctor";
                }
                else
                {
                    memberKind = "Method";
                    memberLookupName = memberSymbol.Name;
                }

                parameterTypeNames = methodSymbol.Parameters
                    .Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .ToImmutableArray();
            }
            else
            {
                memberKind = "Property";
                memberLookupName = memberSymbol.Name;
            }

            // Build the generated class name using the same logic as Execute
            string? classNamespace = containingType.ContainingNamespace.IsGlobalNamespace
                ? null
                : containingType.ContainingNamespace.ToDisplayString();

            var nestedTypePath = GetRegistryNestedTypePath(containingType);

            var generatedClassName = ProjectionExpressionClassNameGenerator.GenerateName(
                classNamespace,
                nestedTypePath,
                memberLookupName,
                parameterTypeNames.IsEmpty ? null : (IEnumerable<string>)parameterTypeNames);

            var generatedClassFullName = "EntityFrameworkCore.Projectables.Generated." + generatedClassName;

            var declaringTypeFullName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            return new ProjectableRegistryEntry(
                DeclaringTypeFullName: declaringTypeFullName,
                MemberKind: memberKind,
                MemberLookupName: memberLookupName,
                GeneratedClassFullName: generatedClassFullName,
                IsGenericClass: isGenericClass,
                ClassTypeParamCount: containingType.TypeParameters.Length,
                IsGenericMethod: isGenericMethod,
                MethodTypeParamCount: methodTypeParamCount,
                ParameterTypeNames: parameterTypeNames);
        }

        static IEnumerable<string> GetRegistryNestedTypePath(INamedTypeSymbol typeSymbol)
        {
            if (typeSymbol.ContainingType is not null)
            {
                foreach (var name in GetRegistryNestedTypePath(typeSymbol.ContainingType))
                    yield return name;
            }
            yield return typeSymbol.Name;
        }

        /// <summary>
        /// Emits the <c>ProjectionRegistry.g.cs</c> file that aggregates all projectable members
        /// into a single static dictionary keyed by <see cref="System.RuntimeMethodHandle.Value"/>.
        /// Uses SyntaxFactory for the class/method/field structure, consistent with <see cref="Execute"/>.
        /// </summary>
        static void EmitRegistry(ImmutableArray<ProjectableRegistryEntry?> entries, SourceProductionContext context)
        {
            var validEntries = entries
                .Where(e => e is not null)
                .Select(e => e!)
                .ToList();

            if (validEntries.Count == 0)
                return;

            // Build the Build() method body: one block per valid (non-generic) entry
            var buildStatements = new List<StatementSyntax>
            {
                LocalDeclarationStatement(
                    VariableDeclaration(ParseTypeName("var"))
                        .AddVariables(
                            VariableDeclarator("map")
                                .WithInitializer(EqualsValueClause(
                                    ObjectCreationExpression(
                                        ParseTypeName("Dictionary<nint, LambdaExpression>"))
                                        .WithArgumentList(ArgumentList()))))),
            };

            foreach (var entry in validEntries)
            {
                var block = BuildRegistryEntryBlock(entry);
                if (block is not null)
                    buildStatements.Add(block);
            }

            buildStatements.Add(ReturnStatement(IdentifierName("map")));

            var classSyntax = ClassDeclaration("ProjectionRegistry")
                .WithModifiers(TokenList(
                    Token(SyntaxKind.InternalKeyword),
                    Token(SyntaxKind.StaticKeyword)))
                .AddAttributeLists(AttributeList().AddAttributes(_editorBrowsableAttribute))
                .AddMembers(
                    // private static readonly Dictionary<nint, LambdaExpression> _map = Build();
                    FieldDeclaration(
                        VariableDeclaration(ParseTypeName("Dictionary<nint, LambdaExpression>"))
                            .AddVariables(
                                VariableDeclarator("_map")
                                    .WithInitializer(EqualsValueClause(
                                        InvocationExpression(IdentifierName("Build"))
                                            .WithArgumentList(ArgumentList())))))
                        .WithModifiers(TokenList(
                            Token(SyntaxKind.PrivateKeyword),
                            Token(SyntaxKind.StaticKeyword),
                            Token(SyntaxKind.ReadOnlyKeyword))),

                    // public static LambdaExpression TryGet(MemberInfo member)
                    MethodDeclaration(ParseTypeName("LambdaExpression"), "TryGet")
                        .WithModifiers(TokenList(
                            Token(SyntaxKind.PublicKeyword),
                            Token(SyntaxKind.StaticKeyword)))
                        .AddParameterListParameters(
                            Parameter(Identifier("member"))
                                .WithType(ParseTypeName("MemberInfo")))
                        .WithBody(Block(
                            LocalDeclarationStatement(
                                VariableDeclaration(ParseTypeName("var"))
                                    .AddVariables(
                                        VariableDeclarator("handle")
                                            .WithInitializer(EqualsValueClause(
                                                InvocationExpression(IdentifierName("GetHandle"))
                                                    .AddArgumentListArguments(
                                                        Argument(IdentifierName("member"))))))),
                            ReturnStatement(
                                ParseExpression(
                                    "handle.HasValue && _map.TryGetValue(handle.Value, out var expr) ? expr : null")))),

                    // private static nint? GetHandle(MemberInfo member) => member switch { ... };
                    MethodDeclaration(ParseTypeName("nint?"), "GetHandle")
                        .WithModifiers(TokenList(
                            Token(SyntaxKind.PrivateKeyword),
                            Token(SyntaxKind.StaticKeyword)))
                        .AddParameterListParameters(
                            Parameter(Identifier("member"))
                                .WithType(ParseTypeName("MemberInfo")))
                        .WithExpressionBody(ArrowExpressionClause(
                            SwitchExpression(IdentifierName("member"))
                                .WithArms(SeparatedList<SwitchExpressionArmSyntax>(
                                    new SyntaxNodeOrToken[]
                                    {
                                        SwitchExpressionArm(
                                            DeclarationPattern(
                                                ParseTypeName("MethodInfo"),
                                                SingleVariableDesignation(Identifier("m"))),
                                            ParseExpression("m.MethodHandle.Value")),
                                        Token(SyntaxKind.CommaToken),
                                        SwitchExpressionArm(
                                            DeclarationPattern(
                                                ParseTypeName("PropertyInfo"),
                                                SingleVariableDesignation(Identifier("p"))),
                                            ParseExpression("p.GetMethod?.MethodHandle.Value")),
                                        Token(SyntaxKind.CommaToken),
                                        SwitchExpressionArm(
                                            DeclarationPattern(
                                                ParseTypeName("ConstructorInfo"),
                                                SingleVariableDesignation(Identifier("c"))),
                                            ParseExpression("c.MethodHandle.Value")),
                                        Token(SyntaxKind.CommaToken),
                                        SwitchExpressionArm(
                                            DiscardPattern(),
                                            LiteralExpression(SyntaxKind.NullLiteralExpression))
                                    }))))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),

                    // private static Dictionary<nint, LambdaExpression> Build()
                    MethodDeclaration(ParseTypeName("Dictionary<nint, LambdaExpression>"), "Build")
                        .WithModifiers(TokenList(
                            Token(SyntaxKind.PrivateKeyword),
                            Token(SyntaxKind.StaticKeyword)))
                        .WithBody(Block(buildStatements)));

            var compilationUnit = CompilationUnit()
                .AddUsings(
                    UsingDirective(ParseName("System")),
                    UsingDirective(ParseName("System.Collections.Generic")),
                    UsingDirective(ParseName("System.Linq.Expressions")),
                    UsingDirective(ParseName("System.Reflection")))
                .AddMembers(
                    NamespaceDeclaration(ParseName("EntityFrameworkCore.Projectables.Generated"))
                        .AddMembers(classSyntax))
                .WithLeadingTrivia(TriviaList(
                    Comment("// <auto-generated/>"),
                    Trivia(NullableDirectiveTrivia(Token(SyntaxKind.DisableKeyword), true))));

            context.AddSource("ProjectionRegistry.g.cs",
                SourceText.From(compilationUnit.NormalizeWhitespace().ToFullString(), Encoding.UTF8));
        }

        /// <summary>
        /// Builds the registration block for a single projectable entry inside <c>Build()</c>.
        /// Returns <see langword="null"/> for generic class/method entries (they fall back to reflection).
        /// </summary>
        static BlockSyntax? BuildRegistryEntryBlock(ProjectableRegistryEntry entry)
        {
            if (entry.IsGenericClass || entry.IsGenericMethod)
                return null;

            var bindingFlagsExpr = ParseExpression(
                "global::System.Reflection.BindingFlags.Public | " +
                "global::System.Reflection.BindingFlags.NonPublic | " +
                "global::System.Reflection.BindingFlags.Instance | " +
                "global::System.Reflection.BindingFlags.Static");

            // var t = typeof(DeclaringType);
            var tDecl = LocalDeclarationStatement(
                VariableDeclaration(ParseTypeName("var"))
                    .AddVariables(
                        VariableDeclarator("t")
                            .WithInitializer(EqualsValueClause(
                                TypeOfExpression(ParseTypeName(entry.DeclaringTypeFullName))))));

            // var m = t.GetProperty(...) / t.GetMethod(...) / t.GetConstructor(...);
            StatementSyntax? mDecl = entry.MemberKind switch
            {
                "Property" => LocalDeclarationStatement(
                    VariableDeclaration(ParseTypeName("var"))
                        .AddVariables(
                            VariableDeclarator("m")
                                .WithInitializer(EqualsValueClause(
                                    ConditionalAccessExpression(
                                        InvocationExpression(
                                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                                IdentifierName("t"),
                                                IdentifierName("GetProperty")))
                                            .AddArgumentListArguments(
                                                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                                    Literal(entry.MemberLookupName))),
                                                Argument(bindingFlagsExpr)),
                                        MemberBindingExpression(IdentifierName("GetMethod"))))))),

                "Method" => LocalDeclarationStatement(
                    VariableDeclaration(ParseTypeName("var"))
                        .AddVariables(
                            VariableDeclarator("m")
                                .WithInitializer(EqualsValueClause(
                                    InvocationExpression(
                                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName("t"),
                                            IdentifierName("GetMethod")))
                                        .AddArgumentListArguments(
                                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                                Literal(entry.MemberLookupName))),
                                            Argument(bindingFlagsExpr),
                                            Argument(LiteralExpression(SyntaxKind.NullLiteralExpression)),
                                            Argument(BuildTypeArrayExpr(entry.ParameterTypeNames)),
                                            Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))))))),

                "Constructor" => LocalDeclarationStatement(
                    VariableDeclaration(ParseTypeName("var"))
                        .AddVariables(
                            VariableDeclarator("m")
                                .WithInitializer(EqualsValueClause(
                                    InvocationExpression(
                                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName("t"),
                                            IdentifierName("GetConstructor")))
                                        .AddArgumentListArguments(
                                            Argument(bindingFlagsExpr),
                                            Argument(LiteralExpression(SyntaxKind.NullLiteralExpression)),
                                            Argument(BuildTypeArrayExpr(entry.ParameterTypeNames)),
                                            Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))))))),

                _ => null
            };

            if (mDecl is null)
                return null;

            // var exprType = t.Assembly.GetType("GeneratedClassFullName");
            var exprTypeDecl = LocalDeclarationStatement(
                VariableDeclaration(ParseTypeName("var"))
                    .AddVariables(
                        VariableDeclarator("exprType")
                            .WithInitializer(EqualsValueClause(
                                InvocationExpression(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName("t"),
                                            IdentifierName("Assembly")),
                                        IdentifierName("GetType")))
                                    .AddArgumentListArguments(
                                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                            Literal(entry.GeneratedClassFullName))))))));

            // var exprMethod = exprType?.GetMethod("Expression", BindingFlags.Static | BindingFlags.NonPublic);
            var exprMethodDecl = LocalDeclarationStatement(
                VariableDeclaration(ParseTypeName("var"))
                    .AddVariables(
                        VariableDeclarator("exprMethod")
                            .WithInitializer(EqualsValueClause(
                                ConditionalAccessExpression(
                                    IdentifierName("exprType"),
                                    InvocationExpression(
                                        MemberBindingExpression(IdentifierName("GetMethod")))
                                        .AddArgumentListArguments(
                                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                                Literal("Expression"))),
                                            Argument(ParseExpression(
                                                "global::System.Reflection.BindingFlags.Static | " +
                                                "global::System.Reflection.BindingFlags.NonPublic"))))))));

            // if (exprMethod != null)
            //     map[m.MethodHandle.Value] = (LambdaExpression)exprMethod.Invoke(null, null)!;
            var ifExprMethod = IfStatement(
                BinaryExpression(SyntaxKind.NotEqualsExpression,
                    IdentifierName("exprMethod"),
                    LiteralExpression(SyntaxKind.NullLiteralExpression)),
                ExpressionStatement(
                    AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                        ElementAccessExpression(IdentifierName("map"))
                            .AddArgumentListArguments(
                                Argument(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName("m"),
                                            IdentifierName("MethodHandle")),
                                        IdentifierName("Value")))),
                        CastExpression(
                            ParseTypeName("global::System.Linq.Expressions.LambdaExpression"),
                            PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression,
                                InvocationExpression(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName("exprMethod"),
                                        IdentifierName("Invoke")))
                                    .AddArgumentListArguments(
                                        Argument(LiteralExpression(SyntaxKind.NullLiteralExpression)),
                                        Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))))))));

            // if (m != null) { exprType decl; exprMethod decl; if (exprMethod != null) ... }
            var ifM = IfStatement(
                BinaryExpression(SyntaxKind.NotEqualsExpression,
                    IdentifierName("m"),
                    LiteralExpression(SyntaxKind.NullLiteralExpression)),
                Block(exprTypeDecl, exprMethodDecl, ifExprMethod));

            return Block(tDecl, mDecl, ifM);
        }

        /// <summary>
        /// Builds the <c>typeof(...)</c>-array expression used for reflection method/constructor lookup.
        /// Returns <c>global::System.Type.EmptyTypes</c> when there are no parameters.
        /// </summary>
        static ExpressionSyntax BuildTypeArrayExpr(ImmutableArray<string> parameterTypeNames)
        {
            if (parameterTypeNames.IsEmpty)
                return ParseExpression("global::System.Type.EmptyTypes");

            var typeofExprs = parameterTypeNames
                .Select(name => (ExpressionSyntax)TypeOfExpression(ParseTypeName(name)))
                .ToArray();

            return ArrayCreationExpression(
                ArrayType(ParseTypeName("global::System.Type"))
                    .AddRankSpecifiers(ArrayRankSpecifier()))
                .WithInitializer(
                    InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                        SeparatedList<ExpressionSyntax>(typeofExprs)));
        }

    }
}
