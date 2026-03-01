using Microsoft.CodeAnalysis;

namespace EntityFrameworkCore.Projectables.Generator
{
    public static class Diagnostics
    {
        public static readonly DiagnosticDescriptor BlockBodyExperimental = new DiagnosticDescriptor(
            id: "EFP0001",
            title: "Block-bodied member support is experimental",
            messageFormat: "Block-bodied member '{0}' is using an experimental feature. Set AllowBlockBody = true on the Projectable attribute to suppress this warning.",
            category: "Design",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NullConditionalRewriteUnsupported = new DiagnosticDescriptor(
            id: "EFP0002",
            title: "Method or property is not configured to support null-conditional expressions",
            messageFormat: "'{0}' has a null-conditional expression exposed but is not configured to rewrite this (Consider configuring a strategy using the NullConditionalRewriteSupport property on the Projectable attribute)",
            category: "Design",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedStatementInBlockBody = new DiagnosticDescriptor(
            id: "EFP0003",
            title: "Unsupported statement in block-bodied method",
            messageFormat: "Method '{0}' contains an unsupported statement: {1}",
            category: "Design",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SideEffectInBlockBody = new DiagnosticDescriptor(
            id: "EFP0004",
            title: "Statement with side effects in block-bodied method",
            messageFormat: "{0}",
            category: "Design",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PotentialSideEffectInBlockBody = new DiagnosticDescriptor(
            id: "EFP0005",
            title: "Potential side effect in block-bodied method",
            messageFormat: "{0}",
            category: "Design",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RequiresBodyDefinition = new DiagnosticDescriptor(
            id: "EFP0006",
            title: "Method or property should expose a body definition",
            messageFormat: "Method or property '{0}' should expose a body definition (e.g. an expression-bodied member or a block-bodied method) to be used as the source for the generated expression tree.",
            category: "Design",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedPatternInExpression = new DiagnosticDescriptor(
            id: "EFP0007",
            title: "Unsupported pattern in projectable expression",
            messageFormat: "The pattern '{0}' cannot be rewritten into an expression tree. Simplify the pattern or restructure the projectable member body.",
            category: "Design",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    }
}
