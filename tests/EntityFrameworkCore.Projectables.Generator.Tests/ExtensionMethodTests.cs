using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Projectables.Generator.Tests;

[UsesVerify]
public class ExtensionMethodTests : ProjectionExpressionGeneratorTestsBase
{
    public ExtensionMethodTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper) { }

    [Fact]
    public Task ProjectableExtensionMethod()
    {
        var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class D { }
    
    static class C {
        [Projectable]
        public static int Foo(this D d) => 1;
    }
}
");

        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }

    [Fact]
    public Task ProjectableExtensionMethod2()
    {
        var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;
namespace Foo {
    static class C {
        [Projectable]
        public static int Foo(this int i) => i;
    }
}
");

        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }

    [Fact]
    public Task ProjectableExtensionMethod3()
    {
        var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;
namespace Foo {
    static class C {
        [Projectable]
        public static int Foo1(this int i) => i;

        [Projectable]
        public static int Foo2(this int i) => i.Foo1();
    }
}
");

        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.GeneratedTrees.Length);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }

    [Fact]
    public Task ProjectableExtensionMethod4()
    {
        var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;
namespace Foo {
    static class C {
        [Projectable]
        public static object Foo1(this object i) => i.Foo1();
    }
}
");

        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }
}