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
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.RequiresBodyDefinition,
                methodDeclarationSyntax.GetLocation(),
                memberSymbol.Name));
            return false;
        }

        var returnType = declarationSyntaxRewriter.Visit(methodDeclarationSyntax.ReturnType);
        descriptor.ReturnTypeName = returnType.ToString();

        // Only rewrite expression-bodied methods; block-bodied methods are already rewritten
        descriptor.ExpressionBody = isExpressionBodied
            ? (ExpressionSyntax)expressionSyntaxRewriter.Visit(bodyExpression)
            : bodyExpression;

        foreach (var additionalParameter in
                 ((ParameterListSyntax)declarationSyntaxRewriter.Visit(methodDeclarationSyntax.ParameterList)).Parameters)
        {
            descriptor.ParametersList = descriptor.ParametersList!.AddParameters(additionalParameter);
        }

        if (methodDeclarationSyntax.TypeParameterList is not null)
        {
            descriptor.TypeParameterList = SyntaxFactory.TypeParameterList();
            foreach (var additionalTypeParameter in
                     ((TypeParameterListSyntax)declarationSyntaxRewriter.Visit(methodDeclarationSyntax.TypeParameterList)).Parameters)
            {
                descriptor.TypeParameterList = descriptor.TypeParameterList.AddParameters(additionalTypeParameter);
            }
        }

        if (methodDeclarationSyntax.ConstraintClauses.Any())
        {
            descriptor.ConstraintClauses = SyntaxFactory.List(
                methodDeclarationSyntax.ConstraintClauses
                    .Select(x => (TypeParameterConstraintClauseSyntax)declarationSyntaxRewriter.Visit(x)));
        }

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
        ExpressionSyntax? innerBody = null;

        if (exprPropDecl.ExpressionBody?.Expression is { } exprBodyExpr)
        {
            // Expression-bodied property: Prop => (x) => …  OR  Prop => storedExpr
            innerBody = TryExtractLambdaBody(exprBodyExpr, semanticModel, member.SyntaxTree);
        }
        else if (exprPropDecl.AccessorList is not null)
        {
            var getter = exprPropDecl.AccessorList.Accessors
                .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));

            if (getter?.ExpressionBody?.Expression is { } getterExprBody)
            {
                // get => (x) => …  OR  get => storedExpr
                innerBody = TryExtractLambdaBody(getterExprBody, semanticModel, member.SyntaxTree);
            }
            else if (getter?.Body is not null)
            {
                // Block-bodied getter: get { return (x) => …; } or get { return storedExpr; }
                var returnStmt = getter.Body.Statements
                    .OfType<ReturnStatementSyntax>()
                    .FirstOrDefault();

                innerBody = TryExtractLambdaBody(returnStmt?.Expression, semanticModel, member.SyntaxTree);
            }
        }

        if (innerBody is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.RequiresBodyDefinition,
                exprPropDecl.GetLocation(),
                memberSymbol.Name));
            return false;
        }

        var returnType = declarationSyntaxRewriter.Visit(originalMethodDecl.ReturnType);
        descriptor.ReturnTypeName = returnType.ToString();
        descriptor.ExpressionBody = (ExpressionSyntax)expressionSyntaxRewriter.Visit(innerBody);

        foreach (var additionalParameter in
                 ((ParameterListSyntax)declarationSyntaxRewriter.Visit(originalMethodDecl.ParameterList)).Parameters)
        {
            descriptor.ParametersList = descriptor.ParametersList!.AddParameters(additionalParameter);
        }

        if (originalMethodDecl.TypeParameterList is not null)
        {
            descriptor.TypeParameterList = SyntaxFactory.TypeParameterList();
            foreach (var additionalTypeParameter in
                     ((TypeParameterListSyntax)declarationSyntaxRewriter.Visit(originalMethodDecl.TypeParameterList)).Parameters)
            {
                descriptor.TypeParameterList = descriptor.TypeParameterList.AddParameters(additionalTypeParameter);
            }
        }

        if (originalMethodDecl.ConstraintClauses.Any())
        {
            descriptor.ConstraintClauses = SyntaxFactory.List(
                originalMethodDecl.ConstraintClauses
                    .Select(x => (TypeParameterConstraintClauseSyntax)declarationSyntaxRewriter.Visit(x)));
        }

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
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.RequiresBodyDefinition,
                propertyDeclarationSyntax.GetLocation(),
                memberSymbol.Name));
            return false;
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
        foreach (var additionalParameter in
                 ((ParameterListSyntax)declarationSyntaxRewriter.Visit(constructorDeclarationSyntax.ParameterList)).Parameters)
        {
            descriptor.ParametersList = descriptor.ParametersList!.AddParameters(additionalParameter);
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
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.RequiresBodyDefinition,
                constructorDeclarationSyntax.GetLocation(),
                memberSymbol.Name));
            return false;
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
    {
        if (expression is null || depth > 5)
        {
            return null;
        }

        // Lambda literal → extract its expression body directly.
        // Block-bodied lambda (e.g. x => { return x > 5; }) yields null → falls through to EFP0006.
        if (expression is LambdaExpressionSyntax lambda)
        {
            return lambda.Body as ExpressionSyntax;
        }

        // Non-lambda: resolve the symbol and follow the reference to its source
        // declaration (field initializer or property body) to find the underlying lambda.
        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is not (IFieldSymbol or IPropertySymbol))
        {
            return null;
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
                var result = TryExtractLambdaBody(initValue, semanticModel, memberSyntaxTree, depth + 1);
                if (result is not null)
                {
                    return result;
                }
            }

            // Property: unwrap its body the same way we unwrap the outer property.
            if (declSyntax is PropertyDeclarationSyntax followedProp)
            {
                if (followedProp.ExpressionBody?.Expression is { } followedExprBody)
                {
                    var result = TryExtractLambdaBody(followedExprBody, semanticModel, memberSyntaxTree, depth + 1);
                    if (result is not null)
                    {
                        return result;
                    }
                }

                var followedGetter = followedProp.AccessorList?.Accessors
                    .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));

                if (followedGetter?.ExpressionBody?.Expression is { } getterExprBody)
                {
                    var result = TryExtractLambdaBody(getterExprBody, semanticModel, memberSyntaxTree, depth + 1);
                    if (result is not null)
                    {
                        return result;
                    }
                }

                var returnResult = TryExtractLambdaBody(
                    followedGetter?.Body?.Statements
                        .OfType<ReturnStatementSyntax>()
                        .FirstOrDefault()?.Expression,
                    semanticModel, memberSyntaxTree, depth + 1);

                if (returnResult is not null)
                {
                    return returnResult;
                }
            }
        }

        return null;
    }
}