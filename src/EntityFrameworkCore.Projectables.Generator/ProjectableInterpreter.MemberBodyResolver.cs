using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator;

public static partial class ProjectableInterpreter
{
    /// <summary>
    /// Resolves the member body syntax to use.
    /// <para>
    /// When <paramref name="useMemberBody"/> is <c>null</c>, returns <paramref name="member"/> unchanged.
    /// When resolution succeeds, returns the resolved body.
    /// When resolution fails (silent skip or diagnostic reported), returns <c>null</c>.
    /// </para>
    /// </summary>
    private static MemberDeclarationSyntax? TryResolveMemberBody(
        MemberDeclarationSyntax member,
        ISymbol memberSymbol,
        string? useMemberBody,
        SourceProductionContext context)
    {
        if (useMemberBody is null)
        {
            return member;
        }

        var comparer = SymbolEqualityComparer.Default;

        // Step 1: find all members with the requested name
        var allCandidates = memberSymbol.ContainingType.GetMembers(useMemberBody);

        if (allCandidates.IsEmpty)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UseMemberBodyNotFound,
                member.GetLocation(),
                memberSymbol.Name,
                useMemberBody,
                memberSymbol.ContainingType.Name));
            return null;
        }

        // Step 2: partition into same-type candidates and Expression<TDelegate> property candidates
        var regularCompatible = allCandidates.Where(x =>
        {
            if (memberSymbol is IMethodSymbol symbolMethod &&
                x is IMethodSymbol xMethod &&
                comparer.Equals(symbolMethod.ReturnType, xMethod.ReturnType) &&
                symbolMethod.TypeArguments.Length == xMethod.TypeArguments.Length &&
                !symbolMethod.TypeArguments.Zip(xMethod.TypeArguments, (a, b) => !comparer.Equals(a, b)).Any(v => v) &&
                symbolMethod.Parameters.Length == xMethod.Parameters.Length &&
                !symbolMethod.Parameters.Zip(xMethod.Parameters,
                    (a, b) => !comparer.Equals(a.Type, b.Type) || a.RefKind != b.RefKind).Any(v => v))
            {
                return true;
            }

            if (memberSymbol is IPropertySymbol symbolProperty &&
                x is IPropertySymbol xProperty &&
                comparer.Equals(symbolProperty.Type, xProperty.Type))
            {
                return true;
            }

            return false;
        }).ToList();

        // Expression-property candidates: a property returning Expression<TDelegate>.
        // Supported in the generator only when the projectable member is a method.
        // When the projectable member is a property, the runtime resolver handles it.
        var exprPropertyCandidates = memberSymbol is IMethodSymbol
            ? allCandidates.Where(IsExpressionDelegateProperty).ToList()
            : [];

        // Filter Expression<TDelegate> candidates whose Func generic-argument count is
        // compatible with the projectable method's parameter list.
        List<ISymbol> compatibleExprPropertyCandidates = [];
        if (exprPropertyCandidates.Count > 0 && memberSymbol is IMethodSymbol exprCheckMethod)
        {
            var isExtensionBlock = memberSymbol.ContainingType is { IsExtension: true };
            var hasImplicitThis = !exprCheckMethod.IsStatic || isExtensionBlock;
            var expectedFuncArgCount = exprCheckMethod.Parameters.Length + (hasImplicitThis ? 2 : 1);

            compatibleExprPropertyCandidates = exprPropertyCandidates.Where(x =>
            {
                if (x is not IPropertySymbol propSym)
                {
                    return false;
                }

                if (propSym.Type is not INamedTypeSymbol exprType || exprType.TypeArguments.Length != 1)
                {
                    return false;
                }

                if (exprType.TypeArguments[0] is not INamedTypeSymbol delegateType)
                {
                    return false;
                }

                return delegateType.TypeArguments.Length == expectedFuncArgCount;
            }).ToList();
        }

        // Step 3: if no generator-handled candidates exist, diagnose or skip
        if (regularCompatible.Count == 0 && compatibleExprPropertyCandidates.Count == 0)
        {
            // Expression properties were found but all have incompatible Func signatures.
            if (exprPropertyCandidates.Count > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.UseMemberBodyIncompatible,
                    member.GetLocation(),
                    memberSymbol.Name,
                    useMemberBody));
                return null;
            }

            // A projectable *property* backed by an Expression<TDelegate> property is
            // handled at runtime by ProjectionExpressionResolver; skip silently so the
            // runtime path can take over without a spurious error.
            if (memberSymbol is IPropertySymbol && allCandidates.Any(IsExpressionDelegateProperty))
            {
                return null;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UseMemberBodyIncompatible,
                member.GetLocation(),
                memberSymbol.Name,
                useMemberBody));
            return null;
        }

        // Step 4a: locate valid syntax for regular (same-type) candidates
        var resolvedBody = regularCompatible
            .SelectMany(x => x.DeclaringSyntaxReferences)
            .Select(x => x.GetSyntax())
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(x =>
            {
                if (x == null ||
                    x.SyntaxTree != member.SyntaxTree ||
                    x.Modifiers.Any(SyntaxKind.StaticKeyword) != member.Modifiers.Any(SyntaxKind.StaticKeyword))
                {
                    return false;
                }

                if (x is MethodDeclarationSyntax xMethod &&
                    (xMethod.ExpressionBody is not null || xMethod.Body is not null))
                {
                    return true;
                }

                if (x is PropertyDeclarationSyntax xProp)
                {
                    return HasReadablePropertyBody(xProp);
                }

                return false;
            });

        // Step 4b: if not found, try compatible Expression<TDelegate> property candidates.
        // These don't need to share the member's static modifier because a
        // static Expression<Func<...>> property can legitimately back either
        // a static or an instance projectable method.
        if (resolvedBody is null && compatibleExprPropertyCandidates.Count > 0)
        {
            resolvedBody = compatibleExprPropertyCandidates
                .SelectMany(x => x.DeclaringSyntaxReferences)
                .Select(x => x.GetSyntax())
                .OfType<MemberDeclarationSyntax>()
                .FirstOrDefault(x =>
                {
                    if (x == null || x.SyntaxTree != member.SyntaxTree)
                    {
                        return false;
                    }

                    return x is PropertyDeclarationSyntax xProp && HasReadablePropertyBody(xProp);
                });
        }

        // Step 5: if still null, the candidates exist but are syntactically
        //         inaccessible (different file, no body, etc.)
        if (resolvedBody is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UseMemberBodyIncompatible,
                member.GetLocation(),
                memberSymbol.Name,
                useMemberBody));
            return null;
        }

        return resolvedBody;
    }

    /// <summary>Returns true when a <see cref="PropertyDeclarationSyntax"/> has a readable body.</summary>
    private static bool HasReadablePropertyBody(PropertyDeclarationSyntax xProp)
    {
        if (xProp.ExpressionBody is not null)
        {
            return true;
        }

        if (xProp.AccessorList is not null)
        {
            var getter = xProp.AccessorList.Accessors
                .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));

            if (getter?.ExpressionBody is not null || getter?.Body is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns true when a symbol is a property returning <c>Expression&lt;TDelegate&gt;</c>.</summary>
    private static bool IsExpressionDelegateProperty(ISymbol sym) =>
        sym is IPropertySymbol p &&
        p.Type is INamedTypeSymbol { Name: "Expression", IsGenericType: true } et &&
        et.TypeArguments.Length == 1 &&
        et.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions";
}