using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
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
        public Task MinimalProjectableComputedProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable]
        public int Foo => Bar;
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
        public Task ProjectablePropertyWithExplicitExpressionGetter()
        {
            // Tests explicit getter with expression body: { get => expression; }
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable]
        public int Foo { get => 1; }
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ProjectablePropertyWithExplicitBlockGetter()
        {
            // Tests explicit getter with block body: { get { return expression; } }
            // Requires AllowBlockBody = true
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable(AllowBlockBody = true)]
        public int Foo { get { return 1; } }
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ProjectableComputedPropertyWithExplicitExpressionGetter()
        {
            // Tests explicit getter with expression body accessing other properties
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable]
        public int Foo { get => Bar + 1; }
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ProjectableComputedPropertyWithExplicitBlockGetter()
        {
            // Tests explicit getter with block body accessing other properties
            // Requires AllowBlockBody = true
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo { get { return Bar + 1; } }
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ProjectablePropertyWithExplicitBlockGetterUsingThis()
        {
            // Tests explicit getter with block body using 'this' qualifier
            // Requires AllowBlockBody = true
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo { get { return this.Bar; } }
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ProjectablePropertyWithExplicitBlockGetterAndMethodCall()
        {
            // Tests explicit getter with block body calling other methods
            // Requires AllowBlockBody = true
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar() => 1;

        [Projectable(AllowBlockBody = true)]
        public int Foo { get { return Bar(); } }
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public void ProjectablePropertyWithExplicitBlockGetter_WithoutAllowBlockBody_EmitsWarning()
        {
            // Tests that block-bodied property getter without AllowBlockBody = true emits a warning
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable]
        public int Foo { get { return 1; } }
    }
}
");

            var result = RunGenerator(compilation);

            // Should have a warning about experimental feature
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("EFP0001", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
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
        public Task IsOperator()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class A {
        [Projectable] 
        public bool IsB => this is B;
    }
    
    class B : A {
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
        public void BlockBodiedMethod_NoLongerRaisesDiagnostics()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable(AllowBlockBody = true)]
        public int Foo() 
        {
            return 1;
        }
    }
}
");

            var result = RunGenerator(compilation);

            // Block-bodied methods are now supported, so no diagnostics should be raised
            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);
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
        public void NullableMemberBinding_UndefinedSupport_IsBeingReported()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
    static class C {
        [Projectable]
        public static int? GetLength(this string input) => input?.Length;
    }
}
");
            var result = RunGenerator(compilation);

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("EFP0002", diagnostic.Id);
        }


        [Fact]
        public void MultiLevelNullableMemberBinding_UndefinedSupport_IsBeingReported()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
        public record Address
        {
            public int Id { get; set; }
            public string? Country { get; set; }
        }

        public record Party
        {
            public int Id { get; set; }

            public Address? Address { get; set; }
        }

        public record Entity
        {
            public int Id { get; set; }

            public Party? Left { get; set; }
            public Party? Right { get; set; }

            [Projectable]
            public bool IsSameCountry => Left?.Address?.Country == Right?.Address?.Country;
        }
}
");
            var result = RunGenerator(compilation);

            Assert.All(result.Diagnostics, diagnostic => {
                Assert.Equal("EFP0002", diagnostic.Id);
            });
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
        public Task StringInterpolationWithStaticCall_IsBeingRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
    static class MyExtensions {
        public static string ToDateString(this DateTime date) => date.ToString(""dd/MM/yyyy"");
    }

    class C {
        public DateTime? ValidationDate { get; set; }

        [Projectable]
        public string Status => ValidationDate != null ? $""Validation date : ({ValidationDate.Value.ToDateString()})"" : """";
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }
        
        [Fact]
        public Task StringInterpolationWithParenthesis_NoParenthesisAdded()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
    static class MyExtensions {
        public static string ToDateString(this DateTime date) => date.ToString(""dd/MM/yyyy"");
    }

    class C {
        public DateTime? ValidationDate { get; set; }

        [Projectable]
        public string Status => ValidationDate != null ? $""Validation date : ({(ValidationDate.Value.ToDateString())})"" : """";
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
        public Task BooleanSimpleTernary_WithRewriteSupport_IsBeingRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using EntityFrameworkCore.Projectables;

namespace Foo {
    static class C {
        [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
        public static bool Test(this object? x) => x?.Equals(4) == false;
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

        [Fact]
        public Task GenericMethods_AreRewritten()
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

        [Projectable]
        public static string EnforceString<T>(T value) where T : unmanaged
            => value.ToString();
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task GenericClassesWithContraints_AreRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public class TypedObject<TEnum> where TEnum : struct, System.Enum
    {
        public TEnum SomeProp { get; set; }
    }

    public abstract class Entity<T, TEnum>where T : TypedObject<TEnum> where TEnum : struct, System.Enum 
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public T SomeSubobject { get; set; }

        [Projectable]
        public string FullName => $""{FirstName} {LastName} {SomeSubobject.SomeProp}"";
    }
}
");
            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task GenericClassesWithTypeContraints_AreRewritten()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public abstract class Entity<T> where T : notnull
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public T SomeSubobject { get; set; }

        [Projectable]
        public string FullName => $""{FirstName} {LastName} {SomeSubobject}"";
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            // Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task DeclarationTypeNamesAreGettingFullyQualified()
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

            [Projectable]
            public static Entity Something(Entity entity)
                => entity;
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
        public Task MixPrimaryConstructorAndProperties()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public static class EntityExtensions
    {
        public record Entity(int Id)
        {
            public int Id { get; set; }
            public string? FullName { get; set; }

            [Projectable]
            public static Entity Something(Entity entity)
                => new Entity(entity.Id) {
                    FullName = entity.FullName
                };
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
        public Task InheritedMembers()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public class Foo {
        public int Id { get; set; }
    }

    public class Bar : Foo {
        [Projectable]
        public int ProjectedId => Id;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task StaticMembers()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public class Foo {
        public static int Bar { get; set; }

        public int Id { get; set; }
  
        [Projectable]
        public int IdWithBar() => Id + Bar;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task StaticMembers2()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public static class Constants {
        public static readonly int Bar  = 1;
    }

    public class Foo {
        public int Id { get; set; }
  
        [Projectable]
        public int IdWithBar() => Id + Constants.Bar;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }


        [Fact]
        public Task StaticMethodWithNoParameters()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

public static class Foo {
    [Projectable]
    public static int Zero() => 0;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task StaticMethodWithParameters()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

public static class Foo {
    [Projectable]
    public static int Zero(int x) => 0;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ConstMember()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public class Foo {
        public const int Bar = 1;

        public int Id { get; set; }
  
        [Projectable]
        public int IdWithBar() => Id + Bar;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ConstMember2()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public static class Constants {
        public const int Bar  = 1;
    }

    public class Foo {
        public int Id { get; set; }
  
        [Projectable]
        public int IdWithBar() => Id + Constants.Bar;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ConstMember3()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public class Foo {
        public const int Bar = 1;

        public int Id { get; set; }
  
        [Projectable]
        public int IdWithBar() => Id + Foo.Bar;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task RelationalProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using System.Linq;
using System.Collections.Generic;
using EntityFrameworkCore.Projectables;

namespace Foos {
    public class Foo {
        public int Id { get; set; }
    }

    public class Bar {
        public Foo Foo { get; set; }

        [Projectable]
        public int FooId => Foo.Id;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task EnumAccessor()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

public enum SomeFlag
{
    Foo
}

public static class SomeExtensions
{
    [Projectable]
    public static bool Test(this SomeFlag f) => f == SomeFlag.Foo;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task Cast()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

namespace Projectables.Repro;

public class SuperEntity : SomeEntity
{
    public string Superpower { get; set; }
}

public class SomeEntity
{
    public int Id { get; set; }
}

public static class SomeExtensions
{
    [Projectable]
    public static string AsSomeResult(this SomeEntity e) => ((SuperEntity)e).Superpower;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task NavigationProperties()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;
using System.Collections.Generic;

namespace Projectables.Repro;

public class SomeEntity
{
    public int Id { get; set; }

    public SomeEntity Parent { get; set; }

    public ICollection<SomeEntity> Children { get; set; }

    [Projectable]
    public ICollection<SomeEntity> RootChildren =>
        Parent != null ? Parent.RootChildren : Children;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task FooOrBar()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;
using System.Collections.Generic;

namespace Projectables.Repro;

public class SomeEntity
{
    public int Id { get; set; }

    public string Foo { get; set; }

    public string Bar { get; set; }

    [Projectable]
    public string FooOrBar =>
        Foo != null ? Foo : Bar;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task BaseMemberExplicitReference()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

namespace Projectables.Repro;

class Base 
{
    public string Foo { get; set; }
}

class Derived : Base
{
    [Projectable]
    public string Bar => base.Foo;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task BaseMemberImplicitReference()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

namespace Projectables.Repro;

class Base 
{
    public string Foo { get; set; }
}

class Derived : Base
{
    [Projectable]
    public string Bar => Foo;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task BaseMethodExplicitReference()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

namespace Projectables.Repro;

class Base 
{
    public string Foo() => """";
}

class Derived : Base
{
    [Projectable]
    public string Bar => base.Foo();
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task BaseMethorImplicitReference()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

namespace Projectables.Repro;

class Base 
{
    public string Foo() => """";
}

class Derived : Base
{
    [Projectable]
    public string Bar => Foo();
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task DefaultValuesGetRemoved()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

class Foo {
    [Projectable]
    public int Calculate(int i = 0) => i;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ParamsModifiedGetsRemoved()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

class Foo {
    [Projectable]
    public int First(params int[] all) => all[0];
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task RequiredNamespace()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

namespace One {
    static class IntExtensions {
        public static int AddOne(this int i) => i + 1;    
    }
}

namespace One.Two {
    class Bar {
        [Projectable]
        public int Method() => 1.AddOne();
    }   
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task NullConditionalNullCoalesceTypeConversion()
        {
            // issue: https://github.com/koenbeuk/EntityFrameworkCore.Projectables/issues/48

            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

class Foo {
    public int? FancyNumber { get; set; }

    [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
    public static int SomeNumber(Foo fancyClass) => fancyClass?.FancyNumber ?? 3;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task SwitchExpressionWithConstantPattern()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

class Foo {
    public int? FancyNumber { get; set; }

    [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
    public int SomeNumber(int input) => input switch {
            1 => 2,
            3 => 4,
            4 when FancyNumber == 12 => 48,
            _ => 1000,
        };
    }
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task SwitchExpressionWithTypePattern()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

public abstract class Item
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class GroupItem : Item
{
    public string Description { get; set; }
}

public class DocumentItem : Item
{
    public int Priority { get; set; }
}

public abstract record ItemData(int Id, string Name);
public record GroupData(int Id, string Name, string Description) : ItemData(Id, Name);
public record DocumentData(int Id, string Name, int Priority) : ItemData(Id, Name);

public static class ItemMapper
{
    [Projectable]
    public static ItemData ToData(this Item item) =>
        item switch
        {
            GroupItem groupItem => new GroupData(groupItem.Id, groupItem.Name, groupItem.Description),
            DocumentItem documentItem => new DocumentData(documentItem.Id, documentItem.Name, documentItem.Priority),
            _ => null!
        };
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            
            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }
        
        [Fact]
        public Task GenericTypes()
        {
            // issue: https://github.com/koenbeuk/EntityFrameworkCore.Projectables/issues/48

            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

class EntiyBase<TId> {
    [Projectable]
    public static TId GetId() => default;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task GenericTypesWithConstraints()
        {
            // issue: https://github.com/koenbeuk/EntityFrameworkCore.Projectables/issues/48

            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

class EntityBase<TId> where TId : ICloneable, new() {
    [Projectable]
    public static TId GetId() => default;
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }
        
        [Fact]
        public Task DictionaryIndexInitializer_IsBeingRewritten()
        {
            // lang=csharp
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
        public static Dictionary<string, object> ToDictionary(this Entity entity)
            => new Dictionary<string, object> 
            {
                [""FullName""] = entity.FullName ?? ""N/A"",
                [""Id""] = entity.Id.ToString(),
            };
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }
        
        [Fact]
        public Task DictionaryObjectInitializer_PreservesCollectionInitializerSyntax()
        {
            // lang=csharp
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
        public static Dictionary<string, string> ToDictionary(this Entity entity)
            => new Dictionary<string, string> 
            {
                { ""FullName"", entity.FullName ?? ""N/A"" }
            };
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task BlockBodiedMethod_SimpleReturn()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            return 42;
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
        public Task BlockBodiedMethod_WithPropertyAccess()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            return Bar + 10;
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
        public Task BlockBodiedMethod_WithIfElse()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            if (Bar > 10)
            {
                return 1;
            }
            else
            {
                return 0;
            }
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
        public Task BlockBodiedMethod_WithNestedIfElse()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public string Foo()
        {
            if (Bar > 10)
            {
                return ""High"";
            }
            else if (Bar > 5)
            {
                return ""Medium"";
            }
            else
            {
                return ""Low"";
            }
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
        public Task BlockBodiedMethod_WithLocalVariable()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            var temp = Bar * 2;
            return temp + 5;
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
        public Task BlockBodiedMethod_WithTransitiveLocalVariables()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            var a = Bar * 2;
            var b = a + 5;
            return b + 10;
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
        public Task BlockBodiedMethod_LocalInIfCondition()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            var threshold = Bar * 2;
            if (threshold > 10)
            {
                return 1;
            }
            else
            {
                return 0;
            }
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
        public Task BlockBodiedMethod_LocalInSwitchExpression()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public string Foo()
        {
            var value = Bar * 2;
            switch (value)
            {
                case 2:
                    return ""Two"";
                case 4:
                    return ""Four"";
                default:
                    return ""Other"";
            }
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
        public Task BlockBodiedMethod_LocalsInNestedBlock_ProducesDiagnostic()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            if (Bar > 10)
            {
                var temp = Bar * 2;
                return temp;
            }
            return 0;
        }
    }
}
", expectedToCompile: true);

            var result = RunGenerator(compilation);

            // Should have a diagnostic about locals in nested blocks
            Assert.NotEmpty(result.Diagnostics);
            Assert.Contains(result.Diagnostics, d => d.Id == "EFP0003");

            return Verifier.Verify(result.Diagnostics.Select(d => d.ToString()));
        }

        [Fact]
        public Task BlockBodiedMethod_WithMultipleParameters()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable(AllowBlockBody = true)]
        public int Add(int a, int b)
        {
            return a + b;
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
        public Task BlockBodiedMethod_WithIfElseAndCondition()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }
        public bool IsActive { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            if (IsActive && Bar > 0)
            {
                return Bar * 2;
            }
            else
            {
                return 0;
            }
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
        public Task BlockBodiedMethod_IfWithoutElse_UsesDefault()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            if (Bar > 10)
            {
                return 1;
            }
            return 0;
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
        public Task BlockBodiedMethod_IfWithoutElse_ImplicitReturn()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int? Foo()
        {
            if (Bar > 10)
            {
                return 1;
            }
        }
    }
}
", expectedToCompile: false);

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task BlockBodiedMethod_SwitchStatement_Simple()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public string Foo()
        {
            switch (Bar)
            {
                case 1:
                    return ""One"";
                case 2:
                    return ""Two"";
                default:
                    return ""Other"";
            }
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
        public Task BlockBodiedMethod_SwitchStatement_WithMultipleCases()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public string Foo()
        {
            switch (Bar)
            {
                case 1:
                case 2:
                    return ""Low"";
                case 3:
                case 4:
                case 5:
                    return ""Medium"";
                default:
                    return ""High"";
            }
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
        public Task BlockBodiedMethod_SwitchStatement_WithoutDefault()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public string? Foo()
        {
            switch (Bar)
            {
                case 1:
                    return ""One"";
                case 2:
                    return ""Two"";
            }
        }
    }
}
", expectedToCompile: false);

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task BlockBodiedMethod_PropertyAssignment_ReportsError()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            Bar = 10;
            return Bar;
        }
    }
}
", expectedToCompile: true);

            var result = RunGenerator(compilation);

            // Should have a diagnostic about side effects
            Assert.NotEmpty(result.Diagnostics);
            Assert.Contains(result.Diagnostics, d => d.Id == "EFP0004");

            return Verifier.Verify(result.Diagnostics.Select(d => d.ToString()));
        }

        [Fact]
        public Task BlockBodiedMethod_CompoundAssignment_ReportsError()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            Bar += 10;
            return Bar;
        }
    }
}
", expectedToCompile: true);

            var result = RunGenerator(compilation);

            // Should have a diagnostic about side effects
            Assert.NotEmpty(result.Diagnostics);
            Assert.Contains(result.Diagnostics, d => d.Id == "EFP0004");

            return Verifier.Verify(result.Diagnostics.Select(d => d.ToString()));
        }

        [Fact]
        public Task BlockBodiedMethod_IncrementOperator_ReportsError()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            var x = 5;
            x++;
            return x;
        }
    }
}
", expectedToCompile: true);

            var result = RunGenerator(compilation);

            // Should have a diagnostic about side effects
            Assert.NotEmpty(result.Diagnostics);
            Assert.Contains(result.Diagnostics, d => d.Id == "EFP0004");

            return Verifier.Verify(result.Diagnostics.Select(d => d.ToString()));
        }

        [Fact]
        public Task BlockBodiedMethod_NonProjectableMethodCall_ReportsWarning()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(AllowBlockBody = true)]
        public int Foo()
        {
            Console.WriteLine(""test"");
            return Bar;
        }
    }
}
", expectedToCompile: true);

            var result = RunGenerator(compilation);

            // Should have a diagnostic about potential side effects
            Assert.NotEmpty(result.Diagnostics);
            Assert.Contains(result.Diagnostics, d => d.Id == "EFP0005");

            return Verifier.Verify(result.Diagnostics.Select(d => d.ToString()));
        }

        [Fact]
        public Task MethodOverloads_WithDifferentParameterTypes()
        {
            // lang=csharp
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable]
        public int Method(int x) => x;
        
        [Projectable]
        public int Method(string s) => s.Length;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Equal(2, result.GeneratedTrees.Length);
            
            // Verify both overloads are generated with distinct names
            var generatedFiles = result.GeneratedTrees.Select(t => t.FilePath).ToList();
            Assert.Contains(generatedFiles, f => f.Contains("Method_P0_int.g.cs"));
            Assert.Contains(generatedFiles, f => f.Contains("Method_P0_string.g.cs"));

            return Verifier.Verify(result.GeneratedTrees.Select(t => t.ToString()));
        }
        
        [Fact]
        public Task MethodOverloads_WithDifferentParameterCounts()
        {
            // lang=csharp
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable]
        public int Method(int x) => x;
        
        [Projectable]
        public int Method(int x, int y) => x + y;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Equal(2, result.GeneratedTrees.Length);
            
            // Verify both overloads are generated with distinct names
            var generatedFiles = result.GeneratedTrees.Select(t => t.FilePath).ToList();
            Assert.Contains(generatedFiles, f => f.Contains("Method_P0_int.g.cs"));
            Assert.Contains(generatedFiles, f => f.Contains("Method_P0_int_P1_int.g.cs"));

            return Verifier.Verify(result.GeneratedTrees.Select(t => t.ToString()));
        }

#if NET10_0_OR_GREATER
        [Fact]
        public Task ExtensionMemberProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

namespace Foo {
    class Entity { 
        public int Id { get; set; }
    }
    
    static class EntityExtensions {
        extension(Entity e) {
            [Projectable]
            public int DoubleId => e.Id * 2;
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
        public Task ExtensionMemberMethod()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

namespace Foo {
    class Entity { 
        public int Id { get; set; }
    }
    
    static class EntityExtensions {
        extension(Entity e) {
            [Projectable]
            public int TripleId() => e.Id * 3;
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
        public Task ExtensionMemberMethodWithParameters()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

namespace Foo {
    class Entity { 
        public int Id { get; set; }
    }
    
    static class EntityExtensions {
        extension(Entity e) {
            [Projectable]
            public int Multiply(int factor) => e.Id * factor;
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
        public Task ExtensionMemberOnPrimitive()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

static class IntExtensions {
    extension(int i) {
        [Projectable]
        public int Squared => i * i;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ExtensionMemberWithMemberAccess()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

namespace Foo {
    class Entity { 
        public int Id { get; set; }
        public string Name { get; set; }
    }
    
    static class EntityExtensions {
        extension(Entity e) {
            [Projectable]
            public string IdAndName => e.Id + "": "" + e.Name;
        }
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }
#endif

        [Fact]
        public Task ExpandEnumMethodsWithDisplayAttribute()
        {
            var compilation = CreateCompilation(@"
using System;
using System.ComponentModel.DataAnnotations;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public enum CustomEnum
    {
        [Display(Name = ""Value 1"")]
        Value1,
        
        [Display(Name = ""Value 2"")]
        Value2,
    }
    
    public static class EnumExtensions
    {
        public static string GetDisplayName(this CustomEnum value)
        {
            return value.ToString();
        }
    }
    
    public record Entity
    {
        public int Id { get; set; }
        public CustomEnum MyValue { get; set; }
        
        [Projectable(ExpandEnumMethods = true)]
        public string MyEnumName => MyValue.GetDisplayName();
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ExpandEnumMethodsWithNullableEnum()
        {
            var compilation = CreateCompilation(@"
using System;
using System.ComponentModel.DataAnnotations;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public enum CustomEnum
    {
        [Display(Name = ""First Value"")]
        First,
        
        [Display(Name = ""Second Value"")]
        Second,
    }
    
    public static class EnumExtensions
    {
        public static string GetDisplayName(this CustomEnum value)
        {
            return value.ToString();
        }
    }
    
    public record Entity
    {
        public int Id { get; set; }
        public CustomEnum? MyValue { get; set; }
        
        [Projectable(ExpandEnumMethods = true)]
        public string MyEnumName => MyValue.HasValue ? MyValue.Value.GetDisplayName() : null;
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ExpandEnumMethodsWithDescriptionAttribute()
        {
            var compilation = CreateCompilation(@"
using System;
using System.ComponentModel;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public enum Status
    {
        [Description(""The item is pending"")]
        Pending,
        
        [Description(""The item is approved"")]
        Approved,
        
        [Description(""The item is rejected"")]
        Rejected,
    }
    
    public static class EnumExtensions
    {
        public static string GetDescription(this Status value)
        {
            return value.ToString();
        }
    }
    
    public record Entity
    {
        public int Id { get; set; }
        public Status Status { get; set; }
        
        [Projectable(ExpandEnumMethods = true)]
        public string StatusDescription => Status.GetDescription();
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ExpandEnumMethodsOnNavigationProperty()
        {
            var compilation = CreateCompilation(@"
using System;
using System.ComponentModel.DataAnnotations;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public enum OrderStatus
    {
        [Display(Name = ""Pending Review"")]
        Pending,
        
        [Display(Name = ""Approved"")]
        Approved,
    }
    
    public static class EnumExtensions
    {
        public static string GetDisplayName(this OrderStatus value)
        {
            return value.ToString();
        }
    }

    public record Order
    {
        public int Id { get; set; }
        public OrderStatus Status { get; set; }
    }
    
    public record OrderItem
    {
        public int Id { get; set; }
        public Order Order { get; set; }
        
        [Projectable(ExpandEnumMethods = true)]
        public string OrderStatusName => Order.Status.GetDisplayName();
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ExpandEnumMethodsReturningBoolean()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public enum Status
    {
        Pending,
        Approved,
        Rejected,
    }
    
    public static class EnumExtensions
    {
        public static bool IsApproved(this Status value)
        {
            return value == Status.Approved;
        }
    }
    
    public record Entity
    {
        public int Id { get; set; }
        public Status Status { get; set; }
        
        [Projectable(ExpandEnumMethods = true)]
        public bool IsStatusApproved => Status.IsApproved();
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ExpandEnumMethodsReturningInteger()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public enum Priority
    {
        Low,
        Medium,
        High,
    }
    
    public static class EnumExtensions
    {
        public static int GetSortOrder(this Priority value)
        {
            return (int)value;
        }
    }
    
    public record Entity
    {
        public int Id { get; set; }
        public Priority Priority { get; set; }
        
        [Projectable(ExpandEnumMethods = true)]
        public int PrioritySortOrder => Priority.GetSortOrder();
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ExpandEnumMethodsWithParameter()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public enum Status
    {
        Pending,
        Approved,
        Rejected,
    }
    
    public static class EnumExtensions
    {
        public static string GetDisplayNameWithPrefix(this Status value, string prefix)
        {
            return prefix + value.ToString();
        }
    }
    
    public record Entity
    {
        public int Id { get; set; }
        public Status Status { get; set; }
        
        [Projectable(ExpandEnumMethods = true)]
        public string StatusWithPrefix => Status.GetDisplayNameWithPrefix(""Status: "");
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ExpandEnumMethodsWithMultipleParameters()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

namespace Foo {
    public enum Status
    {
        Pending,
        Approved,
        Rejected,
    }
    
    public static class EnumExtensions
    {
        public static string Format(this Status value, string prefix, string suffix)
        {
            return prefix + value.ToString() + suffix;
        }
    }
    
    public record Entity
    {
        public int Id { get; set; }
        public Status Status { get; set; }
        
        [Projectable(ExpandEnumMethods = true)]
        public string FormattedStatus => Status.Format(""["", ""]"");
    }
}
");

            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public void BlockBodiedMethod_WithoutAllowFlag_EmitsWarning()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

namespace Foo {
    class C {
        public int Value { get; set; }
        
        [Projectable]
        public int GetDouble()
        {
            return Value * 2;
        }
    }
}
");
            var result = RunGenerator(compilation);

            // Should have a warning about experimental feature
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("EFP0001", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        }

        [Fact]
        public void BlockBodiedMethod_WithAllowFlag_NoWarning()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;

namespace Foo {
    class C {
        public int Value { get; set; }
        
        [Projectable(AllowBlockBody = true)]
        public int GetDouble()
        {
            return Value * 2;
        }
    }
}
");
            var result = RunGenerator(compilation);

            // Should have no warnings
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public Task ProjectableConstructor_BodyAssignments()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

namespace Foo {
    class PointDto {
        public int X { get; set; }
        public int Y { get; set; }

        [Projectable]
        public PointDto(int x, int y) {
            X = x;
            Y = y;
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
        public Task ProjectableConstructor_WithBaseInitializer()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

namespace Foo {
    class Base {
        public int Id { get; set; }
        public Base(int id) { Id = id; }
    }

    class Child : Base {
        public string Name { get; set; }

        [Projectable]
        public Child(int id, string name) : base(id) {
            Name = name;
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
        public Task ProjectableConstructor_Overloads()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

namespace Foo {
    class PersonDto {
        public string FirstName { get; set; }
        public string LastName { get; set; }

        [Projectable]
        public PersonDto(string firstName, string lastName) {
            FirstName = firstName;
            LastName = lastName;
        }

        [Projectable]
        public PersonDto(string fullName) {
            FirstName = fullName;
            LastName = string.Empty;
        }
    }
}
");
            var result = RunGenerator(compilation);

            Assert.Empty(result.Diagnostics);
            Assert.Equal(2, result.GeneratedTrees.Length);

            return Verifier.Verify(result.GeneratedTrees[0].ToString());
        }

        [Fact]
        public Task ProjectableConstructor_WithClassArgument()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

namespace Foo {
    class SourceEntity {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    class PersonDto {
        public int Id { get; set; }
        public string Name { get; set; }

        [Projectable]
        public PersonDto(SourceEntity source) {
            Id = source.Id;
            Name = source.Name;
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
        public Task ProjectableConstructor_WithMultipleClassArguments()
        {
            var compilation = CreateCompilation(@"
using EntityFrameworkCore.Projectables;

namespace Foo {
    class NamePart {
        public string Value { get; set; }
    }

    class PersonDto {
        public string FirstName { get; set; }
        public string LastName { get; set; }

        [Projectable]
        public PersonDto(NamePart first, NamePart last) {
            FirstName = first.Value;
            LastName = last.Value;
        }
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
