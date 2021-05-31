using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.Services;
using Xunit;

namespace EntityFrameworkCore.Projectables.Tests.Services
{
    public class ProjectionExpressionClassNameGeneratorTests
    {
        [Theory]
        [InlineData("ns", new string[] { "a" }, "m", "ns_a_m")]
        [InlineData("ns", new string[] { "a", "b" }, "m", "ns_a_b_m")]
        [InlineData(null, new string[] { "a" }, "m", "_a_m")]
        public void GenerateName(string? namespaceName, string[] nestedTypeNames, string memberName, string expected)
        {
            var result = ProjectionExpressionClassNameGenerator.GenerateName(namespaceName, nestedTypeNames, memberName);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void GeneratedFullName()
        {
            var expected = $"{ProjectionExpressionClassNameGenerator.Namespace}.{ProjectionExpressionClassNameGenerator.GenerateName("a", new[] { "b" }, "m")}";
            var result = ProjectionExpressionClassNameGenerator.GenerateFullName("a", new [] { "b" }, "m");

            Assert.Equal(expected, result);
        }
    }
}
