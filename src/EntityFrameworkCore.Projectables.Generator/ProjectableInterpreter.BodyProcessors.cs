using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator;

public static partial class ProjectableInterpreter
{
    /// <summary>
    /// Fills <paramref name="descriptor"/> from a method declaration body.
    /// Returns <c>false</c> and reports diagnostics on failure.
    /// </summary>
    private static bool TryApplyMethodBody(
        MethodDeclarationSyntax methodDeclarationSyntax,
        bool allowBlockBody,
        ISymbol memberSymbol,
        ExpressionSyntaxRewriter expressionSyntaxRewriter,
        DeclarationSyntaxRewriter declarationSyntaxRewriter,
        SourceProductionContext context,
        ProjectableDescriptor descriptor)
    {
        ExpressionSyntax? bodyExpression = null;
        var isExpressionBodied = false;

        if (methodDeclarationSyntax.ExpressionBody is not null)
        {
            bodyExpression = methodDeclarationSyntax.ExpressionBody.Expression;
            isExpressionBodied = true;
        }
        else if (methodDeclarationSyntax.Body is not null)
        {
            if (!allowBlockBody)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.BlockBodyExperimental,
                    methodDeclarationSyntax.GetLocation(),
                    memberSymbol.Name));
            }

            var blockConverter = new BlockStatementConverter(context, expressionSyntaxRewriter);
            bodyExpression = blockConverter.TryConvertBlock(methodDeclarationSyntax.Body, memberSymbol.Name);

            if (bodyExpression is null)
            {
                return false; // diagnostics already reported by BlockStatementConverter
            }
        }
        else
        {
            return ReportRequiresBodyAndFail(context, methodDeclarationSyntax, memberSymbol.Name);
        }

        var returnType = declarationSyntaxRewriter.Visit(methodDeclarationSyntax.ReturnType);
        descriptor.ReturnTypeName = returnType.ToString();

        // Only rewrite expression-bodied methods; block-bodied methods are already rewritten
        descriptor.ExpressionBody = isExpressionBodied
            ? (ExpressionSyntax)expressionSyntaxRewriter.Visit(bodyExpression)
            : bodyExpression;

        ApplyParameterList(methodDeclarationSyntax.ParameterList, declarationSyntaxRewriter, descriptor);
        ApplyTypeParameters(methodDeclarationSyntax, declarationSyntaxRewriter, descriptor);

        return true;
    }

    /// <summary>
    /// Fills <paramref name="descriptor"/> for a projectable method whose body is
    /// delegated to an <c>Expression&lt;TDelegate&gt;</c> property (specified via
    /// <c>UseMemberBody</c>). Unwraps the inner lambda and uses the method's own
    /// return type and parameter list.
    /// Returns <c>false</c> and reports diagnostics on failure.
    /// </summary>
    private static bool TryApplyExpressionPropertyBody(
        MethodDeclarationSyntax originalMethodDecl,
        PropertyDeclarationSyntax exprPropDecl,
        SemanticModel semanticModel,
        MemberDeclarationSyntax member,
        ISymbol memberSymbol,
        ExpressionSyntaxRewriter expressionSyntaxRewriter,
        DeclarationSyntaxRewriter declarationSyntaxRewriter,
        SourceProductionContext context,
        ProjectableDescriptor descriptor)
    {
        var rawExpr = TryGetPropertyGetterExpression(exprPropDecl);
        var innerBody = rawExpr is not null
            ? TryExtractLambdaBody(rawExpr, semanticModel, member.SyntaxTree)
            : null;

        if (innerBody is null)
        {
            return ReportRequiresBodyAndFail(context, exprPropDecl, memberSymbol.Name);
        }

        var returnType = declarationSyntaxRewriter.Visit(originalMethodDecl.ReturnType);
        descriptor.ReturnTypeName = returnType.ToString();
        descriptor.ExpressionBody = (ExpressionSyntax)expressionSyntaxRewriter.Visit(innerBody);

        ApplyParameterList(originalMethodDecl.ParameterList, declarationSyntaxRewriter, descriptor);
        ApplyTypeParameters(originalMethodDecl, declarationSyntaxRewriter, descriptor);

        return true;
    }

    /// <summary>
    /// Fills <paramref name="descriptor"/> for a projectable property whose body is
    /// delegated to an <c>Expression&lt;TDelegate&gt;</c> property (specified via
    /// <c>UseMemberBody</c>). Unwraps the inner lambda and uses the projectable
    /// property's own return type. The implicit <c>@this</c> parameter is already
    /// added by <see cref="BuildBaseDescriptor"/>.
    /// Returns <c>false</c> and reports diagnostics on failure.
    /// </summary>
    private static bool TryApplyExpressionPropertyBodyForProperty(
        PropertyDeclarationSyntax originalPropertyDecl,
        PropertyDeclarationSyntax exprPropDecl,
        SemanticModel semanticModel,
        MemberDeclarationSyntax member,
        ISymbol memberSymbol,
        ExpressionSyntaxRewriter expressionSyntaxRewriter,
        DeclarationSyntaxRewriter declarationSyntaxRewriter,
        SourceProductionContext context,
        ProjectableDescriptor descriptor)
    {
        var rawExpr = TryGetPropertyGetterExpression(exprPropDecl);
        var (innerBody, firstParamName) = rawExpr is not null
            ? TryExtractLambdaBodyAndFirstParam(rawExpr, semanticModel, member.SyntaxTree)
            : (null, null);

        if (innerBody is null)
        {
            return ReportRequiresBodyAndFail(context, exprPropDecl, memberSymbol.Name);
        }

        // The generated lambda always uses @this as the receiver parameter name.
        // If the expression property used a different name (e.g. `x => x.Id`), rename it.
        // NOTE: renaming must happen AFTER expressionSyntaxRewriter.Visit because the rewriter
        // uses the semantic model which requires the original (pre-rename) syntax nodes.
        var visitedBody = (ExpressionSyntax)expressionSyntaxRewriter.Visit(innerBody);
        if (firstParamName is not null && firstParamName != "@this")
        {
            visitedBody = (ExpressionSyntax)new VariableReplacementRewriter(
                firstParamName,
                SyntaxFactory.IdentifierName("@this")).Visit(visitedBody);
        }

        var returnType = declarationSyntaxRewriter.Visit(originalPropertyDecl.Type);
        descriptor.ReturnTypeName = returnType.ToString();
        descriptor.ExpressionBody = visitedBody;

        return true;
    }

    /// <summary>
    /// Fills <paramref name="descriptor"/> from a property declaration body.
    /// Returns <c>false</c> and reports diagnostics on failure.
    /// </summary>
    private static bool TryApplyPropertyBody(
        PropertyDeclarationSyntax propertyDeclarationSyntax,
        bool allowBlockBody,
        ISymbol memberSymbol,
        ExpressionSyntaxRewriter expressionSyntaxRewriter,
        DeclarationSyntaxRewriter declarationSyntaxRewriter,
        SourceProductionContext context,
        ProjectableDescriptor descriptor)
    {
        ExpressionSyntax? bodyExpression = null;
        var isBlockBodiedGetter = false;

        if (propertyDeclarationSyntax.ExpressionBody is not null)
        {
            // Expression-bodied property: int Prop => value;
            bodyExpression = propertyDeclarationSyntax.ExpressionBody.Expression;
        }
        else if (propertyDeclarationSyntax.AccessorList is not null)
        {
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
                if (!allowBlockBody)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.BlockBodyExperimental,
                        propertyDeclarationSyntax.GetLocation(),
                        memberSymbol.Name));
                }

                var blockConverter = new BlockStatementConverter(context, expressionSyntaxRewriter);
                bodyExpression = blockConverter.TryConvertBlock(getter.Body, memberSymbol.Name);
                isBlockBodiedGetter = true;

                if (bodyExpression is null)
                {
                    return false; // diagnostics already reported by BlockStatementConverter
                }
            }
        }

        if (bodyExpression is null)
        {
            return ReportRequiresBodyAndFail(context, propertyDeclarationSyntax, memberSymbol.Name);
        }

        var returnType = declarationSyntaxRewriter.Visit(propertyDeclarationSyntax.Type);
        descriptor.ReturnTypeName = returnType.ToString();

        // Only rewrite expression-bodied properties; block-bodied getters are already rewritten
        descriptor.ExpressionBody = isBlockBodiedGetter
            ? bodyExpression
            : (ExpressionSyntax)expressionSyntaxRewriter.Visit(bodyExpression);

        return true;
    }

    /// <summary>
    /// Fills <paramref name="descriptor"/> from a constructor declaration body.
    /// Returns <c>false</c> and reports diagnostics on failure.
    /// </summary>
    private static bool TryApplyConstructorBody(
        ConstructorDeclarationSyntax constructorDeclarationSyntax,
        SemanticModel semanticModel,
        ISymbol memberSymbol,
        ExpressionSyntaxRewriter expressionSyntaxRewriter,
        DeclarationSyntaxRewriter declarationSyntaxRewriter,
        SourceProductionContext context,
        Compilation? compilation,
        ProjectableDescriptor descriptor)
    {
        // Constructor delegation requires a Compilation to get semantic models
        // for other syntax trees (base/this ctor may be in a different file).
        if (compilation is null)
        {
            return false; // Should not happen in practice: the pipeline passes compilation for constructors.
        }

        var containingType = memberSymbol.ContainingType;
        var fullTypeName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        descriptor.ReturnTypeName = fullTypeName;

        // Add the constructor's own parameters to the lambda parameter list
        ApplyParameterList(constructorDeclarationSyntax.ParameterList, declarationSyntaxRewriter, descriptor);

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
                    memberSymbol.Name,
                    compilation);

                if (delegatedAssignments is null)
                {
                    return false;
                }

                foreach (var kvp in delegatedAssignments)
                {
                    accumulatedAssignments[kvp.Key] = kvp.Value;
                }
            }
        }

        // 2. Process this constructor's body (supports assignments, locals, if/else).
        if (constructorDeclarationSyntax.Body is { } body)
        {
            var bodyConverter = new ConstructorBodyConverter(context, expressionSyntaxRewriter);
            IReadOnlyDictionary<string, ExpressionSyntax>? initialCtx =
                accumulatedAssignments.Count > 0 ? accumulatedAssignments : null;
            var bodyAssignments = bodyConverter.TryConvertBody(body.Statements, memberSymbol.Name, initialCtx);

            if (bodyAssignments is null)
            {
                return false;
            }

            // Body assignments override anything set by the base/this initializer
            foreach (var kvp in bodyAssignments)
            {
                accumulatedAssignments[kvp.Key] = kvp.Value;
            }
        }

        if (accumulatedAssignments.Count == 0)
        {
            return ReportRequiresBodyAndFail(context, constructorDeclarationSyntax, memberSymbol.Name);
        }

        // Verify the containing type has an accessible parameterless (instance) constructor.
        // The generated projection is: new T() { Prop = ... }, which requires one.
        var hasAccessibleParameterlessConstructor = containingType.Constructors
            .Any(c => !c.IsStatic
                      && c.Parameters.IsEmpty
                      && c.DeclaredAccessibility is Accessibility.Public
                          or Accessibility.Internal
                          or Accessibility.ProtectedOrInternal);

        if (!hasAccessibleParameterlessConstructor)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.MissingParameterlessConstructor,
                constructorDeclarationSyntax.GetLocation(),
                containingType.Name));
            return false;
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
            memberInit);

        return true;
    }

    /// <summary>
    /// Tries to extract the inner <see cref="ExpressionSyntax"/> of a lambda from
    /// <paramref name="expression"/>. If the expression is a lambda, returns its expression body.
    /// Otherwise, follows field/property symbol references (within the same syntax tree) up to
    /// <paramref name="depth"/> levels deep to locate a lambda.
    /// Returns <c>null</c> when no lambda body can be found.
    /// </summary>
    private static ExpressionSyntax? TryExtractLambdaBody(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        SyntaxTree memberSyntaxTree,
        int depth = 0)
        => TryExtractLambdaBodyAndFirstParam(expression, semanticModel, memberSyntaxTree, depth).body;

    /// <summary>
    /// Like <see cref="TryExtractLambdaBody"/>, but also returns the first lambda parameter name
    /// so callers can rename it (e.g. to <c>@this</c>) in the extracted body.
    /// </summary>
    private static (ExpressionSyntax? body, string? firstParamName) TryExtractLambdaBodyAndFirstParam(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        SyntaxTree memberSyntaxTree,
        int depth = 0)
    {
        if (expression is null || depth > 5)
        {
            return (null, null);
        }

        // Lambda literal → extract its expression body and first parameter name directly.
        // Block-bodied lambda yields null body → falls through to EFP0006.
        if (expression is SimpleLambdaExpressionSyntax simpleLambda)
        {
            return (simpleLambda.Body as ExpressionSyntax, simpleLambda.Parameter.Identifier.Text);
        }

        if (expression is ParenthesizedLambdaExpressionSyntax parenLambda)
        {
            var firstName = parenLambda.ParameterList.Parameters.Count > 0
                ? parenLambda.ParameterList.Parameters[0].Identifier.Text
                : null;
            return (parenLambda.Body as ExpressionSyntax, firstName);
        }

        // Non-lambda: resolve the symbol and follow the reference to its source
        // declaration (field initializer or property body) to find the underlying lambda.
        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is not (IFieldSymbol or IPropertySymbol))
        {
            return (null, null);
        }

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            // Only follow same-file declarations; the current semantic model is
            // bound to member.SyntaxTree and cannot analyse nodes in other trees.
            if (syntaxRef.SyntaxTree != memberSyntaxTree)
            {
                continue;
            }

            var declSyntax = syntaxRef.GetSyntax();

            // Field: private static readonly Expression<Func<…>> _f = @this => …;
            if (declSyntax is VariableDeclaratorSyntax { Initializer.Value: var initValue })
            {
                var result = TryExtractLambdaBodyAndFirstParam(initValue, semanticModel, memberSyntaxTree, depth + 1);
                if (result.body is not null)
                {
                    return result;
                }
            }

            // Property: unwrap its body the same way we unwrap the outer property.
            if (declSyntax is PropertyDeclarationSyntax followedProp)
            {
                var result = TryExtractLambdaBodyAndFirstParam(
                    TryGetPropertyGetterExpression(followedProp), semanticModel, memberSyntaxTree, depth + 1);
                if (result.body is not null)
                {
                    return result;
                }
            }
        }


        return (null, null);
    }
}