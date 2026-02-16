using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntityFrameworkCore.Projectables.Generator
{
    /// <summary>
    /// Converts block-bodied methods to expression syntax that can be used in expression trees.
    /// Only supports a subset of C# statements.
    /// </summary>
    public class BlockStatementConverter
    {
        private readonly SourceProductionContext _context;
        private readonly ExpressionSyntaxRewriter _expressionRewriter;
        private readonly Dictionary<string, ExpressionSyntax> _localVariables = new();

        public BlockStatementConverter(SourceProductionContext context, ExpressionSyntaxRewriter expressionRewriter)
        {
            _context = context;
            _expressionRewriter = expressionRewriter;
        }

        /// <summary>
        /// Attempts to convert a block statement into a single expression.
        /// Returns null if the block contains unsupported statements.
        /// </summary>
        public ExpressionSyntax? TryConvertBlock(BlockSyntax block, string memberName)
        {
            if (block.Statements.Count == 0)
            {
                var diagnostic = Diagnostic.Create(
                    Diagnostics.UnsupportedStatementInBlockBody,
                    block.GetLocation(),
                    memberName,
                    "Block body must contain at least one statement"
                );
                _context.ReportDiagnostic(diagnostic);
                return null;
            }

            // Try to convert the block statements into an expression
            return TryConvertStatements(block.Statements.ToList(), memberName);
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

            // First, process any local variable declarations at the beginning
            var localDeclStatements = new List<LocalDeclarationStatementSyntax>();
            var remainingStatements = new List<StatementSyntax>();
            
            foreach (var stmt in nonReturnStatements)
            {
                if (stmt is LocalDeclarationStatementSyntax localDecl)
                {
                    localDeclStatements.Add(localDecl);
                }
                else
                {
                    remainingStatements.Add(stmt);
                }
            }

            // Process local variable declarations first
            foreach (var localDecl in localDeclStatements)
            {
                if (!TryProcessLocalDeclaration(localDecl, memberName))
                {
                    return null;
                }
            }

            // Check if we have a pattern like multiple if statements without else followed by a final return:
            // var x = ...; if (a) return 1; if (b) return 2; return 3;
            // This can be converted to nested ternaries: a ? 1 : (b ? 2 : 3)
            if (lastStatement is ReturnStatementSyntax finalReturn &&
                remainingStatements.All(s => s is IfStatementSyntax { Else: null }))
            {
                // All remaining non-return statements are if statements without else
                var ifStatements = remainingStatements.Cast<IfStatementSyntax>().ToList();
                
                // Start with the final return as the base expression
                var elseBody = TryConvertReturnStatement(finalReturn, memberName);
                if (elseBody == null)
                {
                    return null;
                }
                
                // Build nested conditionals from right to left (last to first)
                for (var i = ifStatements.Count - 1; i >= 0; i--)
                {
                    var ifStmt = ifStatements[i];
                    var ifBody = TryConvertStatement(ifStmt.Statement, memberName);
                    if (ifBody == null)
                    {
                        return null;
                    }
                    
                    // Rewrite the condition and replace any local variables
                    var condition = (ExpressionSyntax)_expressionRewriter.Visit(ifStmt.Condition);
                    condition = ReplaceLocalVariables(condition);
                    
                    elseBody = SyntaxFactory.ConditionalExpression(condition, ifBody, elseBody);
                }
                
                return elseBody;
            }

            // If there are any remaining non-if statements, try to convert them individually
            // This will provide better error messages for unsupported statements
            if (remainingStatements.Count > 0)
            {
                // Try converting each remaining statement - this will provide specific error messages
                foreach (var stmt in remainingStatements)
                {
                    var converted = TryConvertStatement(stmt, memberName);
                    if (converted == null)
                    {
                        return null;
                    }
                }
                
                // If we got here but had non-if statements, they weren't properly handled
                ReportUnsupportedStatement(remainingStatements[0], memberName, 
                    "Only local variable declarations and if statements without else (with return) are supported before the final return statement");
                return null;
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
                
                // Also expand any previously defined local variables in this initializer
                // This ensures transitive inlining (e.g., var a = 1; var b = a + 2; return b; -> 1 + 2)
                rewrittenInitializer = ReplaceLocalVariables(rewrittenInitializer);
                
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
                    // Prevent locals declared in nested blocks from leaking into outer scopes
                    var nestedLocal = blockStmt.DescendantNodes()
                        .OfType<LocalDeclarationStatementSyntax>()
                        .FirstOrDefault();

                    if (nestedLocal is not null)
                    {
                        ReportUnsupportedStatement(nestedLocal, memberName, "Local declarations in nested blocks are not supported");
                        return null;
                    }
                    
                    return TryConvertStatements(blockStmt.Statements.ToList(), memberName);

                case ExpressionStatementSyntax exprStmt:
                    // Expression statements may contain side effects - analyze them
                    return AnalyzeExpressionStatement(exprStmt, memberName);

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

        private ConditionalExpressionSyntax? TryConvertIfStatement(IfStatementSyntax ifStmt, string memberName)
        {
            // Convert if-else to conditional (ternary) expression
            // First, rewrite the condition using the expression rewriter
            var condition = (ExpressionSyntax)_expressionRewriter.Visit(ifStmt.Condition);
            
            // Then replace any local variable references with their already-rewritten initializers
            condition = ReplaceLocalVariables(condition);

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
            // Replace any local variable references in the switch expression
            switchExpression = ReplaceLocalVariables(switchExpression);
            
            ExpressionSyntax? currentExpression;
            
            // Find default case first
            SwitchSectionSyntax? defaultSection = null;
            var nonDefaultSections = new List<SwitchSectionSyntax>();
            
            foreach (var section in switchStmt.Sections)
            {
                var hasDefault = section.Labels.Any(label => label is DefaultSwitchLabelSyntax);
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
            for (var i = nonDefaultSections.Count - 1; i >= 0; i--)
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
                        // Rewrite and replace locals in case label value
                        var caseLabelValue = (ExpressionSyntax)_expressionRewriter.Visit(caseLabel.Value);
                        caseLabelValue = ReplaceLocalVariables(caseLabelValue);
                        
                        var labelCondition = SyntaxFactory.BinaryExpression(
                            SyntaxKind.EqualsExpression,
                            switchExpression,
                            caseLabelValue
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

            if (statements.Count != 0)
            {
                return TryConvertStatements(statements, memberName);
            }

            // Use the section's first label location for error reporting
            var firstLabel = section.Labels.FirstOrDefault();
            if (firstLabel == null)
            {
                return null;
            }

            var diagnostic = Diagnostic.Create(
                Diagnostics.UnsupportedStatementInBlockBody,
                firstLabel.GetLocation(),
                memberName,
                "Switch section must have at least one statement"
            );
            _context.ReportDiagnostic(diagnostic);
            return null;

        }

        private ExpressionSyntax ReplaceLocalVariables(ExpressionSyntax expression)
        {
            // Use a rewriter to replace local variable references with their initializer expressions
            var rewriter = new LocalVariableReplacer(_localVariables);
            return (ExpressionSyntax)rewriter.Visit(expression);
        }

        private ExpressionSyntax? AnalyzeExpressionStatement(ExpressionStatementSyntax exprStmt, string memberName)
        {
            var expression = exprStmt.Expression;
            
            // Check for specific side effects
            switch (expression)
            {
                case AssignmentExpressionSyntax assignment:
                    ReportSideEffect(assignment, GetAssignmentErrorMessage(assignment));
                    return null;
                    
                case PostfixUnaryExpressionSyntax postfix when 
                    postfix.IsKind(SyntaxKind.PostIncrementExpression) || 
                    postfix.IsKind(SyntaxKind.PostDecrementExpression):
                    ReportSideEffect(postfix, $"Increment/decrement operator '{postfix.OperatorToken.Text}' has side effects and cannot be used in projectable methods");
                    return null;
                    
                case PrefixUnaryExpressionSyntax prefix when 
                    prefix.IsKind(SyntaxKind.PreIncrementExpression) || 
                    prefix.IsKind(SyntaxKind.PreDecrementExpression):
                    ReportSideEffect(prefix, $"Increment/decrement operator '{prefix.OperatorToken.Text}' has side effects and cannot be used in projectable methods");
                    return null;
                    
                case InvocationExpressionSyntax invocation:
                    // Check if this is a potentially impure method call
                    var symbolInfo = _expressionRewriter.GetSemanticModel().GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
                    {
                        // Check if method has [Projectable] attribute - those are safe
                        var hasProjectableAttr = methodSymbol.GetAttributes()
                            .Any(attr => attr.AttributeClass?.Name == "ProjectableAttribute");
                            
                        if (!hasProjectableAttr)
                        {
                            ReportPotentialSideEffect(invocation, 
                                $"Method call '{methodSymbol.Name}' may have side effects. Only calls to methods marked with [Projectable] are guaranteed to be safe in projectable methods");
                            return null;
                        }
                    }
                    break;
            }
            
            // If we got here, it's an expression statement we don't support
            ReportUnsupportedStatement(exprStmt, memberName, "Expression statements are not supported in projectable methods");
            return null;
        }
        
        private string GetAssignmentErrorMessage(AssignmentExpressionSyntax assignment)
        {
            var operatorText = assignment.OperatorToken.Text;
            
            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                if (assignment.Left is MemberAccessExpressionSyntax memberAccess)
                {
                    return $"Property assignment '{memberAccess.Name}' has side effects and cannot be used in projectable methods";
                }
                return $"Assignment operation has side effects and cannot be used in projectable methods";
            }
            else
            {
                // Compound assignment like +=, -=, etc.
                return $"Compound assignment operator '{operatorText}' has side effects and cannot be used in projectable methods";
            }
        }
        
        private void ReportSideEffect(SyntaxNode node, string message)
        {
            var diagnostic = Diagnostic.Create(
                Diagnostics.SideEffectInBlockBody,
                node.GetLocation(),
                message
            );
            _context.ReportDiagnostic(diagnostic);
        }
        
        private void ReportPotentialSideEffect(SyntaxNode node, string message)
        {
            var diagnostic = Diagnostic.Create(
                Diagnostics.PotentialSideEffectInBlockBody,
                node.GetLocation(),
                message
            );
            _context.ReportDiagnostic(diagnostic);
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
