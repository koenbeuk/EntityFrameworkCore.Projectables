using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.Extensions;
using Xunit;

namespace EntityFrameworkCore.Projectables.Tests.Extensions
{
    public class TypeExtensionTests
    {
        class InnerType
        {
            public class SubsequentlyInnerType
            {

            }
        }

        [Fact]
        public void GetNestedTypePath_OuterType_Returns1Entry()
        {
            var subject = typeof(TypeExtensionTests);

            var result = subject.GetNestedTypePath();

            Assert.Single(result);
        }

        
        [Fact]
        public void GetNestedTypePath_InnerType_Returns2Entries()
        {
            var subject = typeof(InnerType);

            var result = subject.GetNestedTypePath();

            Assert.Equal(2, result.Count());
        }

        [Fact]
        public void GetNestedTypePath_SubsequentlyInnerType_Returns3Entries()
        {
            var subject = typeof(InnerType.SubsequentlyInnerType);

            var result = subject.GetNestedTypePath();

            Assert.Equal(3, result.Count());
        }

        [Fact]
        public void GetNestedTypePath_SubsequentlyInnerType_ReturnsTypesInOrder()
        {
            var subject = typeof(InnerType.SubsequentlyInnerType);

            var result = subject.GetNestedTypePath();

            Assert.Equal(typeof(TypeExtensionTests), result.First());
            Assert.Equal(typeof(InnerType.SubsequentlyInnerType), result.Last());
        }
    }
}
