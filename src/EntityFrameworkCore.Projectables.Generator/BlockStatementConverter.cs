using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace EntityFrameworkCore.Projectables.Generator
{
    /// <summary>
    /// Converts block-bodied methods to expression syntax that can be used in expression trees.
    /// Only supports a subset of C# statements.
    /// </summary>
    public class BlockStatementConverter
    {
        private readonly SemanticModel _semanticModel;
        private readonly SourceProductionContext _context;
        private readonly ExpressionSyntaxRewriter _expressionRewriter;
        private readonly Dictionary<string, ExpressionSyntax> _localVariables = new();

        public BlockStatementConverter(SemanticModel semanticModel, SourceProductionContext context, ExpressionSyntaxRewriter expressionRewriter)
        {
            _semanticModel = semanticModel;
            _context = context;
            _expressionRewriter = expressionRewriter;
        }

        /// <summary>
        /// Attempts to convert a block statement into a single expression.
        /// Returns null if the block contains unsupported statements.
        /// </summary>
        public ExpressionSyntax? TryConvertBlock(BlockSyntax block, string memberName)
        {
            if (block == null || block.Statements.Count == 0)
            {
                return null;
            }

            // Try to convert the block statements into an expression
            var result = TryConvertStatements(block.Statements.ToList(), memberName);
            return result;
        }

        private ExpressionSyntax? TryConvertStatements(List<StatementSyntax> statements, string memberName)
        {
            if (statements.Count == 0)
            {
                return null;
            }

            if (statements.Count == 1)
            {
                return TryConvertStatement(statements[0], memberName);
            }

            // Multiple statements - try to convert them into a chain of expressions
            // This is done by converting local variable declarations and then the final return
            var nonReturnStatements = statements.Take(statements.Count - 1).ToList();
            var lastStatement = statements.Last();

            // Process local variable declarations
            foreach (var stmt in nonReturnStatements)
            {
                if (stmt is LocalDeclarationStatementSyntax localDecl)
                {
                    if (!TryProcessLocalDeclaration(localDecl, memberName))
                    {
                        return null;
                    }
                }
                else
                {
                    ReportUnsupportedStatement(stmt, memberName, "Only local variable declarations are supported before the return statement");
                    return null;
                }
            }

            // Convert the final statement (should be a return)
            return TryConvertStatement(lastStatement, memberName);
        }

        private bool TryProcessLocalDeclaration(LocalDeclarationStatementSyntax localDecl, string memberName)
        {
            foreach (var variable in localDecl.Declaration.Variables)
            {
                if (variable.Initializer == null)
                {
                    ReportUnsupportedStatement(localDecl, memberName, "Local variables must have an initializer");
                    return false;
                }

                var variableName = variable.Identifier.Text;
                // Rewrite the initializer expression NOW while it's still in the tree
                var rewrittenInitializer = (ExpressionSyntax)_expressionRewriter.Visit(variable.Initializer.Value);
                _localVariables[variableName] = rewrittenInitializer;
            }

            return true;
        }

        private ExpressionSyntax? TryConvertStatement(StatementSyntax statement, string memberName)
        {
            switch (statement)
            {
                case ReturnStatementSyntax returnStmt:
                    return TryConvertReturnStatement(returnStmt, memberName);

                case IfStatementSyntax ifStmt:
                    return TryConvertIfStatement(ifStmt, memberName);

                case BlockSyntax blockStmt:
                    return TryConvertStatements(blockStmt.Statements.ToList(), memberName);

                case ExpressionStatementSyntax exprStmt:
                    // Expression statements are generally not useful in expression trees
                    ReportUnsupportedStatement(statement, memberName, "Expression statements are not supported");
                    return null;

                case LocalDeclarationStatementSyntax:
                    // Local declarations should be handled before the return statement
                    ReportUnsupportedStatement(statement, memberName, "Local declarations must appear before the return statement");
                    return null;

                default:
                    ReportUnsupportedStatement(statement, memberName, $"Statement type '{statement.GetType().Name}' is not supported");
                    return null;
            }
        }

        private ExpressionSyntax? TryConvertReturnStatement(ReturnStatementSyntax returnStmt, string memberName)
        {
            if (returnStmt.Expression == null)
            {
                ReportUnsupportedStatement(returnStmt, memberName, "Return statement must have an expression");
                return null;
            }

            // First rewrite the return expression
            var expression = (ExpressionSyntax)_expressionRewriter.Visit(returnStmt.Expression);
            
            // Then replace any local variable references with their already-rewritten initializers
            expression = ReplaceLocalVariables(expression);
            
            return expression;
        }

        private ExpressionSyntax? TryConvertIfStatement(IfStatementSyntax ifStmt, string memberName)
        {
            // Convert if-else to conditional (ternary) expression
            // First, rewrite the condition using the expression rewriter
            var condition = (ExpressionSyntax)_expressionRewriter.Visit(ifStmt.Condition);

            var whenTrue = TryConvertStatement(ifStmt.Statement, memberName);
            if (whenTrue == null)
            {
                return null;
            }

            ExpressionSyntax? whenFalse;
            if (ifStmt.Else != null)
            {
                whenFalse = TryConvertStatement(ifStmt.Else.Statement, memberName);
                if (whenFalse == null)
                {
                    return null;
                }
            }
            else
            {
                // If there's no else clause, we can't convert to a ternary
                ReportUnsupportedStatement(ifStmt, memberName, "If statements must have an else clause to be converted to expressions");
                return null;
            }

            // Create a conditional expression with the rewritten nodes
            return SyntaxFactory.ConditionalExpression(
                condition,
                whenTrue,
                whenFalse
            );
        }

        private ExpressionSyntax ReplaceLocalVariables(ExpressionSyntax expression)
        {
            // Use a rewriter to replace local variable references with their initializer expressions
            var rewriter = new LocalVariableReplacer(_localVariables);
            return (ExpressionSyntax)rewriter.Visit(expression);
        }

        private void ReportUnsupportedStatement(StatementSyntax statement, string memberName, string reason)
        {
            var diagnostic = Diagnostic.Create(
                Diagnostics.UnsupportedStatementInBlockBody,
                statement.GetLocation(),
                memberName,
                reason
            );
            _context.ReportDiagnostic(diagnostic);
        }

        private class LocalVariableReplacer : CSharpSyntaxRewriter
        {
            private readonly Dictionary<string, ExpressionSyntax> _localVariables;

            public LocalVariableReplacer(Dictionary<string, ExpressionSyntax> localVariables)
            {
                _localVariables = localVariables;
            }

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            {
                var identifier = node.Identifier.Text;
                if (_localVariables.TryGetValue(identifier, out var replacement))
                {
                    // Replace the identifier with the expression it was initialized with
                    return replacement.WithTriviaFrom(node);
                }

                return base.VisitIdentifierName(node);
            }
        }
    }
}
