using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using EntityFrameworkCore.Projectables.Services;
using Microsoft.EntityFrameworkCore;
using ScenarioTests;
using VerifyXunit;
using Xunit;

#nullable disable

namespace EntityFrameworkCore.Projectables.FunctionalTests
{

    [UsesVerify]
    public class InheritedModelTests
    {
        public abstract class Base
        {
            public int Id { get; set; }

            [Projectable]
            public int ComputedProperty => SampleProperty + 1;

            public virtual int SampleProperty => 0;

            [Projectable]
            public int ComputedMethod() => SampleMethod() + 1;

            public virtual int SampleMethod() => 0;
        }

        public class Concrete : Base
        {
            [Projectable]
            public override int SampleProperty => 1;

            [Projectable]
            public override int SampleMethod() => 1;
        }

        public class MoreConcrete : Concrete
        {
        }

        [Fact]
        public Task ProjectOverOverriddenPropertyImplementation()
        {
            using var dbContext = new SampleDbContext<Concrete>();

            var query = dbContext.Set<Concrete>()
                .Select(x => x.ComputedProperty);

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task ProjectOverInheritedPropertyImplementation()
        {
            using var dbContext = new SampleDbContext<Concrete>();

            var query = dbContext.Set<Concrete>()
                .Select(x => x.ComputedProperty);

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task ProjectOverOverriddenMethodImplementation()
        {
            using var dbContext = new SampleDbContext<Concrete>();

            var query = dbContext.Set<Concrete>()
                .Select(x => x.ComputedMethod());

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task ProjectOverInheritedMethodImplementation()
        {
            using var dbContext = new SampleDbContext<Concrete>();

            var query = dbContext.Set<Concrete>()
                .Select(x => x.ComputedMethod());

            return Verifier.Verify(query.ToQueryString());
        }
    }
}
