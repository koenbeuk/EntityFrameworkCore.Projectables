using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Projectables.Generator.Tests
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
        public void EmtpyCode_Noop()
        {
            var compilation = CreateCompilation(@"
class C { }
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Empty(result.GeneratedTrees);
        }

        [Fact]
        public Task SimpleProjectableMethod()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable]
        public int Foo() => 1;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task SimpleProjectableProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable]
        public int Foo => 1;
    }
}
");
            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task SimpleProjectableComputedProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable]
        public int Foo => Bar + 1;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task SimpleProjectableComputedInNestedClassProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
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

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ProjectableComputedPropertyUsingThis()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable]
        public int Foo => this.Bar;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ProjectableComputedPropertyMethod()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar() => 1;

        [Projectable]
        public int Foo => Bar();
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }


        [Fact]
        public Task MoreComplexProjectableComputedProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable]
        public int Foo => Bar + this.Bar + Bar;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ArgumentlessProjectableComputedMethod()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable]
        public int Foo() => 0;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ProjectableComputedMethodWithSingleArgument()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable]
        public int Foo(int i) => i;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ProjectableComputedMethodWithMultipleArguments()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable]
        public int Foo(int a, string b, object d) => a;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ProjectablePropertyToNavigationalProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;
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

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task TypesInBodyGetsFullyQualified()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class D { }
    
    class C {
        public System.Collections.Generic.List<D> Dees { get; set; }

        [Projectable]
        public int Foo => Dees.OfType<D>().Count();
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

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

        [Fact]
        public void BlockBodiedMember_RaisesDiagnostics()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
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

            Assert.Single(result.Diagnostics);
        }

        [Fact]
        public void BlockBodiedMethod_RaisesDiagnostics()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
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

            Assert.Single(result.Diagnostics);
        }

        [Fact]
        public Task NullableReferenceTypesAreBeingEliminated()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

#nullable enable

namespace Foo {
    static class C {
        [Projectable]
        public static object? NextFoo(this object? unusedArgument, int? nullablePrimitiveArgument) => null;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task GenericNullableReferenceTypesAreBeingEliminated()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Collections.Generic;
using System.Linq;
using EntityFrameworkCore.Projectables;

#nullable enable

namespace Foo {
    static class C {
        [Projectable]
        public static List<object?> NextFoo(this List<object?> input, List<int?> nullablePrimitiveArgument) => input;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task NullableReferenceTypeCastOperatorGetsEliminated()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Collections.Generic;
using System.Linq;
using EntityFrameworkCore.Projectables;

#nullable enable

namespace Foo {
    static class C {
        [Projectable]
        public static string? NullableReferenceType(object? input) => (string?)input;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task NullableValueCastOperatorsPersist()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Collections.Generic;
using System.Linq;
using EntityFrameworkCore.Projectables;

#nullable enable

namespace Foo {
    static class C {
        [Projectable]        
        public static int? NullableValueType(object? input) => (int?)input;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }


        [Fact]
        public void NullableMemberBinding_WithoutSupport_IsBeingReported()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
    static class C {
        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.None)]
        public static int? GetLength(this string input) => input?.Length;
    }
}
");
            var result = RunGenerator(compilation);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("EFP0002", diagnostic.Id);
        }

        [Fact]
        public Task NullableMemberBinding_WithIgnoreSupport_IsBeingRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
    static class C {
        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)]
        public static int? GetLength(this string input) => input?.Length;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task NullableMemberBinding_WithRewriteSupport_IsBeingRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
    static class C {
        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
        public static int? GetLength(this string input) => input?.Length;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task NullableSimpleElementBinding_WithIgnoreSupport_IsBeingRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
    static class C {
        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)]
        public static char? GetFirst(this string input) => input?[0];
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task NullableSimpleElementBinding_WithRewriteSupport_IsBeingRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
    static class C {
        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
        public static char? GetFirst(this string input) => input?[0];
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }


        [Fact]
        public Task NullableElementBinding_WithIgnoreSupport_IsBeingRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
    static class C {
        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)]
        public static string? GetFirst(this string input) => input?[0].ToString();
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
         public Task NullableElementBinding_WithRewriteSupport_IsBeingRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
    static class C {
        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
        public static string? GetFirst(this string input) => input?[0].ToString();
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task NullableElementAndMemberBinding_WithIgnoreSupport_IsBeingRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public static class EntityExtensions
    {
        public record Entity
        {
            public int Id { get; set; }
            public List<Entity>? RelatedEntities { get; set; }
        }

        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)]
        public static Entity GetFirstRelatedIgnoreNulls(this Entity entity)
            => entity?.RelatedEntities?[0];
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task NullableElementAndMemberBinding_WithRewriteSupport_IsBeingRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public static class EntityExtensions
    {
        public record Entity
        {
            public int Id { get; set; }
            public List<Entity>? RelatedEntities { get; set; }
        }

        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
        public static Entity GetFirstRelatedIgnoreNulls(this Entity entity)
            => entity?.RelatedEntities?[0];
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task NullableParameters_WithRewriteSupport_IsBeingRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public static class EntityExtensions
    {
        public record Entity
        {
            public int Id { get; set; }
            public string? FullName { get; set; }
        }

        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
        public static string GetFirstName(this Entity entity)
            => entity.FullName?.Substring(entity.FullName?.IndexOf(' ') ?? 0);
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
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
                        _testOutputHelper.WriteLine($" > " + diagnostic.ToString());
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

            var result = driver.GetRunResult();

            if (result.Diagnostics.IsEmpty)
            {
                _testOutputHelper.WriteLine("Run did not produce diagnostics");
            }
            else
            {
                _testOutputHelper.WriteLine($"Diagnostics produced:");

                foreach (var diagnostic in result.Diagnostics)
                {
                    _testOutputHelper.WriteLine($" > " + diagnostic.ToString());
                }
            }

            foreach (var newSyntaxTree in result.GeneratedTrees)
            {
                _testOutputHelper.WriteLine($"Produced syntax tree with path produced: {newSyntaxTree.FilePath}");
                _testOutputHelper.WriteLine(newSyntaxTree.GetText().ToString());
            }

            return driver.GetRunResult();
        }

        #endregion
    }
}
