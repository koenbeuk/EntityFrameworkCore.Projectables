using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projections.FunctionalTests.Helpers;
using EntityFrameworkCore.Projections.Services;
using Microsoft.EntityFrameworkCore;
using ScenarioTests;
using Xunit;

#nullable disable

namespace EntityFrameworkCore.Projections.FunctionalTests
{
    
    public partial class ComplexModelTests
    {
        public class User
        {
            public int Id { get; set; }

            public string DisplayName { get; set; }

            public ICollection<Order> Orders { get; set; }

            // todo: since Order is a nested class, we currently have to fully express the location of this class
            [Projectable]
            public EntityFrameworkCore.Projections.FunctionalTests.ComplexModelTests.Order LastOrder =>
                Orders.OrderByDescending(x => x.RecordDate).FirstOrDefault();

            // todo: since Order is a nested class, we currently have to fully express the location of this class
            [Projectable]
            [NotMapped]
            public IEnumerable<EntityFrameworkCore.Projections.FunctionalTests.ComplexModelTests.Order> Last2Orders =>
                Orders.OrderByDescending(x => x.RecordDate).Take(2);

        }

        public class Order
        {
            public int Id { get; set; }

            public DateTime RecordDate { get; set; }
        } 

        [Scenario(NamingPolicy = ScenarioTestMethodNamingPolicy.Test)]
        public void PlayScenario(ScenarioContext scenario)
        {
            using var dbContext = new SampleDbContext<User>();

            scenario.Fact("We can project over a projectable navigation property", () => {
                const string expectedQueryString = 
@"SELECT (
    SELECT TOP(1) [o].[RecordDate]
    FROM [Order] AS [o]
    WHERE [u].[Id] = [o].[UserId]
    ORDER BY [o].[RecordDate] DESC)
FROM [User] AS [u]";

                var query = dbContext.Set<User>()
                    .Select(x => x.LastOrder.RecordDate);

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });

            scenario.Fact("We can project over a projectable navigation collection property", () => {
                const string expectedQueryString = 
@"SELECT [t0].[RecordDate]
FROM [User] AS [u]
INNER JOIN (
    SELECT [t].[RecordDate], [t].[UserId]
    FROM (
        SELECT [o].[RecordDate], [o].[UserId], ROW_NUMBER() OVER(PARTITION BY [o].[UserId] ORDER BY [o].[RecordDate] DESC) AS [row]
        FROM [Order] AS [o]
    ) AS [t]
    WHERE [t].[row] <= 2
) AS [t0] ON [u].[Id] = [t0].[UserId]";

                var query = dbContext.Set<User>()
                    .SelectMany(x => x.Last2Orders)
                    .Select(x => x.RecordDate);

                Assert.Equal(expectedQueryString, query.ToQueryString());
            });
        }
    }
}
