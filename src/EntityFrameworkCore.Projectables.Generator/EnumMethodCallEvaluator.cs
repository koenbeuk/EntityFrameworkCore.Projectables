using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator;

static internal class EnumMethodCallEvaluator
{
    private const string ProjectableEnumMethodAttributeName = "EntityFrameworkCore.Projectables.ProjectableEnumMethodAttribute";

    /// <summary>
    /// Tries to evaluate an enum method call at compile time using the ProjectableEnumMethodAttribute.
    /// </summary>
    /// <param name="methodSymbol">The method symbol being called.</param>
    /// <param name="enumType">The enum type.</param>
    /// <param name="enumMember">The specific enum member to evaluate.</param>
    /// <param name="argumentList">The argument list of the invocation.</param>
    /// <param name="context">The source production context for reporting diagnostics.</param>
    /// <param name="location">The location for diagnostic reporting.</param>
    /// <returns>The evaluated expression, or null if evaluation failed.</returns>
    static internal ExpressionSyntax? TryEvaluateMethodCall(
        IMethodSymbol methodSymbol, 
        ITypeSymbol enumType, 
        IFieldSymbol enumMember, 
        ArgumentListSyntax argumentList,
        SourceProductionContext context,
        Location? location)
    {
        // Get the original method (in case of reduced extension method)
        var originalMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        var expectedParameters = originalMethod.IsExtensionMethod ? 1 : 0;
        var additionalArguments = argumentList.Arguments.Count;
            
        if (originalMethod.Parameters.Length != expectedParameters + additionalArguments)
        {
            return null;
        }

        // For simplicity in this implementation, we only handle methods with no additional arguments
        if (additionalArguments > 0)
        {
            return null;
        }

        // Look for ProjectableEnumMethodAttribute on the method
        var projectableEnumMethodAttr = originalMethod.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ProjectableEnumMethodAttributeName);

        if (projectableEnumMethodAttr == null)
        {
            // Report diagnostic for missing attribute
            var diagnostic = Diagnostic.Create(
                Diagnostics.MissingProjectableEnumMethodAttribute, 
                location, 
                originalMethod.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat));
            context.ReportDiagnostic(diagnostic);
            return null;
        }

        // Parse the attribute arguments
        INamedTypeSymbol? attributeType = null;
        string? propertyName = null;

        if (projectableEnumMethodAttr.ConstructorArguments.Length >= 1)
        {
            attributeType = projectableEnumMethodAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
        }

        if (projectableEnumMethodAttr.ConstructorArguments.Length >= 2)
        {
            propertyName = projectableEnumMethodAttr.ConstructorArguments[1].Value as string;
        }

        // Get the enum member's attributes
        var enumMemberAttributes = enumMember.GetAttributes();

        // If no attribute type specified, we can't evaluate
        if (attributeType == null)
        {
            // Try to infer from generic type argument if present
            if (originalMethod.TypeArguments.Length > 0)
            {
                attributeType = originalMethod.TypeArguments[0] as INamedTypeSymbol;
            }
            
            if (attributeType == null)
            {
                return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
            }
        }

        // Find the matching attribute on the enum member
        var matchingAttr = enumMemberAttributes.FirstOrDefault(a => 
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType) ||
            a.AttributeClass?.ToDisplayString() == attributeType.ToDisplayString());

        if (matchingAttr == null)
        {
            return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }

        // Extract the value based on whether we have a property name or not
        if (propertyName != null)
        {
            // Get the named argument with the specified property name
            var namedArg = matchingAttr.NamedArguments.FirstOrDefault(na => na.Key == propertyName);
            if (namedArg.Key != null)
            {
                return CreateLiteralExpression(namedArg.Value.Value);
            }
            return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }
        else
        {
            // Get the first constructor argument value
            if (matchingAttr.ConstructorArguments.Length > 0)
            {
                return CreateLiteralExpression(matchingAttr.ConstructorArguments[0].Value);
            }
            return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }
    }

    private static ExpressionSyntax CreateLiteralExpression(object? value)
    {
        return value switch
        {
            string s => SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(s)),
            int i => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(i)),
            bool b => b ? SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression) 
                       : SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression),
            null => SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
            _ => SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
        };
    }
}