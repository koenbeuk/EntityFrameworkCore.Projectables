using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

namespace EntityFrameworkCore.Projectables.FunctionalTests
{
    [UsesVerify]
    public class BlockBodiedMethodTests
    {
        public record Entity
        {
            public int Id { get; set; }
            public int Value { get; set; }
            public bool IsActive { get; set; }
            public string? Name { get; set; }
        }

        [Fact]
        public Task SimpleReturn_IsTranslatedToSql()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Select(x => x.GetConstant());

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task ReturnWithPropertyAccess_IsTranslatedToSql()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Select(x => x.GetValuePlusTen());

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task IfElseStatement_IsTranslatedToTernary()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Select(x => x.GetCategory());

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task NestedIfElse_IsTranslatedToNestedTernary()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Select(x => x.GetLevel());

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task LocalVariable_IsInlined()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Select(x => x.CalculateDouble());

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task ComplexConditional_IsTranslatedCorrectly()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Select(x => x.GetAdjustedValue());

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task BlockMethodWithParameters_WorksCorrectly()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Select(x => x.Add(5, 10));

            return Verifier.Verify(query.ToQueryString());
        }
    }

    public static class EntityExtensions
    {
        [Projectable]
        public static int GetConstant(this BlockBodiedMethodTests.Entity entity)
        {
            return 42;
        }

        [Projectable]
        public static int GetValuePlusTen(this BlockBodiedMethodTests.Entity entity)
        {
            return entity.Value + 10;
        }

        [Projectable]
        public static string GetCategory(this BlockBodiedMethodTests.Entity entity)
        {
            if (entity.Value > 100)
            {
                return "High";
            }
            else
            {
                return "Low";
            }
        }

        [Projectable]
        public static string GetLevel(this BlockBodiedMethodTests.Entity entity)
        {
            if (entity.Value > 100)
            {
                return "High";
            }
            else if (entity.Value > 50)
            {
                return "Medium";
            }
            else
            {
                return "Low";
            }
        }

        [Projectable]
        public static int CalculateDouble(this BlockBodiedMethodTests.Entity entity)
        {
            var doubled = entity.Value * 2;
            return doubled + 5;
        }

        [Projectable]
        public static int GetAdjustedValue(this BlockBodiedMethodTests.Entity entity)
        {
            if (entity.IsActive && entity.Value > 0)
            {
                return entity.Value * 2;
            }
            else
            {
                return 0;
            }
        }

        [Projectable]
        public static int Add(this BlockBodiedMethodTests.Entity entity, int a, int b)
        {
            return a + b;
        }
    }
}
