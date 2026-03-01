using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Projectables.Generator.Tests;

public abstract class ProjectionExpressionGeneratorTestsBase
{
    protected readonly ITestOutputHelper _testOutputHelper;

    protected ProjectionExpressionGeneratorTestsBase(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    protected Compilation CreateCompilation([StringSyntax("csharp")] string source)
    {
        var references = Basic.Reference.Assemblies.
#if NET10_0
                Net100
#elif NET9_0
                Net90
#elif NET8_0
            Net80
#endif
            .References.All.ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(ProjectableAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create("compilation",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

#if DEBUG
        var compilationDiagnostics = compilation.GetDiagnostics();

        if (!compilationDiagnostics.IsEmpty)
        {
            _testOutputHelper.WriteLine($"Original compilation diagnostics produced:");

            foreach (var diagnostic in compilationDiagnostics)
            {
                _testOutputHelper.WriteLine($" > " + diagnostic.ToString());
            }

            if (compilationDiagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
            {
                Debug.Fail("Compilation diagnostics produced");
            }
        }
#endif

        return compilation;
    }

    protected GeneratorDriverRunResult RunGenerator(Compilation compilation)
    {
        _testOutputHelper.WriteLine("Running generator and updating compilation...");

        var subject = new ProjectionExpressionGenerator();
        var driver = CSharpGeneratorDriver
            .Create(subject)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var result = driver.GetRunResult();

        if (result.Diagnostics.IsEmpty)
        {
            _testOutputHelper.WriteLine("Run did not produce diagnostics");
        }
        else
        {
            _testOutputHelper.WriteLine("Diagnostics produced:");

            foreach (var diagnostic in result.Diagnostics)
            {
                _testOutputHelper.WriteLine(" > " + diagnostic);
            }
        }

        foreach (var newSyntaxTree in result.GeneratedTrees)
        {
            _testOutputHelper.WriteLine($"Produced syntax tree with path produced: {newSyntaxTree.FilePath}");
            _testOutputHelper.WriteLine(newSyntaxTree.GetText().ToString());
        }

        // Verify that the generated code compiles without errors
        var hasGeneratorErrors = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        if (!hasGeneratorErrors && result.GeneratedTrees.Length > 0)
        {
            _testOutputHelper.WriteLine("Checking that generated code compiles...");

            var compilationErrors = outputCompilation
                .GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            if (compilationErrors.Count > 0)
            {
                _testOutputHelper.WriteLine("Generated code produced compilation errors:");
                foreach (var error in compilationErrors)
                {
                    _testOutputHelper.WriteLine(" > " + error);
                }
            }

            Assert.Empty(compilationErrors);
        }

        return result;
    }
}