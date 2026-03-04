using System.Text;
using BenchmarkDotNet.Attributes;
using EntityFrameworkCore.Projectables;
using EntityFrameworkCore.Projectables.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace EntityFrameworkCore.Projectables.Benchmarks;

[MemoryDiagnoser]
public class GeneratorBenchmarks
{
    [Params(1, 100, 1000)]
    public int ProjectableCount { get; set; }

    private Compilation _compilation = null!;
    private GeneratorDriver _warmedDriver = null!;
    private Compilation _modifiedCompilation = null!;

    [GlobalSetup]
    public void Setup()
    {
        _compilation = CreateCompilation(ProjectableCount);

        // Warm up the driver once so incremental state is established.
        _warmedDriver = CSharpGeneratorDriver
            .Create(new ProjectionExpressionGenerator())
            .RunGeneratorsAndUpdateCompilation(_compilation, out _, out _);

        // Create a slightly modified compilation that doesn't affect any
        // [Projectable] members — used to exercise the incremental cache path.
        var originalTree = _compilation.SyntaxTrees.First();
        var originalText = originalTree.GetText().ToString();
        var modifiedText = originalText + "\n// bench-edit";
        var modifiedTree = originalTree.WithChangedText(SourceText.From(modifiedText));
        _modifiedCompilation = _compilation.ReplaceSyntaxTree(originalTree, modifiedTree);
    }

    /// <summary>Cold run: a fresh driver is created every iteration.</summary>
    [Benchmark]
    public GeneratorDriver RunGenerator()
    {
        return CSharpGeneratorDriver
            .Create(new ProjectionExpressionGenerator())
            .RunGeneratorsAndUpdateCompilation(_compilation, out _, out _);
    }

    /// <summary>
    /// Warm incremental run: the pre-warmed driver processes a trivial one-line
    /// edit that does not touch any <c>[Projectable]</c> member, exercising the
    /// incremental caching path.
    /// </summary>
    [Benchmark]
    public GeneratorDriver RunGenerator_Incremental()
    {
        return _warmedDriver
            .RunGeneratorsAndUpdateCompilation(_modifiedCompilation, out _, out _);
    }

