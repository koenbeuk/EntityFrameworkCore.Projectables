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

            // Check if we have a pattern like: if { return x; } return y;
            // This can be converted to: condition ? x : y
            if (nonReturnStatements.Count == 1 && 
                nonReturnStatements[0] is IfStatementSyntax ifWithoutElse && 
                ifWithoutElse.Else == null &&
                lastStatement is ReturnStatementSyntax finalReturn)
            {
                // Convert: if (condition) { return x; } return y;
                // To: condition ? x : y
                var ifBody = TryConvertStatement(ifWithoutElse.Statement, memberName);
                if (ifBody == null)
                {
                    return null;
                }

                var elseBody = TryConvertReturnStatement(finalReturn, memberName);
                if (elseBody == null)
                {
                    return null;
                }

                var condition = (ExpressionSyntax)_expressionRewriter.Visit(ifWithoutElse.Condition);
                return SyntaxFactory.ConditionalExpression(condition, ifBody, elseBody);
            }

            // If we reach here, the pattern was not detected
            // Process local variable declarations before the final return
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

                case SwitchStatementSyntax switchStmt:
                    return TryConvertSwitchStatement(switchStmt, memberName);

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
                // If there's no else clause, use a default literal
                // This will be inferred to the correct type by the compiler
                whenFalse = SyntaxFactory.LiteralExpression(
                    SyntaxKind.DefaultLiteralExpression,
                    SyntaxFactory.Token(SyntaxKind.DefaultKeyword)
                );
            }

            // Create a conditional expression with the rewritten nodes
            return SyntaxFactory.ConditionalExpression(
                condition,
                whenTrue,
                whenFalse
            );
        }

        private ExpressionSyntax? TryConvertSwitchStatement(SwitchStatementSyntax switchStmt, string memberName)
        {
            // Convert switch statement to nested conditional expressions
            // Process sections in reverse order to build from the default case up
            
            var switchExpression = (ExpressionSyntax)_expressionRewriter.Visit(switchStmt.Expression);
            ExpressionSyntax? currentExpression = null;
            
            // Find default case first
            SwitchSectionSyntax? defaultSection = null;
            var nonDefaultSections = new List<SwitchSectionSyntax>();
            
            foreach (var section in switchStmt.Sections)
            {
                bool hasDefault = section.Labels.Any(label => label is DefaultSwitchLabelSyntax);
                if (hasDefault)
                {
                    defaultSection = section;
                }
                else
                {
                    nonDefaultSections.Add(section);
                }
            }
            
            // Start with default case or null
            if (defaultSection != null)
            {
                currentExpression = ConvertSwitchSection(defaultSection, memberName);
                if (currentExpression == null)
                {
                    return null;
                }
            }
            else
            {
                // No default case - use default literal
                currentExpression = SyntaxFactory.LiteralExpression(
                    SyntaxKind.DefaultLiteralExpression,
                    SyntaxFactory.Token(SyntaxKind.DefaultKeyword)
                );
            }
            
            // Process non-default sections in reverse order
            for (int i = nonDefaultSections.Count - 1; i >= 0; i--)
            {
                var section = nonDefaultSections[i];
                var sectionExpression = ConvertSwitchSection(section, memberName);
                if (sectionExpression == null)
                {
                    return null;
                }
                
                // Build condition for all labels in this section (OR'd together)
                ExpressionSyntax? condition = null;
                foreach (var label in section.Labels)
                {
                    if (label is CaseSwitchLabelSyntax caseLabel)
                    {
                        var labelCondition = SyntaxFactory.BinaryExpression(
                            SyntaxKind.EqualsExpression,
                            switchExpression,
                            (ExpressionSyntax)_expressionRewriter.Visit(caseLabel.Value)
                        );
                        
                        condition = condition == null 
                            ? labelCondition
                            : SyntaxFactory.BinaryExpression(
                                SyntaxKind.LogicalOrExpression,
                                condition,
                                labelCondition
                            );
                    }
                    else if (label is not DefaultSwitchLabelSyntax)
                    {
                        // Unsupported label type (e.g., pattern-based switch in older syntax)
                        ReportUnsupportedStatement(switchStmt, memberName, 
                            $"Switch label type '{label.GetType().Name}' is not supported. Use case labels or switch expressions instead.");
                        return null;
                    }
                }
                
                if (condition != null)
                {
                    currentExpression = SyntaxFactory.ConditionalExpression(
                        condition,
                        sectionExpression,
                        currentExpression
                    );
                }
            }
            
            return currentExpression;
        }
        
        private ExpressionSyntax? ConvertSwitchSection(SwitchSectionSyntax section, string memberName)
        {
            // Convert the statements in the switch section
            // Most switch sections end with break, return, or throw
            var statements = section.Statements.ToList();
            
            // Remove trailing break statements as they're not needed in expressions
            if (statements.Count > 0 && statements.Last() is BreakStatementSyntax)
            {
                statements = statements.Take(statements.Count - 1).ToList();
            }
            
            if (statements.Count == 0)
            {
                // Use the section's first label location for error reporting
                var firstLabel = section.Labels.FirstOrDefault();
                if (firstLabel != null)
                {
                    var diagnostic = Diagnostic.Create(
                        Diagnostics.UnsupportedStatementInBlockBody,
                        firstLabel.GetLocation(),
                        memberName,
                        "Switch section must have at least one statement"
                    );
                    _context.ReportDiagnostic(diagnostic);
                }
                return null;
            }
            
            return TryConvertStatements(statements, memberName);
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
