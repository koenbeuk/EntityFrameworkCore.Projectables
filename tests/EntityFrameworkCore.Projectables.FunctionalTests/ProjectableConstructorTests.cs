using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using VerifyXunit;
using Xunit;

#nullable disable

namespace EntityFrameworkCore.Projectables.FunctionalTests
{
    [UsesVerify]
    public class ProjectableConstructorTests
    {
        // ── Entity ──────────────────────────────────────────────────────────────
        public class PersonEntity
        {
            public int Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }

        // ── DTOs ─────────────────────────────────────────────────────────────────

        /// <summary>DTO built from scalar entity fields.</summary>
        public class PersonSummaryDto
        {
            public int Id { get; set; }
            public string FullName { get; set; }

            public PersonSummaryDto() { }   // required: EF Core uses the parameterless ctor

            [Projectable]
            public PersonSummaryDto(int id, string firstName, string lastName)
            {
                Id = id;
                FullName = firstName + " " + lastName;
            }
        }

        /// <summary>DTO built by passing the whole entity instance as the constructor argument.</summary>
        public class PersonFromEntityDto
        {
            public int Id { get; set; }
            public string FullName { get; set; }

            public PersonFromEntityDto() { }  // required: EF Core uses the parameterless ctor

            [Projectable]
            public PersonFromEntityDto(PersonEntity entity)
            {
                Id = entity.Id;
                FullName = entity.FirstName + " " + entity.LastName;
            }
        }

        // ── Base / derived DTO ────────────────────────────────────────────────────

        public class BaseDto
        {
            public int Id { get; set; }

            public BaseDto() { }              // required
            public BaseDto(int id) { Id = id; }
        }

        public class DerivedDto : BaseDto
        {
            public string FullName { get; set; }

            public DerivedDto() { }            // required: EF Core uses the parameterless ctor

            /// <summary>
            /// <c>Id</c> is automatically included from <c>: base(id)</c> — no need to repeat it here.
            /// </summary>
            [Projectable]
            public DerivedDto(int id, string firstName, string lastName) : base(id)
            {
                FullName = firstName + " " + lastName;
            }
        }

        // ── Overloaded constructors ───────────────────────────────────────────────

        public class PersonOverloadedDto
        {
            public int Id { get; set; }
            public string FullName { get; set; }

            public PersonOverloadedDto() { }   // required: EF Core uses the parameterless ctor

            [Projectable]
            public PersonOverloadedDto(int id, string firstName, string lastName)
            {
                Id = id;
                FullName = firstName + " " + lastName;
            }

            [Projectable]
            public PersonOverloadedDto(string firstName, string lastName)
            {
                // Id deliberately not set → should not appear as a DB column in the query
                FullName = firstName + " " + lastName;
            }
        }

        // ── Partial / unmapped property ───────────────────────────────────────────

        /// <summary>
        /// DTO with a <c>Nickname</c> property that is intentionally NOT assigned
        /// in the <c>[Projectable]</c> constructor body.
        /// The generated SQL must NOT include a column for <c>Nickname</c>.
        /// </summary>
        public class PersonPartialDto
        {
            public int Id { get; set; }
            public string FullName { get; set; }
            public string Nickname { get; set; }  // intentionally unmapped in the constructor

            public PersonPartialDto() { }          // required

            [Projectable]
            public PersonPartialDto(int id, string firstName, string lastName)
            {
                Id = id;
                FullName = firstName + " " + lastName;
                // Nickname is intentionally NOT assigned here
            }
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Fact]
        public Task Select_ScalarFieldsToDto()
        {
            using var dbContext = new SampleDbContext<PersonEntity>();

            var query = dbContext.Set<PersonEntity>()
                .Select(p => new PersonSummaryDto(p.Id, p.FirstName, p.LastName));

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task Select_EntityInstanceToDto()
        {
            using var dbContext = new SampleDbContext<PersonEntity>();

            var query = dbContext.Set<PersonEntity>()
                .Select(p => new PersonFromEntityDto(p));

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task Select_DerivedDtoWithBaseConstructor()
        {
            using var dbContext = new SampleDbContext<PersonEntity>();

            var query = dbContext.Set<PersonEntity>()
                .Select(p => new DerivedDto(p.Id, p.FirstName, p.LastName));

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task Select_OverloadedConstructor_WithThreeArgs()
        {
            using var dbContext = new SampleDbContext<PersonEntity>();

            var query = dbContext.Set<PersonEntity>()
                .Select(p => new PersonOverloadedDto(p.Id, p.FirstName, p.LastName));

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task Select_OverloadedConstructor_WithTwoArgs()
        {
            using var dbContext = new SampleDbContext<PersonEntity>();

            var query = dbContext.Set<PersonEntity>()
                .Select(p => new PersonOverloadedDto(p.FirstName, p.LastName));

            return Verifier.Verify(query.ToQueryString());
        }

        /// <summary>
        /// Verifies that a property not assigned in the [Projectable] constructor body
        /// does NOT appear as a column in the generated SQL query.
        /// </summary>
        [Fact]
        public Task Select_UnassignedPropertyNotInQuery()
        {
            using var dbContext = new SampleDbContext<PersonEntity>();

            var query = dbContext.Set<PersonEntity>()
                .Select(p => new PersonPartialDto(p.Id, p.FirstName, p.LastName));

            var sql = query.ToQueryString();

            // Nickname is not assigned in the constructor → must not appear in SQL
            Assert.DoesNotContain("Nickname", sql, System.StringComparison.OrdinalIgnoreCase);

            return Verifier.Verify(sql);
        }
    }
}

