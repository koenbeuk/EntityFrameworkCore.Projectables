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
    public partial class StatelessPropertyTests
    {
        public record Entity
        {
            public int Id { get; set; }
            
            [Projectable]
            public int Computed => 0; 
        }

        [Scenario(NamingPolicy = ScenarioTestMethodNamingPolicy.Test)]
        public void PlayScenario(ScenarioContext scenario)
        {
            // Setup
            using var dbContext = new SampleDbContext<Entity>(); 

            scenario.Fact("We can filter on a projectable property", () => {
                const string expectedQueryString = "SELECT [e].[Id]\r\nFROM [Entity] AS [e]\r\nWHERE 0 = 1";

                var query = dbContext.Set<Entity>()
                    .Where(x => x.Computed == 1);

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });

            scenario.Fact("We can select on a projectable property", () => {
                const string expectedQueryString = "SELECT 0\r\nFROM [Entity] AS [e]";

                var query = dbContext.Set<Entity>()
                    .Select(x => x.Computed);

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });
        }
    }
}
