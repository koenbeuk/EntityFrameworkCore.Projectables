using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Projections.Generator.Tests
{
    [UsesVerify]
    public class ProjectionExpressionGeneratorTests
    {
        readonly ITestOutputHelper _testOutputHelper;

        public ProjectionExpressionGeneratorTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public Task EmptyCode_Noop()
        {
            var compilation = CreateCompilation(@"
class C { }
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task SimpleProjectableMethod()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        [Projectable]
        public int Foo() => 1;
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task SimpleProjectableProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        [Projectable]
        public int Foo => 1;
    }
}
");
            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task SimpleProjectableComputedProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable]
        public int Foo => Bar + 1;
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task SimpleProjectableComputedInNestedClassProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        class D {
            public int Bar { get; set; }

            [Projectable]
            public int Foo => Bar + 1;
        }
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task ProjectableComputedPropertyUsingThis()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable]
        public int Foo => this.Bar;
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task ProjectableComputedPropertyMethod()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        public int Bar() => 1;

        [Projectable]
        public int Foo => Bar();
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }


        [Fact]
        public Task MoreComplexProjectableComputedProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable]
        public int Foo => Bar + this.Bar + Bar;
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task ArgumentlessProjectableComputedMethod()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        [Projectable]
        public int Foo() => 0;
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task ProjectableComputedMethodWithSingleArgument()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        [Projectable]
        public int Foo(int i) => i;
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task ProjectableComputedMethodWithMultipleArguments()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        [Projectable]
        public int Foo(int a, string b, object d) => a;
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task ProjectablePropertyToNavigationalProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projections;
namespace Foo {
    class D { }

    class C {
        public System.Collections.Generic.List<D> Dees { get; set; }

        [Projectable]
        public D Foo => Dees.First();
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task ProjectableExtensionMethod()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projections;
namespace Foo {
    class D { }

    static class C {
        [Projectable]
        public static int Foo(this D d) => 1;
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task ProjectableExtensionMethod2()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projections;
namespace Foo {
    static class C {
        [Projectable]
        public static int Foo(this int i) => i;
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task BlockBodiedMember_RaisesDiagnostics()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        [Projectable]
        public int Foo
        {
            get => 1;
        }
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        [Fact]
        public Task BlockBodiedMethod_RaisesDiagnostics()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projections;
namespace Foo {
    class C {
        [Projectable]
        public int Foo()
        {
            return 1;
        }
    }
}
");

            var result = RunGenerator(compilation);
            return Verifier.Verify(result);
        }

        #region Helpers

        Compilation CreateCompilation(string source, bool expectedToCompile = true)
        {
            var references = Basic.Reference.Assemblies.NetStandard20.All.ToList();
            references.Add(MetadataReference.CreateFromFile(typeof(ProjectableAttribute).Assembly.Location));

            var compilation = CSharpCompilation.Create("compilation",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

#if DEBUG
            if (expectedToCompile)
            {
                var compilationDiagnostics = compilation.GetDiagnostics();

                if (!compilationDiagnostics.IsEmpty)
                {
                    _testOutputHelper.WriteLine($"Original compilation diagnostics produced:");

                    foreach (var diagnostic in compilationDiagnostics)
                    {
                        _testOutputHelper.WriteLine($" > " + diagnostic);
                    }

                    if (compilationDiagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
                    {
                        Debug.Fail("Compilation diagnostics produced");
                    }
                }
            }
#endif

            return compilation;
        }

        private GeneratorDriverRunResult RunGenerator(Compilation compilation)
        {
            _testOutputHelper.WriteLine("Running generator and updating compilation...");

            var subject = new ProjectionExpressionGenerator();
            var driver = CSharpGeneratorDriver
                .Create(subject)
                .RunGenerators(compilation);
            return driver.GetRunResult();
        }

        #endregion
    }
}
