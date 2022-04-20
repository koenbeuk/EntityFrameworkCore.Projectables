using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace EntityFrameworkCore.Projectables.Generator
{
    public static class Diagnostics
    {
        public static readonly DiagnosticDescriptor RequiresExpressionBodyDefinition = new DiagnosticDescriptor(
            id: "EFP0001",
            title: "Method or property should expose an expression body definition",
            messageFormat: "Method or property '{0}' should expose an expression body definition",
            category: "Design",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NullConditionalRewriteUnsupported = new DiagnosticDescriptor(
            id: "EFP0002",
            title: "Method or property is not configured to support null-conditional expressions",
            messageFormat: "'{0}' has a null-conditional expression exposed but is not configured to rewrite this (Consider configuring a strategy using the NullConditionalRewriteSupport property on the Projectable attribute)",
            category: "Design",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    }
}
