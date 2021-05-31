using Microsoft.CodeAnalysis;

namespace EntityFrameworkCore.Projectables.Generator
{
    public static class Diagnostics
    {
        public static readonly DiagnosticDescriptor RequiresExpressionBodyDefinition = new(
            id: "EFP0001",
            title: "Method or property should expose an expression body definition",
            messageFormat: "Method or property '{0}' should expose an expression body definition",
            category: "Design",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
