using EntityFrameworkCore.Projectables.Infrastructure;
using EntityFrameworkCore.Projectables.Infrastructure.Internal;
using EntityFrameworkCore.Projectables.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public static class DbContextOptionsExtensions
    {
        public static DbContextOptionsBuilder<TContext> UseProjectables<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder, Action<ProjectableOptionsBuilder>? configure = null)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)UseProjectables((DbContextOptionsBuilder)optionsBuilder, configure);

        public static DbContextOptionsBuilder UseProjectables(this DbContextOptionsBuilder optionsBuilder, Action<ProjectableOptionsBuilder>? configure = null)
        {
            var extension = optionsBuilder.Options.FindExtension<ProjectionOptionsExtension>() ?? new ProjectionOptionsExtension();
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

            configure?.Invoke(new ProjectableOptionsBuilder(optionsBuilder));

            return optionsBuilder;
        }
    }
}
