using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Projectables.FunctionalTests;

/// <summary>
/// Validates that closure member evaluation via the reflection switch (FieldInfo/PropertyInfo)
/// produces the same query output as the prior Expression.Compile()-based approach.
///
/// The code under test is in ProjectableExpressionReplacer.VisitMember:
///   var value = node.Member switch {
///       FieldInfo field => field.GetValue(constant.Value),
///       PropertyInfo prop => prop.GetValue(constant.Value),
///       _ => null
///   };
///
/// Scenarios covered:
///   1. Closure capturing a value-type field (int) – FieldInfo branch, non-IQueryable result
///   2. Closure capturing a reference-type field (string) – FieldInfo branch, non-IQueryable result
///   3. Closure capturing two int fields – multiple FieldInfo accesses
///   4. Closure capturing an IQueryable subquery – FieldInfo branch, IQueryable inlining via .Any()
///   5. Closure capturing an IQueryable subquery – IQueryable inlining used in .Count() projection
///   6. Closure capturing both a value-type field and an IQueryable – combined path
/// </summary>
[UsesVerify]
public class ClosureMemberAccessTests
{
    public record Entity
    {
        public int Id { get; set; }
        public string? Name { get; set; }

        [Projectable]
        public bool IsWithinRange(int min, int max) => Id >= min && Id <= max;

        [Projectable]
        public bool HasName(string name) => Name == name;

        [Projectable]
        public int Doubled => Id * 2;
    }

    // -----------------------------------------------------------------------
    // 1. Closure captures a single int field (FieldInfo branch, int value)
    //    The compiler stores `lowerBound` as a field on the generated closure
    //    class.  FieldInfo.GetValue() must return the correct int so that EF
    //    can emit a SQL parameter for it – same result as Expression.Compile().
    // -----------------------------------------------------------------------
    [Fact]
    public Task CapturedIntField_UsedInProjectableMethod()
    {
        using var dbContext = new SampleDbContext<Entity>();

        var lowerBound = 3;
        var query = dbContext.Set<Entity>()
            .Where(x => x.IsWithinRange(lowerBound, 10));

        return Verifier.Verify(query.ToQueryString());
    }

    // -----------------------------------------------------------------------
    // 2. Closure captures a string field (FieldInfo branch, string value)
    //    Ensures the PropertyInfo fallback is not needed for ordinary locals
    //    and that reference-type values are retrieved correctly.
    // -----------------------------------------------------------------------
    [Fact]
    public Task CapturedStringField_UsedInProjectableMethod()
    {
        using var dbContext = new SampleDbContext<Entity>();

        var targetName = "Alice";
        var query = dbContext.Set<Entity>()
            .Where(x => x.HasName(targetName));

        return Verifier.Verify(query.ToQueryString());
    }

    // -----------------------------------------------------------------------
    // 3. Closure captures two int fields (multiple FieldInfo accesses)
    //    Both `lower` and `upper` are read via separate FieldInfo.GetValue()
    //    calls and must arrive intact as SQL parameters.
    // -----------------------------------------------------------------------
    [Fact]
    public Task CapturedMultipleIntFields_UsedInProjectableMethod()
    {
        using var dbContext = new SampleDbContext<Entity>();

        var lower = 2;
        var upper = 8;
        var query = dbContext.Set<Entity>()
            .Where(x => x.IsWithinRange(lower, upper));

        return Verifier.Verify(query.ToQueryString());
    }

    // -----------------------------------------------------------------------
    // 4. Closure captures an IQueryable subquery (FieldInfo branch, IQueryable
    //    result → sub-expression inlining).
    //    When FieldInfo.GetValue() returns an IQueryable that shares the same
    //    provider, ProjectableExpressionReplacer inlines the subquery's
    //    expression tree rather than treating the variable as a parameter.
    //    The projectable [Projectable] method inside the subquery must also be
    //    expanded in the final SQL.
    // -----------------------------------------------------------------------
    [Fact]
    public Task CapturedIQueryableField_SubqueryInlinedViaAny()
    {
        using var dbContext = new SampleDbContext<Entity>();

        var validEntities = dbContext.Set<Entity>()
            .Where(x => x.IsWithinRange(1, 5));

        var query = dbContext.Set<Entity>()
            .Where(x => validEntities.Any(s => s.Id == x.Id));

        return Verifier.Verify(query.ToQueryString());
    }

    // -----------------------------------------------------------------------
    // 5. Closure captures an IQueryable subquery used in a Count() projection.
    //    The captured IQueryable (filtered via a [Projectable] property) is
    //    inlined as a correlated sub-select inside a SELECT projection.
    // -----------------------------------------------------------------------
    [Fact]
    public Task CapturedIQueryableField_SubqueryInlinedViaCount()
    {
        using var dbContext = new SampleDbContext<Entity>();

        var subset = dbContext.Set<Entity>()
            .Where(x => x.Doubled > 4);

        var query = dbContext.Set<Entity>()
            .Select(x => new { x.Id, SubsetCount = subset.Count() });

        return Verifier.Verify(query.ToQueryString());
    }

    // -----------------------------------------------------------------------
    // 6. Closure captures both a value-type field AND an IQueryable.
    //    Exercises both branches in the same expression: the int is read as a
    //    FieldInfo value and turned into a SQL parameter; the IQueryable is
    //    also read as a FieldInfo value and inlined as a sub-expression.
    // -----------------------------------------------------------------------
    [Fact]
    public Task CapturedMixedFields_IntAndIQueryable_BothResolvedCorrectly()
    {
        using var dbContext = new SampleDbContext<Entity>();

        var minCount = 1;
        var highIds = dbContext.Set<Entity>()
            .Where(x => x.IsWithinRange(10, 100));

        var query = dbContext.Set<Entity>()
            .Where(x => x.IsWithinRange(minCount, 50) || highIds.Any(h => h.Id == x.Id));

        return Verifier.Verify(query.ToQueryString());
    }
}


