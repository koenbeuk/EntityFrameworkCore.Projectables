using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using VerifyXunit;
using Xunit;

namespace EntityFrameworkCore.Projectables.FunctionalTests
{
    [UsesVerify]
    public class ExpandEnumMethodsTests
    {
        public enum OrderStatus
        {
            [Display(Name = "Pending Review")]
            Pending,
            
            [Display(Name = "Approved")]
            Approved,
            
            [Display(Name = "Rejected")]
            Rejected
        }

        public enum Priority
        {
            [Description("Low Priority")]
            Low,
            
            [Description("Medium Priority")]
            Medium,
            
            [Description("High Priority")]
            High
        }

        public record Order
        {
            public int Id { get; set; }
            public OrderStatus Status { get; set; }
            public Priority? Priority { get; set; }
            public Customer? Customer { get; set; }

            [Projectable(ExpandEnumMethods = true)]
            public string StatusName => Status.GetDisplayName();

            [Projectable(ExpandEnumMethods = true, NullConditionalRewriteSupport = NullConditionalRewriteSupport.Rewrite)]
            public string? PriorityDescription => Priority.HasValue ? Priority.Value.GetDescription() : null;
        }

        public record Customer
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public Priority PreferredPriority { get; set; }
        }

        public record OrderWithNavigation
        {
            public int Id { get; set; }
            public Customer? Customer { get; set; }

            [Projectable(ExpandEnumMethods = true)]
            public string CustomerPriorityDescription => Customer!.PreferredPriority.GetDescription();
        }

        [Fact]
        public Task FilterOnExpandedEnumProperty()
        {
            using var dbContext = new SampleDbContext<Order>();

            var query = dbContext.Set<Order>()
                .Where(x => x.StatusName == "Pending Review");

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task SelectExpandedEnumProperty()
        {
            using var dbContext = new SampleDbContext<Order>();

            var query = dbContext.Set<Order>()
                .Select(x => x.StatusName);

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task OrderByExpandedEnumProperty()
        {
            using var dbContext = new SampleDbContext<Order>();

            var query = dbContext.Set<Order>()
                .OrderBy(x => x.StatusName);

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task SelectNullableEnumExpandedProperty()
        {
            using var dbContext = new SampleDbContext<Order>();

            var query = dbContext.Set<Order>()
                .Select(x => x.PriorityDescription);

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task SelectEnumOnNavigationProperty()
        {
            using var dbContext = new SampleDbContext<OrderWithNavigation>();

            var query = dbContext.Set<OrderWithNavigation>()
                .Select(x => x.CustomerPriorityDescription);

            return Verifier.Verify(query.ToQueryString());
        }
    }

    public static class EnumExtensions
    {
        public static string GetDisplayName<TEnum>(this TEnum value) where TEnum : struct, System.Enum
        {
            var type = value.GetType();
            var memberInfo = type.GetMember(value.ToString())[0];
            var displayAttribute = memberInfo.GetCustomAttributes(typeof(DisplayAttribute), false)
                .OfType<DisplayAttribute>()
                .FirstOrDefault();
            return displayAttribute?.Name ?? value.ToString();
        }

        public static string GetDescription<TEnum>(this TEnum value) where TEnum : struct, System.Enum
        {
            var type = value.GetType();
            var memberInfo = type.GetMember(value.ToString())[0];
            var descriptionAttribute = memberInfo.GetCustomAttributes(typeof(DescriptionAttribute), false)
                .OfType<DescriptionAttribute>()
                .FirstOrDefault();
            return descriptionAttribute?.Description ?? value.ToString();
        }
    }
}
