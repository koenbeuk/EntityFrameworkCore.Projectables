using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace EntityFrameworkCore.Projectables.FunctionalTests;

[UsesVerify]
public class DbFunctionTests 
{
    public record TestEntity
    {
        public int Id { get; set; }

        [Projectable]
        public int Sample1() => DbFunctions.Function1();

        [Projectable]
        public int Sample2() => DbFunctions.Function2(Id);

        [Projectable]
        public int Sample3(int arg) => DbFunctions.Function2(arg);
    }

    public static class DbFunctions
    {
        public static int Function1() => throw new NotSupportedException();

        public static int Function2(int a) => throw new NotSupportedException();
    }

    public class DbFunctionSampleDbContext : SampleDbContext<TestEntity>
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDbFunction(typeof(DbFunctions).GetMethod(nameof(DbFunctions.Function1))!)
                .HasName(nameof(DbFunctions.Function1));

            modelBuilder.HasDbFunction(typeof(DbFunctions).GetMethod(nameof(DbFunctions.Function2))!)
                .HasName(nameof(DbFunctions.Function2));

            base.OnModelCreating(modelBuilder);
        }
    }

    [Fact]
    public Task Function1_IsInlined()
    {
        var dbContext = new DbFunctionSampleDbContext();
        var query = dbContext.Set<TestEntity>().Select(x => x.Sample1());

        return Verifier.Verify(query.ToQueryString());
    }

    [Fact]
    public Task Function2_IsInlined()
    {
        var dbContext = new DbFunctionSampleDbContext();
        var query = dbContext.Set<TestEntity>().Select(x => x.Sample2());

        return Verifier.Verify(query.ToQueryString());
    }

    [Fact]
    public Task Function2_WithExplicitArg_IsInlined()
    {
        var dbContext = new DbFunctionSampleDbContext();
        var arg = 1;
        var query = dbContext.Set<TestEntity>().Select(x => x.Sample3(arg));

        return Verifier.Verify(query.ToQueryString());
    }
}
