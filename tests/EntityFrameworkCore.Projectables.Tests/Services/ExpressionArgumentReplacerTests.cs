using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.Services;
using Xunit;

namespace EntityFrameworkCore.Projectables.Tests.Services
{
    public class ExpressionArgumentReplacerTests
    {
        [Fact]
        public void VisitParameter_MapsParametersWithArguments()
        {
            var parameter = Expression.Parameter(typeof(int));
            var argument = Expression.Constant(1);
            var subject = new ExpressionArgumentReplacer() {
                ParameterArgumentMapping = {
                    { parameter, argument }
                }
            };

            var result = subject.Visit(parameter);

            Assert.Equal(argument, result);
        }
        
    }
}
