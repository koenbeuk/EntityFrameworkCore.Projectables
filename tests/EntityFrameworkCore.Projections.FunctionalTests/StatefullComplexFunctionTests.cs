using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projections.FunctionalTests.Helpers;
using EntityFrameworkCore.Projections.Services;
using Microsoft.EntityFrameworkCore;
using ScenarioTests;
using Xunit;

namespace EntityFrameworkCore.Projections.FunctionalTests
{
    public partial class StatefullComplexFunctionTests
    {
        public record Entity
        {
            public int Id { get; set; }
            
            [Projectable]
            public int Computed(int argument) => Id + argument; 
        }

        [Scenario(NamingPolicy = ScenarioTestMethodNamingPolicy.Test)]
        public void PlayScenario(ScenarioContext scenario)
        {
            // Setup
            using var dbContext = new SampleDbContext<Entity>(); 

            scenario.Fact("We can filter on a projectable property", () => {
                const string expectedQueryString =
@"DECLARE @__argument_0 int = 1;

SELECT [e].[Id]
FROM [Entity] AS [e]
WHERE ([e].[Id] + @__argument_0) = 2";

                var query = dbContext.Set<Entity>().AsQueryable()
                    .Where(x => x.Computed(1) == 2);

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });

            scenario.Fact("We can select on a projectable property", () => {
                const string expectedQueryString =
@"DECLARE @__argument_0 int = 1;

SELECT [e].[Id] + @__argument_0
FROM [Entity] AS [e]";

                var query = dbContext.Set<Entity>()
                    .AsQueryable()
                    .Select(x => x.Computed(1));

                 Assert.Equal(expectedQueryString, query.ToQueryString());
            });

            scenario.Fact("We can pass in variables", () => {
                const string expectedQueryString =
@"DECLARE @__argument_0 int = 1;

SELECT [e].[Id] + @__argument_0
FROM [Entity] AS [e]";

                var argument = 1;
                var query = dbContext.Set<Entity>()
                    .Select(x => x.Computed(argument));

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });
        }
    }
}
