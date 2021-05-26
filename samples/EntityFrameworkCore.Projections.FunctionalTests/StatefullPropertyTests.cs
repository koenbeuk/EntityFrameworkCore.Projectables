using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projections.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using ScenarioTests;
using Xunit;

namespace EntityFrameworkCore.Projections.FunctionalTests
{
    public partial class StatefullPropertyTests
    {
        public record Entity
        {
            public int Id { get; set; }
            
            [Projectable]
            public int Computed1 => Id;

            [Projectable]
            public int Computed2 => Id * 2;
        }

        [Scenario(NamingPolicy = ScenarioTestMethodNamingPolicy.Test)]
        public void PlayScenario(ScenarioContext scenario)
        {
            // Setup
            using var dbContext = new SampleDbContext<Entity>(); 

            scenario.Fact("We can filter on a projectable property", () => {
                const string expectedQueryString = "SELECT [e].[Id]\r\nFROM [Entity] AS [e]\r\nWHERE [e].[Id] = 1";

                var query = dbContext.Set<Entity>()
                    .Where(x => x.Computed1 == 1);

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });

            scenario.Fact("We can select on a projectable property", () => {
                const string expectedQueryString = "SELECT [e].[Id]\r\nFROM [Entity] AS [e]";

                var query = dbContext.Set<Entity>()
                    .Select(x => x.Computed1);

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });

            scenario.Fact("We can filter on a more complex projectable property", () => {
                const string expectedQueryString = "SELECT [e].[Id]\r\nFROM [Entity] AS [e]\r\nWHERE ([e].[Id] * 2) = 2";

                var query = dbContext.Set<Entity>()
                    .Where(x => x.Computed2 == 2);

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });

            scenario.Fact("We can select a more complex projectable property", () => {
                const string expectedQueryString = "SELECT [e].[Id] * 2\r\nFROM [Entity] AS [e]";

                var query = dbContext.Set<Entity>()
                    .Select(x => x.Computed2);

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });

            scenario.Fact("We can combine multiple projectable properties", () => {
                const string expectedQueryString = "SELECT [e].[Id] + ([e].[Id] * 2)\r\nFROM [Entity] AS [e]";

                var query = dbContext.Set<Entity>()
                    .Select(x => x.Computed1 + x.Computed2);

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });
        }
    }
}
