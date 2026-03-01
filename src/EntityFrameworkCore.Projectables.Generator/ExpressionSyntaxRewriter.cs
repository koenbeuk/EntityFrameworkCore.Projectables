using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace EntityFrameworkCore.Projectables.Generator
{

    public class ExpressionSyntaxRewriter : CSharpSyntaxRewriter
    {
        readonly INamedTypeSymbol _targetTypeSymbol;
        readonly SemanticModel _semanticModel;
        readonly NullConditionalRewriteSupport _nullConditionalRewriteSupport;
        readonly bool _expandEnumMethods;
        readonly SourceProductionContext _context;
        readonly Stack<ExpressionSyntax> _conditionalAccessExpressionsStack = new();
        readonly string? _extensionParameterName;

        public ExpressionSyntaxRewriter(INamedTypeSymbol targetTypeSymbol, NullConditionalRewriteSupport nullConditionalRewriteSupport, bool expandEnumMethods, SemanticModel semanticModel, SourceProductionContext context, string? extensionParameterName = null)
        {
            _targetTypeSymbol = targetTypeSymbol;
            _nullConditionalRewriteSupport = nullConditionalRewriteSupport;
            _expandEnumMethods = expandEnumMethods;
            _semanticModel = semanticModel;
            _context = context;
            _extensionParameterName = extensionParameterName;
        }

        public SemanticModel GetSemanticModel() => _semanticModel;

        private SyntaxNode? VisitThisBaseExpression(CSharpSyntaxNode node)
        {
            // Swap out the use of this and base to @this and keep leading and trailing trivias
            return SyntaxFactory.IdentifierName("@this")
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());
        }
        
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var expressionSyntax = (ExpressionSyntax?)Visit(node.Expression) ?? throw new ArgumentNullException("expression");
        
            var syntaxNode = Visit(node.Name);
        
            // Prevents invalid cast when visiting a QualifiedNameSyntax
            if (syntaxNode is QualifiedNameSyntax qst)
            {
                syntaxNode = qst.Right;
            }
            
            return node.Update(expressionSyntax, VisitToken(node.OperatorToken), (SimpleNameSyntax)syntaxNode);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            // Fully qualify extension method calls
            if (node.Expression is not MemberAccessExpressionSyntax memberAccessExpressionSyntax)
            {
                return base.VisitInvocationExpression(node);
            }

            var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is not IMethodSymbol methodSymbol)
            {
                return base.VisitInvocationExpression(node);
            }

            // Check if we should expand enum methods
            if (_expandEnumMethods && TryExpandEnumMethodCall(node, memberAccessExpressionSyntax, methodSymbol, out var expandedExpression))
            {
                return expandedExpression;
            }

            // Fully qualify extension method calls
            if (methodSymbol.IsExtensionMethod)
            {
                return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseName(methodSymbol.ContainingType.ToDisplayString(NullableFlowState.None, SymbolDisplayFormat.FullyQualifiedFormat)),
                        memberAccessExpressionSyntax.Name
                    ),
                    node.ArgumentList.WithArguments(
                        ((ArgumentListSyntax)VisitArgumentList(node.ArgumentList)!).Arguments.Insert(0, SyntaxFactory.Argument(
                                (ExpressionSyntax)Visit(memberAccessExpressionSyntax.Expression)
                            )
                        )
                    )
                );
            }

            return base.VisitInvocationExpression(node);
        }

        private bool TryExpandEnumMethodCall(InvocationExpressionSyntax node, MemberAccessExpressionSyntax memberAccess, IMethodSymbol methodSymbol, out ExpressionSyntax? expandedExpression)
        {
            expandedExpression = null;

            // Get the receiver expression (the enum instance or variable)
            var receiverExpression = memberAccess.Expression;
            var receiverTypeInfo = _semanticModel.GetTypeInfo(receiverExpression);
            var receiverType = receiverTypeInfo.Type;

            // Handle nullable enum types
            ITypeSymbol enumType;
            var isNullable = false;
            if (receiverType is INamedTypeSymbol { IsGenericType: true, Name: "Nullable" } nullableType && 
                nullableType.TypeArguments.Length == 1 && 
                nullableType.TypeArguments[0].TypeKind == TypeKind.Enum)
            {
                enumType = nullableType.TypeArguments[0];
                isNullable = true;
            }
            else if (receiverType?.TypeKind == TypeKind.Enum)
            {
                enumType = receiverType;
            }
            else
            {
                // Not an enum type
                return false;
            }

            // Get all enum members
            var enumMembers = enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => f.HasConstantValue)
                .ToList();

            if (enumMembers.Count == 0)
            {
                return false;
            }

            // Visit the receiver expression to transform it (e.g., @this.MyProperty)
            var visitedReceiver = (ExpressionSyntax)Visit(receiverExpression);

            // Get the original method (in case of reduced extension method)
            var originalMethod = methodSymbol.ReducedFrom ?? methodSymbol;
            
            // Get the return type of the method to determine the default value
            var returnType = methodSymbol.ReturnType;
            
            // Build a chain of ternary expressions for each enum value
            // Start with default(T) as the fallback for non-nullable types, or null for nullable/reference types
            ExpressionSyntax defaultExpression;
            if (returnType.IsReferenceType || returnType.NullableAnnotation == NullableAnnotation.Annotated || 
                returnType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            {
                defaultExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
            }
            else
            {
                // Use default(T) for value types
                defaultExpression = SyntaxFactory.DefaultExpression(
                    SyntaxFactory.ParseTypeName(returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }

            var currentExpression = defaultExpression;
            
            // Create the enum value access: EnumType.Value
            var enumAccessValues = enumMembers
                .AsEnumerable()
                .Reverse()
                .Select(m =>
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseTypeName(enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                        SyntaxFactory.IdentifierName(m.Name)
                    )
                );

            // Build the ternary chain, calling the method on each enum value
            foreach (var enumValueAccess in enumAccessValues)
            {
                // Create the method call on the enum value: ExtensionClass.Method(EnumType.Value)
                var methodCall = CreateMethodCallOnEnumValue(originalMethod, enumValueAccess, node.ArgumentList);

                // Create condition: receiver == EnumType.Value
                var condition = SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    visitedReceiver,
                    enumValueAccess
                );

                // Create conditional expression: condition ? methodCall : previousExpression
                currentExpression = SyntaxFactory.ConditionalExpression(
                    condition,
                    methodCall,
                    currentExpression
                );
            }

            // If nullable, wrap in null check
            if (isNullable)
            {
                var nullCheck = SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    visitedReceiver,
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                );

                currentExpression = SyntaxFactory.ConditionalExpression(
                    nullCheck,
                    defaultExpression,
                    currentExpression
                );
            }

            expandedExpression = SyntaxFactory.ParenthesizedExpression(currentExpression);
            return true;
        }

        private ExpressionSyntax CreateMethodCallOnEnumValue(IMethodSymbol methodSymbol, ExpressionSyntax enumValueExpression, ArgumentListSyntax originalArguments)
        {
            // Get the fully qualified containing type name
            var containingTypeName = methodSymbol.ContainingType.ToDisplayString(NullableFlowState.None, SymbolDisplayFormat.FullyQualifiedFormat);
            
            // Create the method access expression: ContainingType.MethodName
            var methodAccess = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.ParseName(containingTypeName),
                SyntaxFactory.IdentifierName(methodSymbol.Name)
            );

            // Build arguments: the enum value as the first argument (for extension methods), followed by any additional arguments
            var arguments = SyntaxFactory.SeparatedList<ArgumentSyntax>();
            arguments = arguments.Add(SyntaxFactory.Argument(enumValueExpression));
            
            // Add any additional arguments from the original call
            foreach (var arg in originalArguments.Arguments)
            {
                arguments = arguments.Add((ArgumentSyntax)Visit(arg));
            }

            return SyntaxFactory.InvocationExpression(
                methodAccess,
                SyntaxFactory.ArgumentList(arguments)
            );
        }

        public override SyntaxNode? VisitInterpolation(InterpolationSyntax node)
        {
            // Visit the expression first
            var targetExpression = (ExpressionSyntax)Visit(node.Expression);
            
            // Check if the expression already has parentheses
            if (targetExpression is ParenthesizedExpressionSyntax)
            {
                return node.WithExpression(targetExpression);
            }
            
            // Create a new expression wrapped in parentheses
            var newExpression = SyntaxFactory.ParenthesizedExpression(targetExpression);
            
            return node.WithExpression(newExpression);
        }

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            var targetExpression = (ExpressionSyntax)Visit(node.Expression);

            _conditionalAccessExpressionsStack.Push(targetExpression);

            if (_nullConditionalRewriteSupport == NullConditionalRewriteSupport.None)
            {
                var diagnostic = Diagnostic.Create(Diagnostics.NullConditionalRewriteUnsupported, node.GetLocation(), node);
                _context.ReportDiagnostic(diagnostic);

                // Return the original node, do not attempt further rewrites
                return node;
            }

            else if (_nullConditionalRewriteSupport is NullConditionalRewriteSupport.Ignore)
            {
                // Ignore the conditional accesss and simply visit the WhenNotNull expression
                return Visit(node.WhenNotNull);
            }

            else if (_nullConditionalRewriteSupport is NullConditionalRewriteSupport.Rewrite)
            {
                var typeInfo = _semanticModel.GetTypeInfo(node);

                // Do not translate until we can resolve the target type
                if (typeInfo.ConvertedType is not null)
                {
                    // Translate null-conditional into a conditional expression, wrapped inside parenthesis
                    return SyntaxFactory.ParenthesizedExpression(
                        SyntaxFactory.ConditionalExpression(
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.NotEqualsExpression,
                            targetExpression.WithTrailingTrivia(SyntaxFactory.Whitespace(" ")),
                            SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression).WithLeadingTrivia(SyntaxFactory.Whitespace(" "))
                       ).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")),
                       SyntaxFactory.ParenthesizedExpression(
                           (ExpressionSyntax)Visit(node.WhenNotNull)
                       ).WithLeadingTrivia(SyntaxFactory.Whitespace(" ")).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")),
                        SyntaxFactory.CastExpression(
                            SyntaxFactory.ParseName(typeInfo.ConvertedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                            SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                        ).WithLeadingTrivia(SyntaxFactory.Whitespace(" "))
                    ).WithLeadingTrivia(node.GetLeadingTrivia()).WithTrailingTrivia(node.GetTrailingTrivia()));
                }
            }

            return base.VisitConditionalAccessExpression(node);

        }

        public override SyntaxNode? VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            var arms = node.Arms.Reverse();
            var visitedGoverning = (ExpressionSyntax)Visit(node.GoverningExpression);
            ExpressionSyntax? currentExpression = null;

            foreach (var arm in arms)
            {
                var armExpression = (ExpressionSyntax)Visit(arm.Expression);

                if (currentExpression == null)
                {
                    currentExpression = arm.Pattern is DiscardPatternSyntax
                        ? armExpression
                        : SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
                    continue;
                }

                ExpressionSyntax? condition;

                // DeclarationPattern with a named variable requires replacing the variable with a cast in the arm body
                if (arm.Pattern is DeclarationPatternSyntax declaration && declaration.Designation is SingleVariableDesignationSyntax)
                {
                    condition = SyntaxFactory.BinaryExpression(SyntaxKind.IsExpression, visitedGoverning, declaration.Type);
                    armExpression = ReplaceVariableWithCast(armExpression, declaration, visitedGoverning);
                }
                else
                {
                    condition = ConvertPatternToExpression(arm.Pattern, visitedGoverning);
                    if (condition is null)
                    {
                        // A diagnostic (EFP0007) has already been reported for this arm.
                        // Skip it instead of falling back to base.VisitSwitchExpression which
                        // would leave an unsupported switch expression in the generated lambda and
                        // produce unrelated compiler errors.  The best-effort ternary chain built
                        // so far is still emitted so the output remains valid C#.
                        continue;
                    }
                }

                if (arm.WhenClause != null)
                {
                    condition = SyntaxFactory.BinaryExpression(
                        SyntaxKind.LogicalAndExpression,
                        condition,
                        (ExpressionSyntax)Visit(arm.WhenClause.Condition)
                    );
                }

                currentExpression = SyntaxFactory.ConditionalExpression(condition, armExpression, currentExpression);
            }

            return currentExpression;
        }

        public override SyntaxNode? VisitMemberBindingExpression(MemberBindingExpressionSyntax node)
        {
            if (_conditionalAccessExpressionsStack.Count > 0)
            {
                var targetExpression = _conditionalAccessExpressionsStack.Pop();

                return _nullConditionalRewriteSupport switch {
                    NullConditionalRewriteSupport.Ignore => SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, targetExpression, node.Name),
                    NullConditionalRewriteSupport.Rewrite => SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, targetExpression, node.Name),
                    _ => node
                };
            }

            return base.VisitMemberBindingExpression(node);
        }

        public override SyntaxNode? VisitElementBindingExpression(ElementBindingExpressionSyntax node)
        {
            if (_conditionalAccessExpressionsStack.Count > 0)
            {
                var targetExpression = _conditionalAccessExpressionsStack.Pop();

                return _nullConditionalRewriteSupport switch {
                    NullConditionalRewriteSupport.Ignore => SyntaxFactory.ElementAccessExpression(targetExpression, node.ArgumentList),
                    NullConditionalRewriteSupport.Rewrite => SyntaxFactory.ElementAccessExpression(targetExpression, node.ArgumentList),
                    _ => Visit(node)
                };
            }

            return base.VisitElementBindingExpression(node);
        }

        public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node)
        {
            // Swap out the use of this to @this
            return VisitThisBaseExpression(node);
        }

        public override SyntaxNode? VisitBaseExpression(BaseExpressionSyntax node)
        {
            // Swap out the use of this to @this
            return VisitThisBaseExpression(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // Handle C# 14 extension parameter replacement (e.g., `e` in `extension(Entity e)` becomes `@this`)
            if (_extensionParameterName is not null && node.Identifier.Text == _extensionParameterName)
            {
                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                
                // Check if this identifier refers to the extension parameter
                if (symbol is IParameterSymbol { ContainingSymbol: INamedTypeSymbol { IsExtension: true } })
                {
                    return SyntaxFactory.IdentifierName("@this")
                        .WithLeadingTrivia(node.GetLeadingTrivia())
                        .WithTrailingTrivia(node.GetTrailingTrivia());
                }
            }

            var identifierSymbol = _semanticModel.GetSymbolInfo(node).Symbol;
            if (identifierSymbol is not null)
            {
                var operation = node switch { { Parent: { } parent } when parent.IsKind(SyntaxKind.InvocationExpression) => _semanticModel.GetOperation(node.Parent),
                    _ => _semanticModel.GetOperation(node!)
                };

                if (operation is IMemberReferenceOperation memberReferenceOperation)
                {
                    var memberAccessCanBeQualified = node switch { { Parent: { Parent: { } parent } } when parent.IsKind(SyntaxKind.ObjectInitializerExpression) => false,
                        _ => true
                    };

                    if (memberAccessCanBeQualified)
                    {
                        // if this operation is targeting an instance member on our targetType implicitly
                        if (memberReferenceOperation.Instance is { IsImplicit: true } && SymbolEqualityComparer.Default.Equals(memberReferenceOperation.Instance.Type, _targetTypeSymbol))
                        {
                            return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName("@this"),
                                node.WithoutLeadingTrivia()
                            ).WithLeadingTrivia(node.GetLeadingTrivia());
                        }

                        // if this operation is targeting a static member on our targetType implicitly
                        if (memberReferenceOperation.Instance is null && SymbolEqualityComparer.Default.Equals(memberReferenceOperation.Member.ContainingType, _targetTypeSymbol))
                        {
                            return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.ParseTypeName(_targetTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                                node.WithoutLeadingTrivia()
                            ).WithLeadingTrivia(node.GetLeadingTrivia());
                        }
                    }
                }
                else if (operation is IInvocationOperation invocationOperation)
                {
                    // if this operation is targeting an instance method on our targetType implicitly
                    if (invocationOperation.Instance is { IsImplicit: true } && SymbolEqualityComparer.Default.Equals(invocationOperation.Instance.Type, _targetTypeSymbol))
                    {
                        return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("@this"),
                            node.WithoutLeadingTrivia()
                        ).WithLeadingTrivia(node.GetLeadingTrivia());
                    }

                    // if this operation is targeting a static method on our targetType implicitly
                    if (invocationOperation.Instance is null && SymbolEqualityComparer.Default.Equals(invocationOperation.TargetMethod.ContainingType, _targetTypeSymbol))
                    {
                        return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ParseTypeName(_targetTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                            node.WithoutLeadingTrivia()
                        ).WithLeadingTrivia(node.GetLeadingTrivia());
                    }
                }

                // if this node refers to a named type which is not yet fully qualified, we want to fully qualify it
                if (identifierSymbol.Kind is SymbolKind.NamedType && node.Parent?.Kind() is not SyntaxKind.QualifiedName)
                {
                    return SyntaxFactory.ParseTypeName(
                        identifierSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    ).WithLeadingTrivia(node.GetLeadingTrivia());
                }
            }

            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);

            if (symbolInfo.Symbol is not null)
            {
                if (symbolInfo.Symbol.Kind is SymbolKind.NamedType)
                {
                    var typeInfo = _semanticModel.GetTypeInfo(node);

                    if (typeInfo.Type is not null)
                    {
                        return SyntaxFactory.ParseTypeName(
                            typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        );
                    }
                }
            }

            return base.VisitQualifiedName(node);
        }

        public override SyntaxNode? VisitNullableType(NullableTypeSyntax node)
        {
            var typeInfo = _semanticModel.GetTypeInfo(node);
            if (typeInfo.Type is not null)
            {
                if (typeInfo.Type.TypeKind is not TypeKind.Struct)
                {
                    return Visit(node.ElementType)
                        .WithLeadingTrivia(node.GetLeadingTrivia())
                        .WithTrailingTrivia(node.GetTrailingTrivia());
                }
            }

            return base.VisitNullableType(node);
        }

        public override SyntaxNode? VisitInitializerExpression(InitializerExpressionSyntax node)
        {
            // Only handle object initializers that might contain indexer assignments
            if (!node.IsKind(SyntaxKind.ObjectInitializerExpression))
            {
                return base.VisitInitializerExpression(node);
            }

            // Check if any expression is an indexer assignment (e.g., ["key"] = value)
            var hasIndexerAssignment = node.Expressions.Any(e => 
                e is AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax });

            if (!hasIndexerAssignment)
            {
                return base.VisitInitializerExpression(node);
            }

            var newExpressions = new SeparatedSyntaxList<ExpressionSyntax>();

            foreach (var expression in node.Expressions)
            {
                if (expression is AssignmentExpressionSyntax assignment &&
                    assignment.Left is ImplicitElementAccessSyntax implicitElementAccess)
                {
                    // Transform ["key"] = value into { "key", value }
                    var arguments = new SeparatedSyntaxList<ExpressionSyntax>();

                    foreach (var argument in implicitElementAccess.ArgumentList.Arguments)
                    {
                        var visitedArgument = (ExpressionSyntax?)Visit(argument.Expression) ?? argument.Expression;
                        arguments = arguments.Add(visitedArgument);
                    }

                    var visitedValue = (ExpressionSyntax?)Visit(assignment.Right) ?? assignment.Right;
                    arguments = arguments.Add(visitedValue);

                    var complexElementInitializer = SyntaxFactory.InitializerExpression(
                        SyntaxKind.ComplexElementInitializerExpression,
                        arguments
                    );

                    newExpressions = newExpressions.Add(complexElementInitializer);
                }
                else
                {
                    var visitedExpression = (ExpressionSyntax?)Visit(expression) ?? expression;
                    newExpressions = newExpressions.Add(visitedExpression);
                }
            }

            return SyntaxFactory.InitializerExpression(
                SyntaxKind.CollectionInitializerExpression,
                newExpressions
            ).WithTriviaFrom(node);
        }

        private ExpressionSyntax ReplaceVariableWithCast(ExpressionSyntax expression, DeclarationPatternSyntax declaration, ExpressionSyntax governingExpression)
        {
            if (declaration.Designation is SingleVariableDesignationSyntax variableDesignation)
            {
                var variableName = variableDesignation.Identifier.ValueText;
        
                var castExpression = SyntaxFactory.ParenthesizedExpression(
                    SyntaxFactory.CastExpression(
                        declaration.Type,
                        (ExpressionSyntax)Visit(governingExpression)
                    )
                );

                var rewriter = new VariableReplacementRewriter(variableName, castExpression);
                return (ExpressionSyntax)rewriter.Visit(expression);
            }

            return expression;
        }
        
        public override SyntaxNode? VisitIsPatternExpression(IsPatternExpressionSyntax node)
        {
            // Pattern matching is not supported in expression trees (CS8122).
            // We need to convert patterns into equivalent expressions.
            var expression = (ExpressionSyntax)Visit(node.Expression);

            // ConvertPatternToExpression returns null when the pattern cannot be rewritten and has
            // already reported a diagnostic (EFP0007).  Return a 'false' literal placeholder so
            // the generated lambda stays syntactically valid and no additional CS8122 errors are
            // triggered by leaving raw pattern-matching syntax inside an expression tree.
            return ConvertPatternToExpression(node.Pattern, expression)
                ?? SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);
        }

        /// <summary>
        /// Returns true when <paramref name="type"/> can be compared to null.
        /// Accepts a pre-resolved symbol so synthesized (unbound) expression nodes can bypass
        /// semantic-model lookup, which would return <c>null</c> for synthesized nodes and cause
        /// the method to conservatively (and incorrectly) emit a null-check for value-type properties.
        /// </summary>
        private static bool TypeRequiresNullCheck(ITypeSymbol? type)
        {
            if (type is null)
            {
                return true; // conservative: unknown type → assume nullable
            }

            // Nullable<T> is a value type whose OriginalDefinition is System.Nullable<T>
            if (type.IsValueType &&
                type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
            {
                return false; // plain struct / record struct — null check would not compile
            }

            return true;
        }

        /// <summary>
        /// Attempts to convert <paramref name="pattern"/> into an ordinary expression that is valid
        /// inside an expression tree.  Returns <c>null</c> and reports a diagnostic when the pattern
        /// cannot be rewritten.
        /// </summary>
        /// <param name="pattern">The pattern syntax to convert.</param>
        /// <param name="expression">The expression being tested against the pattern.</param>
        /// <param name="expressionType">
        /// Pre-resolved type of <paramref name="expression"/>. When the expression is a synthesized
        /// node (not present in the original source) Roslyn cannot bind it, so callers that know the
        /// type should pass it here to avoid falling back to the conservative "assume nullable" path.
        /// </param>
        private ExpressionSyntax? ConvertPatternToExpression(PatternSyntax pattern, ExpressionSyntax expression, ITypeSymbol? expressionType = null)
        {
            switch (pattern)
            {
                case RecursivePatternSyntax recursivePattern:
                    return ConvertRecursivePattern(recursivePattern, expression, expressionType);

                case ConstantPatternSyntax constantPattern:
                    // e is null  /  e is 5
                    return SyntaxFactory.BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        expression,
                        (ExpressionSyntax)Visit(constantPattern.Expression)
                    );

                case DeclarationPatternSyntax declarationPattern:
                    // e is string _  → type-check only (discard is fine)
                    // e is string s  → we cannot safely rewrite because references to 's' in
                    //                  the surrounding expression are outside this node's scope.
                    if (declarationPattern.Designation is SingleVariableDesignationSyntax)
                    {
                        _context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.UnsupportedPatternInExpression,
                            pattern.GetLocation(),
                            pattern.ToString()));
                        return null;
                    }

                    return SyntaxFactory.BinaryExpression(
                        SyntaxKind.IsExpression,
                        expression,
                        declarationPattern.Type
                    );

                case RelationalPatternSyntax relationalPattern:
                {
                    // e is > 100
                    SyntaxKind? binaryKind = relationalPattern.OperatorToken.Kind() switch
                    {
                        SyntaxKind.LessThanToken        => SyntaxKind.LessThanExpression,
                        SyntaxKind.LessThanEqualsToken  => SyntaxKind.LessThanOrEqualExpression,
                        SyntaxKind.GreaterThanToken     => SyntaxKind.GreaterThanExpression,
                        SyntaxKind.GreaterThanEqualsToken => SyntaxKind.GreaterThanOrEqualExpression,
                        _ => null
                    };

                    if (binaryKind is null)
                    {
                        _context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.UnsupportedPatternInExpression,
                            pattern.GetLocation(),
                            pattern.ToString()));
                        return null;
                    }

                    return SyntaxFactory.BinaryExpression(
                        binaryKind.Value,
                        expression,
                        (ExpressionSyntax)Visit(relationalPattern.Expression)
                    );
                }

                case BinaryPatternSyntax binaryPattern:
                {
                    // e is > 10 and < 100
                    var left  = ConvertPatternToExpression(binaryPattern.Left,  expression);
                    var right = ConvertPatternToExpression(binaryPattern.Right, expression);

                    // Propagate failures from either side
                    if (left is null || right is null)
                    {
                        return null;
                    }

                    SyntaxKind? logicalKind = binaryPattern.OperatorToken.Kind() switch
                    {
                        SyntaxKind.AndKeyword => SyntaxKind.LogicalAndExpression,
                        SyntaxKind.OrKeyword  => SyntaxKind.LogicalOrExpression,
                        _ => null
                    };

                    if (logicalKind is null)
                    {
                        _context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.UnsupportedPatternInExpression,
                            pattern.GetLocation(),
                            pattern.ToString()));
                        return null;
                    }

                    return SyntaxFactory.BinaryExpression(logicalKind.Value, left, right);
                }

                case UnaryPatternSyntax unaryPattern when unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword):
                {
                    // e is not null
                    var inner = ConvertPatternToExpression(unaryPattern.Pattern, expression);
                    if (inner is null)
                    {
                        return null;
                    }

                    return SyntaxFactory.PrefixUnaryExpression(
                        SyntaxKind.LogicalNotExpression,
                        SyntaxFactory.ParenthesizedExpression(inner)
                    );
                }

                default:
                    _context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.UnsupportedPatternInExpression,
                        pattern.GetLocation(),
                        pattern.ToString()));
                    return null;
            }
        }

        private ExpressionSyntax? ConvertRecursivePattern(RecursivePatternSyntax recursivePattern, ExpressionSyntax expression, ITypeSymbol? expressionType = null)
        {
            // Positional / deconstruct patterns (e.g. obj is Point(1, 2)) cannot be rewritten
            // into a plain expression tree.  Report a diagnostic and bail out.
            if (recursivePattern.PositionalPatternClause != null)
            {
                _context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.UnsupportedPatternInExpression,
                    recursivePattern.GetLocation(),
                    recursivePattern.ToString()));
                return null;
            }

            var conditions = new List<ExpressionSyntax>();

            // Null check: only legal (and only necessary) for reference types and nullable value types.
            // Emitting "x != null" for a plain struct / record struct would not compile.
            // Use the pre-resolved expressionType when available so synthesized nodes (which Roslyn
            // cannot bind) are handled correctly instead of falling back to the conservative path.
            var typeForNullCheck = expressionType ?? _semanticModel.GetTypeInfo(expression).Type;
            if (TypeRequiresNullCheck(typeForNullCheck))
            {
                conditions.Add(SyntaxFactory.BinaryExpression(
                    SyntaxKind.NotEqualsExpression,
                    expression,
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                ));
            }

            // Type check: "obj is SomeType { ... }" — add "expression is SomeType" guard.
            TypeSyntax? visitedType = null;
            if (recursivePattern.Type != null)
            {
                visitedType = (TypeSyntax)Visit(recursivePattern.Type);
                conditions.Add(SyntaxFactory.BinaryExpression(
                    SyntaxKind.IsExpression,
                    expression,
                    visitedType
                ));
            }

            // When a concrete type is known, member accesses on sub-patterns must go through a
            // cast so the generated code compiles correctly (e.g. ((SomeType)expression).Prop).
            var memberBase = visitedType != null
                ? SyntaxFactory.ParenthesizedExpression(
                    SyntaxFactory.CastExpression(visitedType, expression))
                : expression;

            // Handle property sub-patterns: { Prop: value, ... }
            if (recursivePattern.PropertyPatternClause != null)
            {
                foreach (var subpattern in recursivePattern.PropertyPatternClause.Subpatterns)
                {
                    ExpressionSyntax propExpression;
                    ITypeSymbol? propType = null;

                    if (subpattern.NameColon != null)
                    {
                        // Look up the property/field type from the original source binding so that
                        // when the recursive ConvertPatternToExpression call checks TypeRequiresNullCheck
                        // on the synthesized propExpression it receives the real symbol instead of null.
                        var memberSymbol = _semanticModel.GetSymbolInfo(subpattern.NameColon.Name).Symbol;
                        propType = memberSymbol switch
                        {
                            IPropertySymbol prop  => prop.Type,
                            IFieldSymbol    field => field.Type,
                            _                     => null
                        };

                        propExpression = SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            memberBase,
                            SyntaxFactory.IdentifierName(subpattern.NameColon.Name.Identifier));
                    }
                    else
                    {
                        propExpression = memberBase;
                    }

                    // Pass propType so nested recursive patterns don't misidentify value-type
                    // properties as nullable when Roslyn can't bind the synthesized node.
                    var condition = ConvertPatternToExpression(subpattern.Pattern, propExpression, propType);
                    if (condition is null)
                    {
                        return null; // diagnostic already emitted
                    }

                    conditions.Add(condition);
                }
            }

            if (conditions.Count == 0)
            {
                return SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression);
            }

            // Combine all conditions with &&
            var result = conditions[0];
            for (var i = 1; i < conditions.Count; i++)
            {
                result = SyntaxFactory.BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    result,
                    conditions[i]
                );
            }

            return result;
        }
    }
}
