using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator;

static internal class EnumMethodCallEvaluator
{
    static internal ExpressionSyntax? TryEvaluateMethodCall(IMethodSymbol methodSymbol, ITypeSymbol enumType, IFieldSymbol enumMember, ArgumentListSyntax argumentList)
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

    private static ExpressionSyntax? TryInterpretMethodForEnumMember(IMethodSymbol methodSymbol, IFieldSymbol enumMember, System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
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
}