    private static Compilation CreateCompilation(int projectableCount)
    {
        var source = BuildSource(projectableCount);

        var references = Basic.Reference.Assemblies.
#if NET10_0
                Net100
#else
            Net80
#endif
            .References.All.ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(ProjectableAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            "GeneratorBenchmarkInput",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string BuildSource(int projectableCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using EntityFrameworkCore.Projectables;");
        sb.AppendLine();
        sb.AppendLine("namespace GeneratorBenchmarkInput;");
        sb.AppendLine();

        // Enum used by the switch-expression and is-pattern members.
        sb.AppendLine("public enum OrderStatus { Pending, Active, Completed, Cancelled }");
        sb.AppendLine();

        sb.AppendLine("public class Order");
        sb.AppendLine("{");
        sb.AppendLine("    public string FirstName { get; set; } = string.Empty;");
        sb.AppendLine("    public string LastName { get; set; } = string.Empty;");
        sb.AppendLine("    public string? Email { get; set; }");
        sb.AppendLine("    public decimal Amount { get; set; }");
        sb.AppendLine("    public decimal TaxRate { get; set; }");
        sb.AppendLine("    public DateTime? DeletedAt { get; set; }");
        sb.AppendLine("    public bool IsEnabled { get; set; }");
        sb.AppendLine("    public int Priority { get; set; }");
        sb.AppendLine("    public OrderStatus Status { get; set; }");
        sb.AppendLine();

        // Nine member kinds — one per transformer path in the generator:
        //  0  simple string-concat property          (ExpressionSyntaxRewriter baseline)
        //  1  boolean null-check property            (ExpressionSyntaxRewriter, logical AND)
        //  2  single-param decimal method            (ExpressionSyntaxRewriter, arithmetic)
        //  3  multi-param string method              (ExpressionSyntaxRewriter, concat)
        //  4  null-conditional property              (NullConditionalRewrite, Rewrite mode)
        //  5  switch-expression method               (SwitchExpressionRewrite, relational patterns)
        //  6  is-pattern property                    (VisitIsPatternExpression, not-null pattern)
        //  7  block-bodied if/else chain             (BlockStatementConverter)
        //  8  block-bodied switch with local var     (BlockStatementConverter + local replacement)
        for (int i = 0; i < projectableCount; i++)
        {
            switch (i % 9)
            {
                case 0:
                    // Expression-bodied: simple string concatenation.
                    sb.AppendLine("    [Projectable]");
                    sb.AppendLine($"    public string FullName{i} => FirstName + \" \" + LastName;");
                    break;

                case 1:
                    // Expression-bodied: null check combined with logical AND.
                    sb.AppendLine("    [Projectable]");
                    sb.AppendLine($"    public bool IsActive{i} => DeletedAt == null && IsEnabled;");
                    break;

                case 2:
                    // Expression-bodied: single-param decimal arithmetic.
                    sb.AppendLine("    [Projectable]");
                    sb.AppendLine($"    public decimal TotalWithTax{i}(decimal taxRate) => Amount * (1 + taxRate);");
                    break;

                case 3:
                    // Expression-bodied: multi-param string method.
                    sb.AppendLine("    [Projectable]");
                    sb.AppendLine($"    public string FormatSummary{i}(string prefix, int count) => prefix + \": \" + FirstName + \" x\" + count;");
                    break;

                case 4:
                    // Null-conditional member access — exercises NullConditionalRewrite transformer.
                    sb.AppendLine("    [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]");
                    sb.AppendLine($"    public int? EmailLength{i} => Email?.Length;");
                    break;

                case 5:
                    // Switch expression with relational patterns — exercises SwitchExpressionRewrite transformer.
                    sb.AppendLine("    [Projectable]");
                    sb.AppendLine($"    public string GetGrade{i}(int score) => score switch {{ >= 90 => \"A\", >= 70 => \"B\", _ => \"C\" }};");
                    break;

                case 6:
                    // Is-pattern (not-null) — exercises ExpressionSyntaxRewriter.VisitIsPatternExpression.
                    sb.AppendLine("    [Projectable]");
                    sb.AppendLine($"    public bool HasEmail{i} => Email is not null;");
                    break;

                case 7:
                    // Block-bodied if/else chain — exercises BlockStatementConverter.
                    sb.AppendLine("    [Projectable(AllowBlockBody = true)]");
                    sb.AppendLine($"    public string GetStatusLabel{i}()");
                    sb.AppendLine("    {");
                    sb.AppendLine("        if (DeletedAt != null)");
                    sb.AppendLine("        {");
                    sb.AppendLine("            return \"Deleted\";");
                    sb.AppendLine("        }");
                    sb.AppendLine("        if (IsEnabled)");
                    sb.AppendLine("        {");
                    sb.AppendLine("            return \"Active: \" + FirstName;");
                    sb.AppendLine("        }");
                    sb.AppendLine("        return \"Inactive\";");
                    sb.AppendLine("    }");
                    break;

                case 8:
                    // Block-bodied switch statement with a local variable — exercises
                    // BlockStatementConverter together with local-variable replacement.
                    sb.AppendLine("    [Projectable(AllowBlockBody = true)]");
                    sb.AppendLine($"    public string GetPriorityName{i}()");
                    sb.AppendLine("    {");
                    sb.AppendLine("        var p = Priority;");
                    sb.AppendLine("        switch (p)");
                    sb.AppendLine("        {");
                    sb.AppendLine("            case 1: return \"Low\";");
                    sb.AppendLine("            case 2: return \"Medium\";");
                    sb.AppendLine("            case 3: return \"High\";");
                    sb.AppendLine("            default: return \"Unknown\";");
                    sb.AppendLine("        }");
                    sb.AppendLine("    }");
                    break;
            }

            sb.AppendLine();
        }

        sb.AppendLine("}");
        sb.AppendLine();

        // DTO classes with [Projectable] constructors — exercises the constructor
        // projection path (ProjectableInterpreter constructor handling).
        // Count scales proportionally: roughly one DTO per nine Order members.
        int ctorCount = Math.Max(1, projectableCount / 9);
        for (int j = 0; j < ctorCount; j++)
        {
            sb.AppendLine($"public class OrderSummaryDto{j}");
            sb.AppendLine("{");
            sb.AppendLine("    public string FullName { get; set; } = string.Empty;");
            sb.AppendLine("    public decimal Total { get; set; }");
            sb.AppendLine("    public bool IsActive { get; set; }");
            sb.AppendLine();
            // Parameterless constructor required by [Projectable] constructor support.
            sb.AppendLine($"    public OrderSummaryDto{j}() {{ }}");
            sb.AppendLine();
            sb.AppendLine("    [Projectable]");
            sb.AppendLine($"    public OrderSummaryDto{j}(string firstName, string lastName, decimal amount, decimal taxRate, bool isActive)");
            sb.AppendLine("    {");
            sb.AppendLine("        FullName = firstName + \" \" + lastName;");
            sb.AppendLine("        Total = amount * (1 + taxRate);");
            sb.AppendLine("        IsActive = isActive && amount > 0;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
