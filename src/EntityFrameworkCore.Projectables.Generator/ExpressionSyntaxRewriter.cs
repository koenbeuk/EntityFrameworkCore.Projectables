using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables.Generator
{

    public class ExpressionSyntaxRewriter : CSharpSyntaxRewriter
    {
        readonly INamedTypeSymbol _targetTypeSymbol;
        readonly SemanticModel _semanticModel;
        readonly NullConditionalRewriteSupport _nullConditionalRewriteSupport;
        readonly bool _expandEnumMethods;
        readonly SourceProductionContext _context;
        readonly Compilation _compilation;
        readonly Stack<ExpressionSyntax> _conditionalAccessExpressionsStack = new();
        
        public ExpressionSyntaxRewriter(INamedTypeSymbol targetTypeSymbol, NullConditionalRewriteSupport nullConditionalRewriteSupport, bool expandEnumMethods, SemanticModel semanticModel, Compilation compilation, SourceProductionContext context)
        {
            _targetTypeSymbol = targetTypeSymbol;
            _nullConditionalRewriteSupport = nullConditionalRewriteSupport;
            _expandEnumMethods = expandEnumMethods;
            _semanticModel = semanticModel;
            _compilation = compilation;
            _context = context;
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
            if (node.Expression is MemberAccessExpressionSyntax memberAccessExpressionSyntax)
            {
                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol is IMethodSymbol methodSymbol)
                {
                    // Check if we should expand enum methods
                    if (_expandEnumMethods)
                    {
                        if (TryExpandEnumMethodCall(node, memberAccessExpressionSyntax, methodSymbol, out var expandedExpression))
                        {
                            return expandedExpression;
                        }
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
                }
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
            bool isNullable = false;
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

            // Build a chain of ternary expressions for each enum value
            // Start with null as the fallback
            ExpressionSyntax? currentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);

            // Evaluate the method call for each enum value and build the ternary chain
            foreach (var enumMember in enumMembers.AsEnumerable().Reverse())
            {
                // Try to evaluate the method call at compile time
                var evaluatedValue = TryEvaluateMethodCall(methodSymbol, enumType, enumMember, node.ArgumentList);
                if (evaluatedValue == null)
                {
                    // Cannot evaluate at compile time
                    return false;
                }

                // Create condition: receiver == EnumType.Value
                var enumValueAccess = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseTypeName(enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                    SyntaxFactory.IdentifierName(enumMember.Name)
                );

                var condition = SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    visitedReceiver,
                    enumValueAccess
                );

                // Create conditional expression: condition ? evaluatedValue : previousExpression
                currentExpression = SyntaxFactory.ConditionalExpression(
                    condition,
                    evaluatedValue,
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
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
                    currentExpression
                );
            }

            expandedExpression = SyntaxFactory.ParenthesizedExpression(currentExpression);
            return true;
        }

        private ExpressionSyntax? TryEvaluateMethodCall(IMethodSymbol methodSymbol, ITypeSymbol enumType, IFieldSymbol enumMember, ArgumentListSyntax argumentList)
        {
            // For now, we support methods that return string? and take no additional arguments (besides the enum value for extension methods)
            // We need to evaluate the method call at compile time by using the Compilation to run the code

            // Check if method has a single parameter (the extension 'this' parameter) or no parameters
            // Note: For reduced extension method calls (e.g., x.Method()), the `this` parameter is not included in Parameters
            // We need to check ReducedFrom to get the original method with all parameters
            var originalMethod = methodSymbol.ReducedFrom ?? methodSymbol;
            var expectedParameters = originalMethod.IsExtensionMethod ? 1 : 0;
            var additionalArguments = argumentList.Arguments.Count;
            
            if (originalMethod.Parameters.Length != expectedParameters + additionalArguments)
            {
                // Method has unexpected number of parameters
                return null;
            }

            // For simplicity in this implementation, we only handle methods with no additional arguments
            // and methods that return simple types (string, int, etc.)
            if (additionalArguments > 0)
            {
                return null;
            }

            // Try to get the constant value of the method call through interpreter-based evaluation
            // Since we can't actually execute arbitrary code at compile time in a source generator,
            // we will look for certain patterns we can handle:
            
            // Pattern 1: GetAttribute<T>()?.Name or similar attribute-based patterns
            // We need to look at the enum member's attributes
            
            // Get the method body to see what it does
            var methodSyntaxRef = originalMethod.DeclaringSyntaxReferences.FirstOrDefault();
            if (methodSyntaxRef == null)
            {
                return null;
            }
            
            // For now, let's support a common pattern: accessing attributes on enum members
            // We'll check if the enum member has attributes and try to match them
            var attributes = enumMember.GetAttributes();
            
            // Try to interpret common patterns
            return TryInterpretMethodForEnumMember(originalMethod, enumMember, attributes);
        }

        private ExpressionSyntax? TryInterpretMethodForEnumMember(IMethodSymbol methodSymbol, IFieldSymbol enumMember, System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
        {
            // This is a simplified implementation that handles specific known patterns
            // A more complete implementation would need to actually interpret the method body
            
            // Get the method's return type
            var returnType = methodSymbol.ReturnType;
            
            // If the return type is string or string?, we can try to find Display attributes
            if (returnType.SpecialType == SpecialType.System_String || 
                (returnType is INamedTypeSymbol { IsReferenceType: true } && returnType.ToDisplayString().StartsWith("string")))
            {
                // Look for DisplayAttribute
                var displayAttr = attributes.FirstOrDefault(a => 
                    a.AttributeClass?.Name == "DisplayAttribute" ||
                    a.AttributeClass?.ToDisplayString().EndsWith("DisplayAttribute") == true);
                
                if (displayAttr != null)
                {
                    // Try to get the Name property
                    var nameArg = displayAttr.NamedArguments.FirstOrDefault(na => na.Key == "Name");
                    if (nameArg.Key != null && nameArg.Value.Value is string nameValue)
                    {
                        return SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(nameValue));
                    }
                }
                
                // Look for DescriptionAttribute
                var descAttr = attributes.FirstOrDefault(a => 
                    a.AttributeClass?.Name == "DescriptionAttribute" ||
                    a.AttributeClass?.ToDisplayString().EndsWith("DescriptionAttribute") == true);
                
                if (descAttr != null && descAttr.ConstructorArguments.Length > 0)
                {
                    var descValue = descAttr.ConstructorArguments[0].Value as string;
                    if (descValue != null)
                    {
                        return SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(descValue));
                    }
                }

                // If no matching attribute found, return null literal
                return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
            }

            // For other return types, we can't handle them yet
            return null;
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
            var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is not null)
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
                if (symbol.Kind is SymbolKind.NamedType && node.Parent?.Kind() is not SyntaxKind.QualifiedName)
                {
                    return SyntaxFactory.ParseTypeName(
                        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
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
