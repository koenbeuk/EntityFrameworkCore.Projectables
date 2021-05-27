using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projections.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using ScenarioTests;
using Xunit;

namespace EntityFrameworkCore.Projections.FunctionalTests.ExtensionMethods
{

    public partial class ExtensionMethodTests
    {
        [Scenario(NamingPolicy = ScenarioTestMethodNamingPolicy.Test)]
        public void PlayScenario(ScenarioContext scenario)
        {
            using var dbContext = new SampleDbContext<Entity>();

            scenario.Fact("We can select on a projectable extension method", () => {
                const string expectedQueryString = "SELECT [e].[Id]\r\nFROM [Entity] AS [e]";

                var query = dbContext.Set<Entity>()
                    .Select(x => x.Foo());

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });

        }
    }
}
