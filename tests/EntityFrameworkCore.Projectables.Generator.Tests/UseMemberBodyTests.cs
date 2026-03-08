using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Projectables.Generator.Tests;

/// <summary>
/// Tests for [Projectable(UseMemberBody = ...)] — both valid cases (code is generated correctly)
/// and invalid cases (EFP0010 / EFP0011 diagnostics are emitted).
/// </summary>
[UsesVerify]
public class UseMemberBodyTests : ProjectionExpressionGeneratorTestsBase
{
    public UseMemberBodyTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper) { }

    // ── Valid cases ────────────────────────────────────────────────────────────

    [Fact]
    public Task Method_UsesMethodBody_SameReturnType()
    {
        var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Bar { get; set; }

        [Projectable(UseMemberBody = nameof(ComputedImpl))]
        public int GetComputed() => Bar;

        private int ComputedImpl() => Bar * 2;
    }
}
");
        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }

    [Fact]
    public Task Method_UsesExpressionPropertyBody_StaticExtension()
    {
        var compilation = CreateCompilation(@"
using System;
using System.Linq.Expressions;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class Entity { public string Name { get; set; } }

    static class EntityExtensions {
        [Projectable(UseMemberBody = nameof(NameEqualsExpr))]
        public static bool NameEquals(this Entity a, Entity b) => a.Name == b.Name;

        private static Expression<Func<Entity, Entity, bool>> NameEqualsExpr =>
            (a, b) => a.Name == b.Name;
    }
}
");
        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }

    [Fact]
    public Task Method_UsesExpressionPropertyBody_InstanceMethod()
    {
        var compilation = CreateCompilation(@"
using System;
using System.Linq.Expressions;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Value { get; set; }

        [Projectable(UseMemberBody = nameof(IsPositiveExpr))]
        public bool IsPositive() => Value > 0;

        private static Expression<Func<C, bool>> IsPositiveExpr => @this => @this.Value > 0;
    }
}
");
        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }

    [Fact]
    public Task Method_UsesExpressionPropertyBody_BlockBodiedGetterLambda()
    {
        var compilation = CreateCompilation(@"
using System;
using System.Linq.Expressions;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Value { get; set; }

        [Projectable(UseMemberBody = nameof(IsPositiveExpr))]
        public bool IsPositive() => Value > 0;

        private static Expression<Func<C, bool>> IsPositiveExpr {
            get { return @this => @this.Value > 0; }
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
    public Task Method_UsesExpressionPropertyBody_StoredField_ExpressionBodied()
    {
        // Expression-bodied property that returns a stored private field.
        // The generator follows the field reference to inline the underlying lambda body,
        // so no code is emitted that would reference the private field from the generated class.
        var compilation = CreateCompilation(@"
using System;
using System.Linq.Expressions;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Value { get; set; }

        [Projectable(UseMemberBody = nameof(IsPositiveExpr))]
        public bool IsPositive() => Value > 0;

        private static Expression<Func<C, bool>> IsPositiveExpr => _isPositiveExpr;
        private static readonly Expression<Func<C, bool>> _isPositiveExpr = @this => @this.Value > 0;
    }
}
");
        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }

    [Fact]
    public Task Method_UsesExpressionPropertyBody_StoredField_GetterExpressionBodied()
    {
        // get => storedField: the generator follows the reference into the field initializer.
        var compilation = CreateCompilation(@"
using System;
using System.Linq.Expressions;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Value { get; set; }

        [Projectable(UseMemberBody = nameof(IsPositiveExpr))]
        public bool IsPositive() => Value > 0;

        private static Expression<Func<C, bool>> IsPositiveExpr { get => _isPositiveExpr; }
        private static readonly Expression<Func<C, bool>> _isPositiveExpr = @this => @this.Value > 0;
    }
}
");
        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }

    [Fact]
    public Task Method_UsesExpressionPropertyBody_StoredField_BlockBodiedGetter()
    {
        // get { return storedField; }: block-bodied getter returning a stored private field.
        var compilation = CreateCompilation(@"
using System;
using System.Linq.Expressions;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Value { get; set; }

        [Projectable(UseMemberBody = nameof(IsPositiveExpr))]
        public bool IsPositive() => Value > 0;

        private static Expression<Func<C, bool>> IsPositiveExpr {
            get { return _isPositiveExpr; }
        }
        private static readonly Expression<Func<C, bool>> _isPositiveExpr = @this => @this.Value > 0;
    }
}
");
        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }

    [Fact]
    public Task Property_UsesPropertyBody_SameType()
    {
        var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Id { get; set; }

        [Projectable(UseMemberBody = nameof(IdDoubled))]
        public int Computed => Id;

        private int IdDoubled => Id * 2;
    }
}
");
        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }

    [Fact]
    public Task StaticMethod_UsesStaticMethodBody()
    {
        var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable(UseMemberBody = nameof(AddImpl))]
        public static int Add(int a, int b) => a + b;

        private static int AddImpl(int a, int b) => a + b + 1;
    }
}
");
        var result = RunGenerator(compilation);

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);

        return Verifier.Verify(result.GeneratedTrees[0].ToString());
    }

    // ── Invalid cases ──────────────────────────────────────────────────────────

    [Fact]
    public void UseMemberBody_MemberNotFound_EmitsEFP0010()
    {
        var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable(UseMemberBody = ""DoesNotExist"")]
        public int Foo() => 1;
    }
}
");
        var result = RunGenerator(compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("EFP0010", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void UseMemberBody_MemberNotFound_OnProperty_EmitsEFP0010()
    {
        var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Id { get; set; }

        [Projectable(UseMemberBody = ""NoSuchMember"")]
        public int Computed => Id;
    }
}
");
        var result = RunGenerator(compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("EFP0010", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void UseMemberBody_IncompatibleReturnType_EmitsEFP0011()
    {
        var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable(UseMemberBody = nameof(WrongType))]
        public int Foo() => 1;

        // Different return type — incompatible
        private string WrongType() => ""hello"";
    }
}
");
        var result = RunGenerator(compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("EFP0011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void UseMemberBody_IncompatiblePropertyType_EmitsEFP0011()
    {
        var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        public int Id { get; set; }

        [Projectable(UseMemberBody = nameof(WrongProp))]
        public int Computed => Id;

        // Different type — incompatible
        private string WrongProp => ""x"";
    }
}
");
        var result = RunGenerator(compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("EFP0011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void UseMemberBody_MethodPointsToField_EmitsEFP0011()
    {
        var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        private int _field = 42;

        [Projectable(UseMemberBody = ""_field"")]
        public int Foo() => 1;
    }
}
");
        var result = RunGenerator(compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("EFP0011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void UseMemberBody_ExtensionMethod_MemberNotFound_EmitsEFP0010()
    {
        var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class Entity { public string Name { get; set; } }

    static class EntityExtensions {
        [Projectable(UseMemberBody = ""NoSuchExpr"")]
        public static bool NameEquals(this Entity a, Entity b) => a.Name == b.Name;
    }
}
");
        var result = RunGenerator(compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("EFP0010", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void UseMemberBody_IncompatibleParameterCount_EmitsEFP0011()
    {
        var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        // Two parameters
        [Projectable(UseMemberBody = nameof(WrongParams))]
        public int Add(int a, int b) => a + b;

        // Same return type but only one parameter — count mismatch
        private int WrongParams(int a) => a * 2;
    }
}
");
        var result = RunGenerator(compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("EFP0011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void UseMemberBody_IncompatibleParameterTypes_EmitsEFP0011()
    {
        var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class C {
        [Projectable(UseMemberBody = nameof(WrongParamTypes))]
        public int Add(int a, int b) => a + b;

        // Same return type and parameter count but different parameter types
        private int WrongParamTypes(int a, string b) => a;
    }
}
");
        var result = RunGenerator(compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("EFP0011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void UseMemberBody_ExtensionMethod_IncompatibleExpressionProperty_EmitsEFP0011()
    {
        var compilation = CreateCompilation(@"
using System;
using System.Linq.Expressions;
using EntityFrameworkCore.Projectables;
namespace Foo {
    class Entity { public string Name { get; set; } }

    static class EntityExtensions {
        [Projectable(UseMemberBody = nameof(WrongSignatureExpr))]
        public static bool NameEquals(this Entity a, Entity b) => a.Name == b.Name;

        // Wrong signature: Func<Entity, bool> instead of Func<Entity, Entity, bool>
        private static Expression<Func<Entity, bool>> WrongSignatureExpr => a => a.Name == null;
    }
}
");
        var result = RunGenerator(compilation);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("EFP0011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }
}

