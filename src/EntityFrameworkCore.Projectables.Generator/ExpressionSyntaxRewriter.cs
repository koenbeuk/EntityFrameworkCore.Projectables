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
            // Reverse arms order to start from the default value
            var arms = node.Arms.Reverse();

            ExpressionSyntax? currentExpression = null;

            foreach (var arm in arms)
            {
                var armExpression = (ExpressionSyntax)Visit(arm.Expression);
                
                // Handle fallback value
                if (currentExpression == null)
                {
                    currentExpression = arm.Pattern is DiscardPatternSyntax 
                        ? armExpression
                        : SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);

                    continue;
                }
                
                // Handle each arm, only if it's a constant expression
                if (arm.Pattern is ConstantPatternSyntax constant)
                {
                    ExpressionSyntax expression = SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, (ExpressionSyntax)Visit(node.GoverningExpression), constant.Expression);
                    
                    // Add the when clause as a AND expression
                    if (arm.WhenClause != null)
                    {
                        expression = SyntaxFactory.BinaryExpression(
                            SyntaxKind.LogicalAndExpression, 
                            expression,
                            (ExpressionSyntax)Visit(arm.WhenClause.Condition)
                        );
                    }
                    
                    currentExpression = SyntaxFactory.ConditionalExpression(
                        expression,
                        armExpression,
                        currentExpression
                    );

                    continue;
                }

                if (arm.Pattern is DeclarationPatternSyntax declaration)
                {
                    var getTypeExpression = SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        (ExpressionSyntax)Visit(node.GoverningExpression),
                        SyntaxFactory.IdentifierName("GetType")
                    );

                    var getTypeCall = SyntaxFactory.InvocationExpression(getTypeExpression);
                    var typeofExpression = SyntaxFactory.TypeOfExpression(declaration.Type);
                    var equalsExpression = SyntaxFactory.BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        getTypeCall,
                        typeofExpression
                    );

                    ExpressionSyntax condition = equalsExpression;
                    if (arm.WhenClause != null)
                    {
                        condition = SyntaxFactory.BinaryExpression(
                            SyntaxKind.LogicalAndExpression, 
                            equalsExpression,
                            (ExpressionSyntax)Visit(arm.WhenClause.Condition)
                        );
                    }

                    var modifiedArmExpression = ReplaceVariableWithCast(armExpression, declaration, node.GoverningExpression);
                    currentExpression = SyntaxFactory.ConditionalExpression(
                        condition,
                        modifiedArmExpression,
                        currentExpression
                    );

                    continue;
                }

                throw new InvalidOperationException(
                    $"Switch expressions rewriting supports only constant values and declaration patterns (Type var). " +
                    $"Unsupported pattern: {arm.Pattern.GetType().Name}"
                );
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
    }
}
