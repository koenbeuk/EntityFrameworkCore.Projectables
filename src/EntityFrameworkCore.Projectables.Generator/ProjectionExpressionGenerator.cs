using EntityFrameworkCore.Projectables.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace EntityFrameworkCore.Projectables.Generator
{
    [Generator]
    public class ProjectionExpressionGenerator : IIncrementalGenerator
    {
        private const string ProjectablesAttributeName = "EntityFrameworkCore.Projectables.ProjectableAttribute";

        private readonly static AttributeSyntax _editorBrowsableAttribute = 
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
        
        private static MethodDeclarationSyntax? _registerHelperMethod;
        private static FieldDeclarationSyntax? _mapField;
        private static MethodDeclarationSyntax? _tryGetMethod;
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
            
            // Build the projection registry: collect all entries and emit a single registry file
            var registryEntries = compilationAndMemberPairs.Select(
                static (source, cancellationToken) => {
                    var ((member, _), compilation) = source;
                    
                    var semanticModel = compilation.GetSemanticModel(member.SyntaxTree);
                    var memberSymbol = semanticModel.GetDeclaredSymbol(member, cancellationToken);

                    if (memberSymbol is null)
                    {
                        return null;
                    }
                    
                    return ExtractRegistryEntry(memberSymbol);
                });

            context.RegisterImplementationSourceOutput(
                registryEntries.Collect(),
                static (spc, entries) => EmitRegistry(entries, spc));
        }

        private static SyntaxTriviaList BuildSourceDocComment(ConstructorDeclarationSyntax ctor, Compilation compilation)
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
        private static List<ConstructorDeclarationSyntax> CollectConstructorChain(
            ConstructorDeclarationSyntax ctor, Compilation compilation)
        {
            var result = new List<ConstructorDeclarationSyntax> { ctor };
            var visited = new HashSet<SyntaxNode>() { ctor };

            var current = ctor;
            while (current.Initializer is { } initializer)
            {
                var semanticModel = compilation.GetSemanticModel(current.SyntaxTree);
                if (semanticModel.GetSymbolInfo(initializer).Symbol is not IMethodSymbol delegated)
                {
                    break;
                }

                var delegatedSyntax = delegated.DeclaringSyntaxReferences
                    .Select(r => r.GetSyntax())
                    .OfType<ConstructorDeclarationSyntax>()
                    .FirstOrDefault();

                if (delegatedSyntax is null || !visited.Add(delegatedSyntax))
                {
                    break;
                }

                result.Add(delegatedSyntax);
                current = delegatedSyntax;
            }

            return result;
        }

        private static void Execute(
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

            foreach (var usingDirective in projectable.UsingDirectives!)
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

#nullable restore

        /// <summary>
        /// Extracts a <see cref="ProjectableRegistryEntry"/> from a member declaration.
        /// Returns null when the member does not have [Projectable], is an extension member,
        /// or cannot be represented in the registry (e.g. a generic class member or generic method).
        /// </summary>
        private static ProjectableRegistryEntry? ExtractRegistryEntry(ISymbol memberSymbol)
        {
            var containingType = memberSymbol.ContainingType;
            
            // Skip C# 14 extension type members — they require special handling (fall back to reflection)
            if (containingType is { IsExtension: true })
            {
                return null;
            }

            // Early exit for generic classes: BuildRegistryEntryStatement returns null for them anyway.
            if (containingType.TypeParameters.Length > 0)
            {
                return null;
            }

            // Determine member kind and lookup name
            ProjectableRegistryMemberType memberKind;
            string memberLookupName;
            var parameterTypeNames = ImmutableArray<string>.Empty;

            if (memberSymbol is IMethodSymbol methodSymbol)
            {
                // Early exit for generic methods
                if (methodSymbol.TypeParameters.Length > 0)
                {
                    return null;
                }

                if (methodSymbol.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor)
                {
                    memberKind = ProjectableRegistryMemberType.Constructor;
                    memberLookupName = "_ctor";
                }
                else
                {
                    memberKind = ProjectableRegistryMemberType.Method;
                    memberLookupName = memberSymbol.Name;
                }

                parameterTypeNames = [
                    ..methodSymbol.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                ];
            }
            else
            {
                memberKind = ProjectableRegistryMemberType.Property;
                memberLookupName = memberSymbol.Name;
            }

            // Build the generated class name using the same logic as Execute
            var classNamespace = containingType.ContainingNamespace.IsGlobalNamespace
                ? null
                : containingType.ContainingNamespace.ToDisplayString();

            var nestedTypePath = GetRegistryNestedTypePath(containingType);

            var generatedClassName = ProjectionExpressionClassNameGenerator.GenerateName(
                classNamespace,
                nestedTypePath,
                memberLookupName,
                parameterTypeNames.IsEmpty ? null : parameterTypeNames);

            var generatedClassFullName = "EntityFrameworkCore.Projectables.Generated." + generatedClassName;

            var declaringTypeFullName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            return new ProjectableRegistryEntry(
                DeclaringTypeFullName: declaringTypeFullName,
                MemberKind: memberKind,
                MemberLookupName: memberLookupName,
                GeneratedClassFullName: generatedClassFullName,
                ParameterTypeNames: parameterTypeNames);
        }

        private static IEnumerable<string> GetRegistryNestedTypePath(INamedTypeSymbol typeSymbol)
        {
            if (typeSymbol.ContainingType is not null)
            {
                foreach (var name in GetRegistryNestedTypePath(typeSymbol.ContainingType))
                {
                    yield return name;
                }
            }
            yield return typeSymbol.Name;
        }

        /// <summary>
        /// Emits the <c>ProjectionRegistry.g.cs</c> file that aggregates all projectable members
        /// into a single static dictionary keyed by <see cref="System.RuntimeMethodHandle.Value"/>.
        /// Uses SyntaxFactory for the class/method/field structure, consistent with <see cref="Execute"/>.
        /// The generated <c>Build()</c> method uses a shared <c>Register</c> helper to avoid repeating
        /// the lookup boilerplate for every entry.
        /// </summary>
        private static void EmitRegistry(ImmutableArray<ProjectableRegistryEntry?> entries, SourceProductionContext context)
        {
            // Build the per-entry Register(...) statements first so we can bail out early
            // if every entry is generic (they all fall back to reflection, no registry needed).
            var entryStatements = entries
                .Where(e => e is not null)
                .Select(e => BuildRegistryEntryStatement(e!))
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();

            if (entryStatements.Count == 0)
            {
                return;
            }

            // Build() body:
            //   const BindingFlags allFlags = ...;
            //   var map = new Dictionary<nint, LambdaExpression>();
            //   Register(map, typeof(T).GetXxx(...), "ClassName");  ← one line per entry
            //   return map;
            var buildStatements = new List<StatementSyntax>
            {
                // const BindingFlags allFlags = BindingFlags.Public | BindingFlags.NonPublic | ...;
                LocalDeclarationStatement(
                        VariableDeclaration(ParseTypeName("BindingFlags"))
                            .AddVariables(
                                VariableDeclarator("allFlags")
                                    .WithInitializer(EqualsValueClause(
                                        ParseExpression(
                                            "BindingFlags.Public | BindingFlags.NonPublic | " +
                                            "BindingFlags.Instance | BindingFlags.Static")))))
                    .WithModifiers(TokenList(Token(SyntaxKind.ConstKeyword))),

                // var map = new Dictionary<nint, LambdaExpression>();
                LocalDeclarationStatement(
                    VariableDeclaration(ParseTypeName("var"))
                        .AddVariables(
                            VariableDeclarator("map")
                                .WithInitializer(EqualsValueClause(
                                    ObjectCreationExpression(
                                            ParseTypeName("Dictionary<nint, LambdaExpression>"))
                                        .WithArgumentList(ArgumentList()))))),
            };

            buildStatements.AddRange(entryStatements);
            buildStatements.Add(ReturnStatement(IdentifierName("map")));

            var classSyntax = ClassDeclaration("ProjectionRegistry")
                .WithModifiers(TokenList(
                    Token(SyntaxKind.InternalKeyword),
                    Token(SyntaxKind.StaticKeyword)))
                .AddAttributeLists(AttributeList().AddAttributes(_editorBrowsableAttribute))
                .AddMembers(
                    // private static Dictionary<nint, LambdaExpression> Build() { ... }
                    MethodDeclaration(ParseTypeName("Dictionary<nint, LambdaExpression>"), "Build")
                        .WithModifiers(TokenList(
                            Token(SyntaxKind.PrivateKeyword),
                            Token(SyntaxKind.StaticKeyword)))
                        .WithBody(Block(buildStatements)),

                    // Cached members — built once and reused across incremental runs
                    BuildMapField(),
                    BuildTryGetMethod(),
                    BuildRegisterHelperMethod());

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
        /// Builds a single compact <c>Register(map, typeof(T).GetXxx(...), "ClassName")</c>
        /// statement for one projectable entry in <c>Build()</c>.
        /// </summary>
        private static ExpressionStatementSyntax? BuildRegistryEntryStatement(ProjectableRegistryEntry entry)
        {
            // typeof(DeclaringType).GetProperty/Method/Constructor(name, allFlags, ...)
            ExpressionSyntax? memberCallExpr = entry.MemberKind switch
            {
                // typeof(T).GetProperty("Name", allFlags)?.GetMethod
                ProjectableRegistryMemberType.Property => ConditionalAccessExpression(
                    InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                TypeOfExpression(ParseTypeName(entry.DeclaringTypeFullName)),
                                IdentifierName("GetProperty")))
                        .AddArgumentListArguments(
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                                Literal(entry.MemberLookupName))),
                            Argument(IdentifierName("allFlags"))),
                    MemberBindingExpression(IdentifierName("GetMethod"))),

                // typeof(T).GetMethod("Name", allFlags, null, new Type[] {...}, null)
                ProjectableRegistryMemberType.Method => InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            TypeOfExpression(ParseTypeName(entry.DeclaringTypeFullName)),
                            IdentifierName("GetMethod")))
                    .AddArgumentListArguments(
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                            Literal(entry.MemberLookupName))),
                        Argument(IdentifierName("allFlags")),
                        Argument(LiteralExpression(SyntaxKind.NullLiteralExpression)),
                        Argument(BuildTypeArrayExpr(entry.ParameterTypeNames)),
                        Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))),

                // typeof(T).GetConstructor(allFlags, null, new Type[] {...}, null)
                ProjectableRegistryMemberType.Constructor => InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            TypeOfExpression(ParseTypeName(entry.DeclaringTypeFullName)),
                            IdentifierName("GetConstructor")))
                    .AddArgumentListArguments(
                        Argument(IdentifierName("allFlags")),
                        Argument(LiteralExpression(SyntaxKind.NullLiteralExpression)),
                        Argument(BuildTypeArrayExpr(entry.ParameterTypeNames)),
                        Argument(LiteralExpression(SyntaxKind.NullLiteralExpression))),

                _ => null
            };

            if (memberCallExpr is null)
            {
                return null;
            }

            // Register(map, <memberCallExpr>, "<generatedClassFullName>");
            return ExpressionStatement(
                InvocationExpression(IdentifierName("Register"))
                    .AddArgumentListArguments(
                        Argument(IdentifierName("map")),
                        Argument(memberCallExpr),
                        Argument(LiteralExpression(SyntaxKind.StringLiteralExpression,
                            Literal(entry.GeneratedClassFullName)))));
        }


        /// <summary>
        /// Builds (and caches) the <c>_map</c> field declaration:
        /// <c>private static readonly Dictionary&lt;nint, LambdaExpression&gt; _map = Build();</c>
        /// </summary>
        private static FieldDeclarationSyntax BuildMapField() => _mapField ??=
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
                    Token(SyntaxKind.ReadOnlyKeyword)));

        /// <summary>
        /// Builds (and caches) the <c>TryGet</c> public static method declaration.
        /// The <c>GetHandle</c> logic is inlined as a switch expression on <c>member</c>.
        /// </summary>
        private static MethodDeclarationSyntax BuildTryGetMethod() => _tryGetMethod ??=
            // public static LambdaExpression TryGet(MemberInfo member)
            // {
            //     var handle = member switch
            //     {
            //         MethodInfo m      => (nint?)m.MethodHandle.Value,
            //         PropertyInfo p    => p.GetMethod?.MethodHandle.Value,
            //         ConstructorInfo c => (nint?)c.MethodHandle.Value,
            //         _                 => null
            //     };
            //     return handle.HasValue && _map.TryGetValue(handle.Value, out var expr) ? expr : null;
            // }
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
                                        SwitchExpression(IdentifierName("member"))
                                            .WithArms(SeparatedList<SwitchExpressionArmSyntax>(
                                                new SyntaxNodeOrToken[]
                                                {
                                                    SwitchExpressionArm(
                                                        DeclarationPattern(
                                                            ParseTypeName("MethodInfo"),
                                                            SingleVariableDesignation(Identifier("m"))),
                                                        ParseExpression("(nint?)m.MethodHandle.Value")),
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
                                                        ParseExpression("(nint?)c.MethodHandle.Value")),
                                                    Token(SyntaxKind.CommaToken),
                                                    SwitchExpressionArm(
                                                        DiscardPattern(),
                                                        LiteralExpression(SyntaxKind.NullLiteralExpression))
                                                })))))),
                    ReturnStatement(
                        ParseExpression(
                            "handle.HasValue && _map.TryGetValue(handle.Value, out var expr) ? expr : null"))));

        /// <summary>
        /// Builds the <c>Register</c> private static helper method that all per-entry calls delegate to.
        /// It handles the null checks and the common reflection lookup pattern once, centrally.
        /// </summary>
        private static MethodDeclarationSyntax BuildRegisterHelperMethod() => _registerHelperMethod ??=
            // private static void Register(Dictionary<nint, LambdaExpression> map, MethodBase m, string exprClass)
            // {
            //     if (m is null) return;
            //     var exprType = m.DeclaringType?.Assembly.GetType(exprClass);
            //     var exprMethod = exprType?.GetMethod("Expression", BindingFlags.Static | BindingFlags.NonPublic);
            //     if (exprMethod is not null)
            //         map[m.MethodHandle.Value] = (LambdaExpression)exprMethod.Invoke(null, null)!;
            // }
            MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), "Register")
                .WithModifiers(TokenList(
                    Token(SyntaxKind.PrivateKeyword),
                    Token(SyntaxKind.StaticKeyword)))
                .AddParameterListParameters(
                    Parameter(Identifier("map"))
                        .WithType(ParseTypeName("Dictionary<nint, LambdaExpression>")),
                    Parameter(Identifier("m"))
                        .WithType(ParseTypeName("MethodBase")),
                    Parameter(Identifier("exprClass"))
                        .WithType(PredefinedType(Token(SyntaxKind.StringKeyword))))
                .WithBody(Block(
                    // if (m is null) return;
                    IfStatement(
                        IsPatternExpression(
                            IdentifierName("m"),
                            ConstantPattern(LiteralExpression(SyntaxKind.NullLiteralExpression))),
                        ReturnStatement()),
                    // var exprType = m.DeclaringType?.Assembly.GetType(exprClass);
                    LocalDeclarationStatement(
                        VariableDeclaration(ParseTypeName("var"))
                            .AddVariables(
                                VariableDeclarator("exprType")
                                    .WithInitializer(EqualsValueClause(
                                        ParseExpression("m.DeclaringType?.Assembly.GetType(exprClass)"))))),
                    // var exprMethod = exprType?.GetMethod("Expression", BindingFlags.Static | BindingFlags.NonPublic);
                    LocalDeclarationStatement(
                        VariableDeclaration(ParseTypeName("var"))
                            .AddVariables(
                                VariableDeclarator("exprMethod")
                                    .WithInitializer(EqualsValueClause(
                                        ParseExpression(
                                            @"exprType?.GetMethod(""Expression"", BindingFlags.Static | BindingFlags.NonPublic)"))))),
                    // if (exprMethod is not null)
                    //     map[m.MethodHandle.Value] = (LambdaExpression)exprMethod.Invoke(null, null)!;
                    IfStatement(
                        ParseExpression("exprMethod is not null"),
                        ExpressionStatement(
                            ParseExpression(
                                "map[m.MethodHandle.Value] = (LambdaExpression)exprMethod.Invoke(null, null)!")))));

        /// <summary>
        /// Builds the <c>typeof(...)</c>-array expression used for reflection method/constructor lookup.
        /// Returns <c>global::System.Type.EmptyTypes</c> when there are no parameters.
        /// </summary>
        private static ExpressionSyntax BuildTypeArrayExpr(ImmutableArray<string> parameterTypeNames)
        {
            if (parameterTypeNames.IsEmpty)
            {
                return ParseExpression("global::System.Type.EmptyTypes");
            }

            var typeofExprs = parameterTypeNames
                .Select(name => (ExpressionSyntax)TypeOfExpression(ParseTypeName(name)))
                .ToArray();

            return ArrayCreationExpression(
                    ArrayType(ParseTypeName("global::System.Type"))
                        .AddRankSpecifiers(ArrayRankSpecifier()))
                .WithInitializer(
                    InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                        SeparatedList(typeofExprs)));
        }
    }
}