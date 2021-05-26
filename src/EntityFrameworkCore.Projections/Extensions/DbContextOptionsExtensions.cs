using EntityFrameworkCore.Projections.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projections.Extensions
{
    public static class DbContextOptionsExtensions
    {
        public static DbContextOptionsBuilder<TContext> UseProjections<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)UseProjections((DbContextOptionsBuilder)optionsBuilder);

        public static DbContextOptionsBuilder UseProjections(this DbContextOptionsBuilder optionsBuilder)
        {
            var extension = optionsBuilder.Options.FindExtension<ProjectionOptionsExtension>() ?? new ProjectionOptionsExtension();
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

            return optionsBuilder;
        }
    }
}
