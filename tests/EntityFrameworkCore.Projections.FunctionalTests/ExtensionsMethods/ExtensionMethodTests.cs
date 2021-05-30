using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projections.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using ScenarioTests;
using VerifyXunit;
using Xunit;

namespace EntityFrameworkCore.Projections.FunctionalTests.ExtensionMethods
{

    [UsesVerify]
    public class ExtensionMethodTests
    {
        [Fact]
        public Task ExtensionOnPrimitive()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Select(x => x.Id.Squared());

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task SelectProjectableExtensionMethod()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Select(x => x.Foo());

            return Verifier.Verify(query.ToQueryString());
        }
    }
}
