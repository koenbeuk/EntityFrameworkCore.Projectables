using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator
{
    /// <summary>
    /// Converts constructor body statements into a dictionary of property-name → expression
    /// pairs that are used to build a member-init expression for EF Core projections.
    /// Supports simple assignments, local variable declarations, and if/else statements.
    /// </summary>
    public class ConstructorBodyConverter
    {
        private readonly SourceProductionContext _context;

        /// <summary>
        /// Expression-level rewriter applied to every RHS/condition expression.
        /// For the main constructor body this is the <see cref="ExpressionSyntaxRewriter"/>;
        /// for a delegated (base/this) constructor body it is the identity function because
        /// the syntax belongs to a different compilation context and only parameter substitution
        /// is needed.
        /// </summary>
        private readonly Func<ExpressionSyntax, ExpressionSyntax> _rewrite;

        /// <summary>
        /// Maps base/this constructor parameter names to the rewritten argument expressions
        /// supplied at the call site. Empty when processing the main constructor body.
        /// </summary>
        private readonly Dictionary<string, ExpressionSyntax> _paramSubstitutions;

        /// <summary>Local variable name → already-rewritten initializer expression.</summary>
        private readonly Dictionary<string, ExpressionSyntax> _localVariables = new();

        /// <summary>
        /// Creates a converter for the <em>main</em> constructor body.
        /// The <paramref name="expressionRewriter"/> is applied to every expression encountered.
        /// </summary>
        public ConstructorBodyConverter(
            SourceProductionContext context,
            ExpressionSyntaxRewriter expressionRewriter)
        {
            _context = context;
            _rewrite = expr => (ExpressionSyntax)expressionRewriter.Visit(expr);
            _paramSubstitutions = new Dictionary<string, ExpressionSyntax>();
        }

        /// <summary>
        /// Creates a converter for a <em>delegated</em> (base/this) constructor body.
        /// No expression-level rewriter is applied; only <paramref name="paramSubstitutions"/>
        /// are substituted (parameter name → call-site argument expression).
        /// </summary>
        public ConstructorBodyConverter(
            SourceProductionContext context,
            Dictionary<string, ExpressionSyntax> paramSubstitutions)
        {
            _context = context;
            _rewrite = expr => expr; // identity – base-ctor syntax lives in its own context
            _paramSubstitutions = paramSubstitutions;
        }

        /// <summary>
        /// Tries to convert <paramref name="statements"/> into a property-name → expression map.
        /// Returns <c>null</c> if conversion fails (diagnostics are reported on the context).
        /// </summary>
        public Dictionary<string, ExpressionSyntax>? TryConvertBody(
            IEnumerable<StatementSyntax> statements,
            string memberName)
        {
            var assignments = new Dictionary<string, ExpressionSyntax>();
            foreach (var statement in statements)
            {
                if (!TryProcessStatement(statement, assignments, memberName))
                {
                    return null;
                }
            }
            return assignments;
        }

        private bool TryProcessStatement(
            StatementSyntax statement,
            Dictionary<string, ExpressionSyntax> assignments,
            string memberName)
        {
            switch (statement)
            {
                case LocalDeclarationStatementSyntax localDecl:
                    return TryProcessLocalDeclaration(localDecl, memberName);

                case ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment }
                    when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                    return TryProcessAssignment(assignment, assignments, memberName);

                case IfStatementSyntax ifStmt:
                    return TryProcessIfStatement(ifStmt, assignments, memberName);

                case BlockSyntax block:
                    return TryProcessBlock(block.Statements, assignments, memberName);

                default:
                    ReportUnsupported(statement, memberName,
                        $"Statement type '{statement.GetType().Name}' is not supported in a [Projectable] constructor body. " +
                        "Only assignments, local variable declarations, and if/else statements are supported.");
                    return false;
            }
        }

        private bool TryProcessLocalDeclaration(LocalDeclarationStatementSyntax localDecl, string memberName)
        {
            foreach (var variable in localDecl.Declaration.Variables)
            {
                if (variable.Initializer == null)
                {
                    ReportUnsupported(localDecl, memberName, "Local variables must have an initializer");
                    return false;
                }

                var rewritten = _rewrite(variable.Initializer.Value);
                rewritten = ApplySubstitutions(rewritten);
                _localVariables[variable.Identifier.Text] = rewritten;
            }
            return true;
        }

        private bool TryProcessAssignment(
            AssignmentExpressionSyntax assignment,
            Dictionary<string, ExpressionSyntax> assignments,
            string memberName)
        {
            var targetMember = GetTargetMember(assignment.Left);
            if (targetMember is null)
            {
                ReportUnsupported(assignment, memberName,
                    $"Unsupported assignment target '{assignment.Left}'. " +
                    "Only 'PropertyName = ...' or 'this.PropertyName = ...' are supported.");
                return false;
            }

            var rewritten = _rewrite(assignment.Right);
            rewritten = ApplySubstitutions(rewritten);
            assignments[targetMember.Identifier.Text] = rewritten;
            return true;
        }

        private bool TryProcessIfStatement(
            IfStatementSyntax ifStmt,
            Dictionary<string, ExpressionSyntax> assignments,
            string memberName)
        {
            // Rewrite and substitute the condition
            var condition = _rewrite(ifStmt.Condition);
            condition = ApplySubstitutions(condition);

            // Process then-branch
            var thenAssignments = new Dictionary<string, ExpressionSyntax>();
            if (!TryProcessBlock(GetStatements(ifStmt.Statement), thenAssignments, memberName))
                return false;

            // Process else-branch (may be absent)
            var elseAssignments = new Dictionary<string, ExpressionSyntax>();
            if (ifStmt.Else != null)
            {
                if (!TryProcessBlock(GetStatements(ifStmt.Else.Statement), elseAssignments, memberName))
                    return false;
            }

            // Merge: for each property assigned in the then-branch create a ternary that
            // falls back to the else-branch value, the already-accumulated value, or default.
            foreach (var thenKvp in thenAssignments)
            {
                var prop = thenKvp.Key;
                var thenExpr = thenKvp.Value;

                ExpressionSyntax elseExpr;
                if (elseAssignments.TryGetValue(prop, out var elseVal))
                {
                    elseExpr = elseVal;
                }
                else if (assignments.TryGetValue(prop, out var existing))
                {
                    // The else-branch doesn't touch this property – keep the pre-if value.
                    elseExpr = existing;
                }
                else
                {
                    elseExpr = DefaultLiteral();
                }

                assignments[prop] = SyntaxFactory.ConditionalExpression(condition, thenExpr, elseExpr);
            }

            // For properties only in the else-branch
            foreach (var elseKvp in elseAssignments)
            {
                var prop = elseKvp.Key;
                var elseExpr = elseKvp.Value;

                if (thenAssignments.ContainsKey(prop))
                    continue; // already handled above

                ExpressionSyntax thenExpr;
                if (assignments.TryGetValue(prop, out var existing))
                {
                    thenExpr = existing;
                }
                else
                {
                    thenExpr = DefaultLiteral();
                }

                assignments[prop] = SyntaxFactory.ConditionalExpression(condition, thenExpr, elseExpr);
            }

            return true;
        }

        private bool TryProcessBlock(
            IEnumerable<StatementSyntax> statements,
            Dictionary<string, ExpressionSyntax> assignments,
            string memberName)
        {
            foreach (var statement in statements)
            {
                if (!TryProcessStatement(statement, assignments, memberName))
                    return false;
            }
            return true;
        }

        private static IEnumerable<StatementSyntax> GetStatements(StatementSyntax statement) =>
            statement is BlockSyntax block
                ? block.Statements
                : new StatementSyntax[] { statement };

        private static IdentifierNameSyntax? GetTargetMember(ExpressionSyntax left) =>
            left switch
            {
                MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } => name,
                IdentifierNameSyntax ident => ident,
                _ => null
            };

        private ExpressionSyntax ApplySubstitutions(ExpressionSyntax expr)
        {
            if (_paramSubstitutions.Count > 0)
                expr = ParameterSubstitutor.Substitute(expr, _paramSubstitutions);
            if (_localVariables.Count > 0)
                expr = LocalVariableSubstitutor.Substitute(expr, _localVariables);
            return expr;
        }

        private static ExpressionSyntax DefaultLiteral() =>
            SyntaxFactory.LiteralExpression(
                SyntaxKind.DefaultLiteralExpression,
                SyntaxFactory.Token(SyntaxKind.DefaultKeyword));

        private void ReportUnsupported(SyntaxNode node, string memberName, string reason)
        {
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UnsupportedStatementInBlockBody,
                node.GetLocation(),
                memberName,
                reason));
        }

        /// <summary>
        /// Replaces identifier names that match base/this-constructor parameter names with the
        /// corresponding outer argument expressions.
        /// </summary>
        internal sealed class ParameterSubstitutor : CSharpSyntaxRewriter
        {
            private readonly Dictionary<string, ExpressionSyntax> _map;

            private ParameterSubstitutor(Dictionary<string, ExpressionSyntax> map) => _map = map;

            public static ExpressionSyntax Substitute(ExpressionSyntax expr, Dictionary<string, ExpressionSyntax> map)
                => (ExpressionSyntax)new ParameterSubstitutor(map).Visit(expr);

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
                => _map.TryGetValue(node.Identifier.Text, out var replacement)
                    ? replacement.WithTriviaFrom(node)
                    : base.VisitIdentifierName(node);
        }

        /// <summary>
        /// Replaces local-variable identifier references with their already-rewritten
        /// initializer expressions (parenthesised to preserve operator precedence).
        /// </summary>
        private sealed class LocalVariableSubstitutor : CSharpSyntaxRewriter
        {
            private readonly Dictionary<string, ExpressionSyntax> _locals;

            private LocalVariableSubstitutor(Dictionary<string, ExpressionSyntax> locals) => _locals = locals;

            public static ExpressionSyntax Substitute(ExpressionSyntax expr, Dictionary<string, ExpressionSyntax> locals)
                => (ExpressionSyntax)new LocalVariableSubstitutor(locals).Visit(expr);

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
                => _locals.TryGetValue(node.Identifier.Text, out var replacement)
                    ? SyntaxFactory.ParenthesizedExpression(replacement.WithoutTrivia()).WithTriviaFrom(node)
                    : base.VisitIdentifierName(node);
        }
    }
}



