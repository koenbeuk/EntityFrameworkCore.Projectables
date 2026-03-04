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
    [Params(1, 10, 50, 100)]
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
        sb.AppendLine("public class Order");
        sb.AppendLine("{");
        sb.AppendLine("    public string FirstName { get; set; } = string.Empty;");
        sb.AppendLine("    public string LastName { get; set; } = string.Empty;");
        sb.AppendLine("    public string? Email { get; set; }");
        sb.AppendLine("    public decimal Amount { get; set; }");
        sb.AppendLine("    public decimal TaxRate { get; set; }");
        sb.AppendLine("    public DateTime? DeletedAt { get; set; }");
        sb.AppendLine("    public bool IsEnabled { get; set; }");
        sb.AppendLine();

        for (int i = 0; i < projectableCount; i++)
        {
            sb.AppendLine("    [Projectable]");
            switch (i % 4)
            {
                case 0:
                    // Computed string property
                    sb.AppendLine($"    public string FullName{i} => FirstName + \" \" + LastName;");
                    break;
                case 1:
                    // Boolean flag property
                    sb.AppendLine($"    public bool IsActive{i} => DeletedAt == null && IsEnabled;");
                    break;
                case 2:
                    // Decimal method with single param
                    sb.AppendLine($"    public decimal TotalWithTax{i}(decimal taxRate) => Amount * (1 + taxRate);");
                    break;
                case 3:
                    // Multi-param method returning string
                    sb.AppendLine($"    public string FormatSummary{i}(string prefix, int count) => prefix + \": \" + FirstName + \" x\" + count;");
                    break;
            }
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
