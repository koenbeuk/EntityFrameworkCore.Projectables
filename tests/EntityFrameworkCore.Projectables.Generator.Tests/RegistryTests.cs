using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Projectables.Generator.Tests;

public class RegistryTests : ProjectionExpressionGeneratorTestsBase
{
    public RegistryTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper) { }

    [Fact]
    public void NoProjectables_NoRegistry()
    {
        var compilation = CreateCompilation(@"class C { }");
        var result = RunGenerator(compilation);

        Assert.Null(result.RegistryTree);
    }

    [Fact]
    public void SingleProperty_RegistryContainsEntry()
    {
        var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Id { get; set; }
        [Projectable]
        public int IdPlus1 => Id + 1;
    }
}");
        var result = RunGenerator(compilation);

        Assert.NotNull(result.RegistryTree);
        var src = result.RegistryTree!.GetText().ToString();

        Assert.Contains("ProjectionRegistry", src);
        // Uses the compact Register helper — not a repeated block
        Assert.Contains("private static void Register(", src);
        Assert.Contains("Register(map,", src);
        Assert.Contains("GetProperty(\"IdPlus1\"", src);
        Assert.Contains("Foo_C_IdPlus1", src);
    }

    [Fact]
    public void SingleMethod_RegistryContainsEntry()
    {
        var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Id { get; set; }
        [Projectable]
        public int AddDelta(int delta) => Id + delta;
    }
}");
        var result = RunGenerator(compilation);

        Assert.NotNull(result.RegistryTree);
        var src = result.RegistryTree!.GetText().ToString();

        Assert.Contains("GetMethod(\"AddDelta\"", src);
        Assert.Contains("typeof(int)", src);
        Assert.Contains("Foo_C_AddDelta_P0_int", src);
    }

    [Fact]
    public void MultipleProjectables_AllRegistered()
    {
        var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Id { get; set; }
        [Projectable]
        public int IdPlus1 => Id + 1;
        [Projectable]
        public int AddDelta(int delta) => Id + delta;
    }
}");
        var result = RunGenerator(compilation);

        Assert.NotNull(result.RegistryTree);
        var src = result.RegistryTree!.GetText().ToString();

        // Two separate Register(map, ...) calls — one per projectable
        Assert.Contains("GetProperty(\"IdPlus1\"", src);
        Assert.Contains("GetMethod(\"AddDelta\"", src);
        // Each entry is a single line, not a repeated multi-line block
        var registerCallCount = CountOccurrences(src, "Register(map,");
        Assert.Equal(2, registerCallCount);
    }

    [Fact]
    public void GenericClass_NotIncludedInRegistry()
    {
        var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C<T> {
        public int Id { get; set; }
        [Projectable]
        public int IdPlus1 => Id + 1;
    }
}");
        var result = RunGenerator(compilation);

        // Generic class members fall back to reflection — no registry emitted
        Assert.Null(result.RegistryTree);
    }

    [Fact]
    public void Registry_ConstBindingFlagsUsedInBuild()
    {
        var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Id { get; set; }
        [Projectable]
        public int IdPlus1 => Id + 1;
    }
}");
        var result = RunGenerator(compilation);

        Assert.NotNull(result.RegistryTree);
        var src = result.RegistryTree!.GetText().ToString();

        // Build() uses a single const BindingFlags instead of repeating the flags per entry
        Assert.Contains("const BindingFlags allFlags", src);
        Assert.Contains("allFlags", src);
    }

    [Fact]
    public void Registry_RegisterHelperUsesDeclaringTypeAssembly()
    {
        var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Id { get; set; }
        [Projectable]
        public int IdPlus1 => Id + 1;
    }
}");
        var result = RunGenerator(compilation);

        Assert.NotNull(result.RegistryTree);
        var src = result.RegistryTree!.GetText().ToString();

        // Register helper derives the assembly from m.DeclaringType (no typeof repeated per entry)
        Assert.Contains("m.DeclaringType?.Assembly.GetType(exprClass)", src);
    }

    [Fact]
    public void MethodOverloads_BothRegistered()
    {
        var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Id { get; set; }
        [Projectable]
        public int Add(int delta) => Id + delta;
        [Projectable]
        public long Add(long delta) => Id + delta;
    }
}");
        var result = RunGenerator(compilation);

        Assert.NotNull(result.RegistryTree);
        var src = result.RegistryTree!.GetText().ToString();

        // Both overloads registered by parameter-type disambiguation
        Assert.Contains("typeof(int)", src);
        Assert.Contains("typeof(long)", src);
        var registerCallCount = CountOccurrences(src, "Register(map,");
        Assert.Equal(2, registerCallCount);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
