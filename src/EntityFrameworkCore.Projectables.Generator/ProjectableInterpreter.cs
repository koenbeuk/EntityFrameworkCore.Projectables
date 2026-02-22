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

        /// <summary>
        /// Gets the nested class path for extension members, skipping the extension block itself
        /// and using the outer class as the containing type.
        /// </summary>
        static IEnumerable<string> GetNestedInClassPathForExtensionMember(ITypeSymbol extensionType)
        {
            // For extension members, the ContainingType is the extension block,
            // and its ContainingType is the outer class (e.g., EntityExtensions)
            var outerType = extensionType.ContainingType;
            
            if (outerType is not null)
            {
                return GetNestedInClassPath(outerType);
            }
            
            return [];
        }

        public static ProjectableDescriptor? GetDescriptor(Compilation compilation, MemberDeclarationSyntax member, SourceProductionContext context)
        {
            var semanticModel = compilation.GetSemanticModel(member.SyntaxTree);
            var memberSymbol = semanticModel.GetDeclaredSymbol(member);

            if (memberSymbol is null)
            {
                return null;
            }

            var projectableAttributeTypeSymbol = compilation.GetTypeByMetadataName("EntityFrameworkCore.Projectables.ProjectableAttribute");

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

            var useMemberBody = projectableAttributeClass.NamedArguments
                .Where(x => x.Key == "UseMemberBody")
                .Select(x => x.Value.Value)
                .OfType<string?>()
                .FirstOrDefault();

            var expandEnumMethods = projectableAttributeClass.NamedArguments
                .Where(x => x.Key == "ExpandEnumMethods")
                .Select(x => x.Value.Value is bool b && b)
                .FirstOrDefault();

            var allowBlockBody = projectableAttributeClass.NamedArguments
                .Where(x => x.Key == "AllowBlockBody")
                .Select(x => x.Value.Value is bool b && b)
                .FirstOrDefault();

            var memberBody = member;

            if (useMemberBody is not null)
            {
                var comparer = SymbolEqualityComparer.Default;

                memberBody = memberSymbol.ContainingType.GetMembers(useMemberBody)
                    .Where(x =>
                    {
                        if (memberSymbol is IMethodSymbol symbolMethod &&
                            x is IMethodSymbol xMethod &&
                            comparer.Equals(symbolMethod.ReturnType, xMethod.ReturnType) &&
                            symbolMethod.TypeArguments.Length == xMethod.TypeArguments.Length &&
                            !symbolMethod.TypeArguments.Zip(xMethod.TypeArguments, (a, b) => !comparer.Equals(a, b)).Any())
                        {
                            return true;
                        }
                        else if (memberSymbol is IPropertySymbol symbolProperty &&
                            x is IPropertySymbol xProperty &&
                            comparer.Equals(symbolProperty.Type, xProperty.Type))
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    })
                    .SelectMany(x => x.DeclaringSyntaxReferences)
                    .Select(x => x.GetSyntax())
                    .OfType<MemberDeclarationSyntax>()
                    .FirstOrDefault(x =>
                    {
                        if (x == null ||
                            x.SyntaxTree != member.SyntaxTree ||
                            x.Modifiers.Any(SyntaxKind.StaticKeyword) != member.Modifiers.Any(SyntaxKind.StaticKeyword))
                        {
                            return false;
                        }
                        else if (x is MethodDeclarationSyntax xMethod &&
                            (xMethod.ExpressionBody is not null || xMethod.Body is not null))
                        {
                            return true;
                        }
                        else if (x is PropertyDeclarationSyntax xProperty)
                        {
                            // Support expression-bodied properties: int Prop => value;
                            if (xProperty.ExpressionBody is not null)
                            {
                                return true;
                            }

                            // Support properties with explicit getters: int Prop { get => value; } or { get { return value; } }
                            if (xProperty.AccessorList is not null)
                            {
                                var getter = xProperty.AccessorList.Accessors
                                    .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                                if (getter?.ExpressionBody is not null || getter?.Body is not null)
                                {
                                    return true;
                                }
                            }
                        }
                        
                        return false;
                    });

                if (memberBody is null)
                {
                    return null;
                }
            }

            // Check if this member is inside a C# 14 extension block
            var isExtensionMember = memberSymbol.ContainingType is { IsExtension: true };
            IParameterSymbol? extensionParameter = null;
            ITypeSymbol? extensionReceiverType = null;
            
            if (isExtensionMember && memberSymbol.ContainingType is { } extensionType)
            {
                extensionParameter = extensionType.ExtensionParameter;
                extensionReceiverType = extensionParameter?.Type;
            }

            // For extension members, use the extension receiver type for rewriting
            var targetTypeForRewriting = isExtensionMember && extensionReceiverType is INamedTypeSymbol receiverNamedType
                ? receiverNamedType
                : memberSymbol.ContainingType;

            var expressionSyntaxRewriter = new ExpressionSyntaxRewriter(
                targetTypeForRewriting, 
                nullConditionalRewriteSupport, 
                expandEnumMethods,
                semanticModel, 
                context,
                extensionParameter?.Name);
            var declarationSyntaxRewriter = new DeclarationSyntaxRewriter(semanticModel);

            // For extension members, use the outer class for class naming
            var classForNaming = isExtensionMember && memberSymbol.ContainingType.ContainingType is not null
                ? memberSymbol.ContainingType.ContainingType
                : memberSymbol.ContainingType;

            var methodSymbol = memberSymbol as IMethodSymbol;

            // Sanitize constructor name (.ctor / .cctor are not valid C# identifiers, use _ctor)
            var memberName = methodSymbol?.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
                ? "_ctor"
                : memberSymbol.Name;

            var descriptor = new ProjectableDescriptor
            {
                UsingDirectives = member.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>(),                    
                ClassName = classForNaming.Name,
                ClassNamespace = classForNaming.ContainingNamespace.IsGlobalNamespace ? null : classForNaming.ContainingNamespace.ToDisplayString(),
                MemberName = memberName,
                NestedInClassNames = isExtensionMember 
                    ? GetNestedInClassPathForExtensionMember(memberSymbol.ContainingType)
                    : GetNestedInClassPath(memberSymbol.ContainingType),
                ParametersList = SyntaxFactory.ParameterList()
            };

            // Collect parameter type names for method overload disambiguation
            if (methodSymbol is not null)
            {
                var parameterTypeNames = methodSymbol.Parameters
                    .Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .ToList();
                
                // For extension members, prepend the extension receiver type to match how the runtime sees the method.
                // At runtime, extension member methods have the receiver as the first parameter, but Roslyn's
                // methodSymbol.Parameters doesn't include it.
                if (isExtensionMember && extensionReceiverType is not null)
                {
                    parameterTypeNames.Insert(0, extensionReceiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
                
                descriptor.ParameterTypeNames = parameterTypeNames;
            }

            if (classForNaming is { IsGenericType: true })
            {
                descriptor.ClassTypeParameterList = SyntaxFactory.TypeParameterList();

                foreach (var additionalClassTypeParameter in classForNaming.TypeParameters)
                {
                    descriptor.ClassTypeParameterList = descriptor.ClassTypeParameterList.AddParameters(
                        SyntaxFactory.TypeParameter(additionalClassTypeParameter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    );
                    
                    // See https://github.com/dotnet/roslyn/blob/d7e010bbe5b1d37837417fc5e79ecb2fd9b7b487/src/VisualStudio/CSharp/Impl/ObjectBrowser/DescriptionBuilder.cs#L340
                    if (!additionalClassTypeParameter.ConstraintTypes.IsDefaultOrEmpty)
                    {
                        descriptor.ClassConstraintClauses ??= SyntaxFactory.List<TypeParameterConstraintClauseSyntax>();

                        var parameters = new List<TypeConstraintSyntax>();

                        if (additionalClassTypeParameter.HasReferenceTypeConstraint)
                        {
                            parameters.Add(MakeTypeConstraint("class"));
                        }

                        if (additionalClassTypeParameter.HasValueTypeConstraint)
                        {
                            parameters.Add(MakeTypeConstraint("struct"));
                        }

                        if (additionalClassTypeParameter.HasNotNullConstraint)
                        {
                            parameters.Add(MakeTypeConstraint("notnull"));
                        }
                        
                        parameters.AddRange(additionalClassTypeParameter
                            .ConstraintTypes
                            .Select(c => 
                                MakeTypeConstraint(c.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                            )
                        );

                        if (additionalClassTypeParameter.HasConstructorConstraint)
                        {
                            parameters.Add(MakeTypeConstraint("new()"));
                        }

                        descriptor.ClassConstraintClauses = descriptor.ClassConstraintClauses.Value.Add(
                            SyntaxFactory.TypeParameterConstraintClause(
                                SyntaxFactory.IdentifierName(additionalClassTypeParameter.Name),
                                SyntaxFactory.SeparatedList<TypeParameterConstraintSyntax>(parameters)
                            )
                        );
                    }
                }
            }

            // Handle extension members - add @this parameter with the extension receiver type
            if (isExtensionMember && extensionReceiverType is not null)
            {
                descriptor.ParametersList = descriptor.ParametersList.AddParameters(
                    SyntaxFactory.Parameter(
                        SyntaxFactory.Identifier("@this")
                    )
                    .WithType(
                        SyntaxFactory.ParseTypeName(
                            extensionReceiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        )
                    )
                );
            }
            else if (!member.Modifiers.Any(SyntaxKind.StaticKeyword) && member is not ConstructorDeclarationSyntax)
            {
                descriptor.ParametersList = descriptor.ParametersList.AddParameters(
                    SyntaxFactory.Parameter(
                        SyntaxFactory.Identifier("@this")
                    )
                    .WithType(
                        SyntaxFactory.ParseTypeName(
                            memberSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        )
                    )
                );
            }

            // Handle target type for extension members
            if (isExtensionMember && extensionReceiverType is not null)
            {
                descriptor.TargetClassNamespace = extensionReceiverType.ContainingNamespace.IsGlobalNamespace ? null : extensionReceiverType.ContainingNamespace.ToDisplayString();
                descriptor.TargetNestedInClassNames = GetNestedInClassPath(extensionReceiverType);
            }
            else if (methodSymbol is { IsExtensionMethod: true })
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

            // Projectable methods
            if (memberBody is MethodDeclarationSyntax methodDeclarationSyntax)
            {
                ExpressionSyntax? bodyExpression = null;

                if (methodDeclarationSyntax.ExpressionBody is not null)
                {
                    // Expression-bodied method (e.g., int Foo() => 1;)
                    bodyExpression = methodDeclarationSyntax.ExpressionBody.Expression;
                }
                else if (methodDeclarationSyntax.Body is not null)
                {
                    // Block-bodied method (e.g., int Foo() { return 1; })
                    
                    // Emit warning if AllowBlockBody is not set to true
                    if (!allowBlockBody)
                    {
                        var diagnostic = Diagnostic.Create(Diagnostics.BlockBodyExperimental, methodDeclarationSyntax.GetLocation(), memberSymbol.Name);
                        context.ReportDiagnostic(diagnostic);
                    }
                    
                    var blockConverter = new BlockStatementConverter(context, expressionSyntaxRewriter);
                    bodyExpression = blockConverter.TryConvertBlock(methodDeclarationSyntax.Body, memberSymbol.Name);

                    if (bodyExpression is null)
                    {
                        // Diagnostics already reported by BlockStatementConverter
                        return null;
                    }
                    
                    // The expression has already been rewritten by BlockStatementConverter, so we don't rewrite it again
                }
                else
                {
                    var diagnostic = Diagnostic.Create(Diagnostics.RequiresBodyDefinition, methodDeclarationSyntax.GetLocation(), memberSymbol.Name);
                    context.ReportDiagnostic(diagnostic);
                    return null;
                }

                var returnType = declarationSyntaxRewriter.Visit(methodDeclarationSyntax.ReturnType);

                descriptor.ReturnTypeName = returnType.ToString();
                // Only rewrite expression-bodied methods, block-bodied methods are already rewritten
                descriptor.ExpressionBody = methodDeclarationSyntax.ExpressionBody is not null 
                    ? (ExpressionSyntax)expressionSyntaxRewriter.Visit(bodyExpression)
                    : bodyExpression;
                foreach (var additionalParameter in ((ParameterListSyntax)declarationSyntaxRewriter.Visit(methodDeclarationSyntax.ParameterList)).Parameters)
                {
                    descriptor.ParametersList = descriptor.ParametersList.AddParameters(additionalParameter);
                }

                if (methodDeclarationSyntax.TypeParameterList is not null)
                {
                    descriptor.TypeParameterList = SyntaxFactory.TypeParameterList();
                    foreach (var additionalTypeParameter in ((TypeParameterListSyntax)declarationSyntaxRewriter.Visit(methodDeclarationSyntax.TypeParameterList)).Parameters)
                    {
                        descriptor.TypeParameterList = descriptor.TypeParameterList.AddParameters(additionalTypeParameter);
                    }
                }

                if (methodDeclarationSyntax.ConstraintClauses.Any())
                {
                    descriptor.ConstraintClauses = SyntaxFactory.List(
                        methodDeclarationSyntax
                            .ConstraintClauses
                            .Select(x => (TypeParameterConstraintClauseSyntax)declarationSyntaxRewriter.Visit(x))
                        );
                }
            }
            
            // Projectable properties
            else if (memberBody is PropertyDeclarationSyntax propertyDeclarationSyntax)
            {
                ExpressionSyntax? bodyExpression = null;
                var isBlockBodiedGetter = false;
    
                // Expression-bodied property: int Prop => value;
                if (propertyDeclarationSyntax.ExpressionBody is not null)
                {
                    bodyExpression = propertyDeclarationSyntax.ExpressionBody.Expression;
                }
                else if (propertyDeclarationSyntax.AccessorList is not null)
                {
                    // Property with explicit getter
                    var getter = propertyDeclarationSyntax.AccessorList.Accessors
                        .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            
                    if (getter?.ExpressionBody is not null)
                    {
                        // get => expression;
                        bodyExpression = getter.ExpressionBody.Expression;
                    }
                    else if (getter?.Body is not null)
                    {
                        // get { return expression; }
                        // Emit warning if AllowBlockBody is not set to true
                        if (!allowBlockBody)
                        {
                            var diagnostic = Diagnostic.Create(Diagnostics.BlockBodyExperimental, propertyDeclarationSyntax.GetLocation(), memberSymbol.Name);
                            context.ReportDiagnostic(diagnostic);
                        }
                        
                        var blockConverter = new BlockStatementConverter(context, expressionSyntaxRewriter);
                        bodyExpression = blockConverter.TryConvertBlock(getter.Body, memberSymbol.Name);
                        isBlockBodiedGetter = true;
                        
                        if (bodyExpression is null)
                        {
                            // Diagnostics already reported by BlockStatementConverter
                            return null;
                        }
                        
                        // The expression has already been rewritten by BlockStatementConverter, so we don't rewrite it again
                    }
                }
    
                if (bodyExpression is null)
                {
                    var diagnostic = Diagnostic.Create(Diagnostics.RequiresBodyDefinition, propertyDeclarationSyntax.GetLocation(), memberSymbol.Name);
                    context.ReportDiagnostic(diagnostic);
                    return null;
                }

                var returnType = declarationSyntaxRewriter.Visit(propertyDeclarationSyntax.Type);

                descriptor.ReturnTypeName = returnType.ToString();
                
                // Only rewrite expression-bodied properties, block-bodied getters are already rewritten
                descriptor.ExpressionBody = isBlockBodiedGetter 
                    ? bodyExpression
                    : (ExpressionSyntax)expressionSyntaxRewriter.Visit(bodyExpression);
            }
            // Projectable constructors
            else if (memberBody is ConstructorDeclarationSyntax constructorDeclarationSyntax)
            {
                var containingType = memberSymbol.ContainingType;
                var fullTypeName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                descriptor.ReturnTypeName = fullTypeName;

                // Add the constructor's own parameters to the lambda parameter list
                foreach (var additionalParameter in ((ParameterListSyntax)declarationSyntaxRewriter.Visit(constructorDeclarationSyntax.ParameterList)).Parameters)
                {
                    descriptor.ParametersList = descriptor.ParametersList.AddParameters(additionalParameter);
                }

                // Accumulated property-name → expression map (later converted to member-init)
                var accumulatedAssignments = new Dictionary<string, ExpressionSyntax>();

                // 1. Process base/this initializer: propagate property assignments from the
                //    delegated constructor so callers don't have to duplicate them in the body.
                if (constructorDeclarationSyntax.Initializer is { } initializer)
                {
                    var initializerSymbol = semanticModel.GetSymbolInfo(initializer).Symbol as IMethodSymbol;
                    if (initializerSymbol is not null)
                    {
                        var delegatedAssignments = CollectDelegatedConstructorAssignments(
                            initializerSymbol,
                            initializer.ArgumentList.Arguments,
                            expressionSyntaxRewriter,
                            context,
                            memberSymbol.Name);

                        if (delegatedAssignments is null)
                        {
                            return null;
                        }

                        foreach (var kvp in delegatedAssignments)
                        {
                            accumulatedAssignments[kvp.Key] = kvp.Value;
                        }
                    }
                }

                // 2. Process this constructor's body (supports assignments, locals, if/else).
                // Pass the already-accumulated base/this initializer assignments as the initial
                // visible context so that references to those properties are correctly inlined.
                if (constructorDeclarationSyntax.Body is { } body)
                {
                    var bodyConverter = new ConstructorBodyConverter(context, expressionSyntaxRewriter);
                    IReadOnlyDictionary<string, ExpressionSyntax>? initialCtx =
                        accumulatedAssignments.Count > 0 ? accumulatedAssignments : null;
                    var bodyAssignments = bodyConverter.TryConvertBody(body.Statements, memberSymbol.Name, initialCtx);

                    if (bodyAssignments is null)
                    {
                        return null;
                    }

                    // Body assignments override anything set by the base/this initializer
                    foreach (var kvp in bodyAssignments)
                    {
                        accumulatedAssignments[kvp.Key] = kvp.Value;
                    }
                }

                if (accumulatedAssignments.Count == 0)
                {
                    var diag = Diagnostic.Create(Diagnostics.RequiresBodyDefinition,
                        constructorDeclarationSyntax.GetLocation(), memberSymbol.Name);
                    context.ReportDiagnostic(diag);
                    return null;
                }

                // Verify the containing type has a parameterless (instance) constructor.
                // The generated projection is: new T() { Prop = ... }, which requires one.
                // INamedTypeSymbol.Constructors covers all partial declarations and also
                // the implicit parameterless constructor that the compiler synthesizes when
                // no constructors are explicitly defined.
                var hasParameterlessConstructor = containingType.Constructors
                    .Any(c => !c.IsStatic && c.Parameters.IsEmpty);

                if (!hasParameterlessConstructor)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.MissingParameterlessConstructor,
                        constructorDeclarationSyntax.GetLocation(),
                        containingType.Name));
                    return null;
                }

                var initExpressions = accumulatedAssignments
                    .Select(kvp => (ExpressionSyntax)SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName(kvp.Key),
                        kvp.Value))
                    .ToList();

                var memberInit = SyntaxFactory.InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SyntaxFactory.SeparatedList(initExpressions));

                // Use a parameterless constructor + object initializer so EF Core only
                // projects columns explicitly listed in the member-init bindings.
                descriptor.ExpressionBody = SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                    SyntaxFactory.ParseTypeName(fullTypeName),
                    SyntaxFactory.ArgumentList(),
                    memberInit
                );
            }
            else
            {
                return null;
            }

            return descriptor;
        }

        /// <summary>
        /// Collects the property-assignment expressions that the delegated constructor (base/this)
        /// would perform, substituting its parameters with the actual call-site argument expressions.
        /// Supports if/else logic inside the delegated constructor body, and follows the chain of
        /// base/this initializers recursively.
        /// Returns <c>null</c> when an unsupported statement is encountered (diagnostics reported).
        /// </summary>
        private static Dictionary<string, ExpressionSyntax>? CollectDelegatedConstructorAssignments(
            IMethodSymbol delegatedCtor,
            SeparatedSyntaxList<ArgumentSyntax> callerArgs,
            ExpressionSyntaxRewriter expressionSyntaxRewriter,
            SourceProductionContext context,
            string memberName,
            bool argsAlreadyRewritten = false)
        {
            // Only process constructors whose source is available in this compilation
            var syntax = delegatedCtor.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .OfType<ConstructorDeclarationSyntax>()
                .FirstOrDefault();

            if (syntax is null)
            {
                return new Dictionary<string, ExpressionSyntax>();
            }

            // Build a mapping: delegated-param-name → caller argument expression.
            // First-level args come from the original syntax tree and must be visited by the
            // ExpressionSyntaxRewriter. Recursive-level args are already-substituted detached
            // nodes and must NOT be visited (doing so throws "node not in syntax tree").
            var paramToArg = new Dictionary<string, ExpressionSyntax>();
            for (var i = 0; i < callerArgs.Count && i < delegatedCtor.Parameters.Length; i++)
            {
                var paramName = delegatedCtor.Parameters[i].Name;
                var argExpr = argsAlreadyRewritten
                    ? callerArgs[i].Expression
                    : (ExpressionSyntax)expressionSyntaxRewriter.Visit(callerArgs[i].Expression);
                paramToArg[paramName] = argExpr;
            }

            // The accumulated assignments start from the delegated ctor's own initializer (if any),
            // so that base/this chains are followed recursively.
            var accumulated = new Dictionary<string, ExpressionSyntax>();

            if (syntax.Initializer is { } delegatedInitializer)
            {
                // The delegated ctor's initializer is part of the original syntax tree,
                // so we can safely use the semantic model to resolve its symbol.
                var semanticModel = expressionSyntaxRewriter.GetSemanticModel();
                var delegatedInitializerSymbol =
                    semanticModel.GetSymbolInfo(delegatedInitializer).Symbol as IMethodSymbol;

                if (delegatedInitializerSymbol is not null)
                {
                    // Substitute the delegated ctor's initializer arguments using our paramToArg map,
                    // so that e.g. `: base(id)` becomes `: base(<caller's expression for id>)`.
                    var substitutedInitArgs = SubstituteArguments(
                        delegatedInitializer.ArgumentList.Arguments, paramToArg);

                    var chainedAssignments = CollectDelegatedConstructorAssignments(
                        delegatedInitializerSymbol,
                        substitutedInitArgs,
                        expressionSyntaxRewriter,
                        context,
                        memberName,
                        argsAlreadyRewritten: true); // args are now detached substituted nodes

                    if (chainedAssignments is null)
                        return null;

                    foreach (var kvp in chainedAssignments)
                        accumulated[kvp.Key] = kvp.Value;
                }
            }

            if (syntax.Body is null)
                return accumulated;

            // Use ConstructorBodyConverter (identity rewriter + param substitutions) so that
            // if/else, local variables and simple assignments in the delegated ctor are all handled.
            // Pass the already-accumulated chained assignments as the initial visible context.
            IReadOnlyDictionary<string, ExpressionSyntax>? initialCtx =
                accumulated.Count > 0 ? accumulated : null;
            var converter = new ConstructorBodyConverter(context, paramToArg);
            var bodyAssignments = converter.TryConvertBody(syntax.Body.Statements, memberName, initialCtx);

            if (bodyAssignments is null)
                return null;

            foreach (var kvp in bodyAssignments)
                accumulated[kvp.Key] = kvp.Value;

            return accumulated;
        }

        /// <summary>
        /// Substitutes identifiers in <paramref name="args"/> using the <paramref name="paramToArg"/>
        /// mapping. This is used to forward the outer caller's arguments through a chain of
        /// base/this initializer calls.
        /// </summary>
        private static SeparatedSyntaxList<ArgumentSyntax> SubstituteArguments(
            SeparatedSyntaxList<ArgumentSyntax> args,
            Dictionary<string, ExpressionSyntax> paramToArg)
        {
            if (paramToArg.Count == 0)
                return args;

            var result = new List<ArgumentSyntax>();
            foreach (var arg in args)
            {
                var substituted = ConstructorBodyConverter.ParameterSubstitutor.Substitute(
                    arg.Expression, paramToArg);
                result.Add(arg.WithExpression(substituted));
            }
            return SyntaxFactory.SeparatedList(result);
        }

        private static TypeConstraintSyntax MakeTypeConstraint(string constraint) => SyntaxFactory.TypeConstraint(SyntaxFactory.IdentifierName(constraint));
    }
}
