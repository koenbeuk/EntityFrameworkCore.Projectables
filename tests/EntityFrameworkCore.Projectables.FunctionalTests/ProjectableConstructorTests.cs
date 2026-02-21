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

            public BaseDto(int id) { Id = id; }
        }

        public class DerivedDto : BaseDto
        {
            public string FullName { get; set; }

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

            [Projectable]
            public PersonOverloadedDto(int id, string firstName, string lastName)
            {
                Id = id;
                FullName = firstName + " " + lastName;
            }

            [Projectable]
            public PersonOverloadedDto(string firstName, string lastName)
            {
                Id = 0;
                FullName = firstName + " " + lastName;
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
    }
}

