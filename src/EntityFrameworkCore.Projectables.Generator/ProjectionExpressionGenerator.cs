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
        /// </summary>
        static void EmitRegistry(ImmutableArray<ProjectableRegistryEntry?> entries, SourceProductionContext context)
        {
            var validEntries = entries
                .Where(e => e is not null)
                .Select(e => e!)
                .ToList();

            if (validEntries.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable disable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq.Expressions;");
            sb.AppendLine("using System.Reflection;");
            sb.AppendLine();
            sb.AppendLine("namespace EntityFrameworkCore.Projectables.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
            sb.AppendLine("    internal static class ProjectionRegistry");
            sb.AppendLine("    {");
            sb.AppendLine("        // Keyed by RuntimeMethodHandle.Value (a stable nint pointer for the method/getter/ctor).");
            sb.AppendLine("        // Populated once at type initialization; shared across the entire AppDomain lifetime.");
            sb.AppendLine("        private static readonly Dictionary<nint, LambdaExpression> _map = Build();");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Returns the pre-built LambdaExpression for the given [Projectable] member,");
            sb.AppendLine("        /// or null if the member is not registered (e.g. open-generic members).");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static LambdaExpression TryGet(MemberInfo member)");
            sb.AppendLine("        {");
            sb.AppendLine("            var handle = GetHandle(member);");
            sb.AppendLine("            return handle.HasValue && _map.TryGetValue(handle.Value, out var expr) ? expr : null;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static nint? GetHandle(MemberInfo member) => member switch");
            sb.AppendLine("        {");
            sb.AppendLine("            MethodInfo m      => m.MethodHandle.Value,");
            sb.AppendLine("            PropertyInfo p    => p.GetMethod?.MethodHandle.Value,");
            sb.AppendLine("            ConstructorInfo c => c.MethodHandle.Value,");
            sb.AppendLine("            _                 => null");
            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        private static Dictionary<nint, LambdaExpression> Build()");
            sb.AppendLine("        {");
            sb.AppendLine("            var map = new Dictionary<nint, LambdaExpression>();");

            foreach (var entry in validEntries)
            {
                EmitRegistryEntry(sb, entry);
            }

            sb.AppendLine("            return map;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            context.AddSource("ProjectionRegistry.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        static void EmitRegistryEntry(StringBuilder sb, ProjectableRegistryEntry entry)
        {
            if (entry.IsGenericClass)
            {
                sb.AppendLine($"            // TODO: generic class — {entry.GeneratedClassFullName} (falls back to reflection)");
                return;
            }

            if (entry.IsGenericMethod)
            {
                sb.AppendLine($"            // TODO: generic method — {entry.GeneratedClassFullName} (falls back to reflection)");
                return;
            }

            const string flags = "global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Static";

            sb.AppendLine("            {");
            sb.AppendLine($"                var t = typeof({entry.DeclaringTypeFullName});");

            switch (entry.MemberKind)
            {
                case "Property":
                    sb.AppendLine($"                var m = t.GetProperty(\"{entry.MemberLookupName}\", {flags})?.GetMethod;");
                    break;

                case "Method":
                {
                    var typeArray = BuildTypeArray(entry.ParameterTypeNames);
                    sb.AppendLine($"                var m = t.GetMethod(\"{entry.MemberLookupName}\", {flags}, null, {typeArray}, null);");
                    break;
                }

                case "Constructor":
                {
                    var typeArray = BuildTypeArray(entry.ParameterTypeNames);
                    sb.AppendLine($"                var m = t.GetConstructor({flags}, null, {typeArray}, null);");
                    break;
                }

                default:
                    sb.AppendLine("            }");
                    return;
            }

            sb.AppendLine("                if (m is not null)");
            sb.AppendLine("                {");
            sb.AppendLine($"                    var exprType = t.Assembly.GetType(\"{entry.GeneratedClassFullName}\");");
            sb.AppendLine("                    var exprMethod = exprType?.GetMethod(\"Expression\", global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.NonPublic);");
            sb.AppendLine("                    if (exprMethod is not null)");
            sb.AppendLine("                        map[m.MethodHandle.Value] = (global::System.Linq.Expressions.LambdaExpression)exprMethod.Invoke(null, null)!;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
        }

        static string BuildTypeArray(ImmutableArray<string> parameterTypeNames)
        {
            if (parameterTypeNames.IsEmpty)
                return "global::System.Type.EmptyTypes";

            var sb = new StringBuilder("new global::System.Type[] { ");
            for (int i = 0; i < parameterTypeNames.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"typeof({parameterTypeNames[i]})");
            }
            sb.Append(" }");
            return sb.ToString();
        }

    }
}
