﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.Extensions;
using EntityFrameworkCore.Projectables.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Projectables.FunctionalTests.Helpers
{
    public class SampleDbContext<TEntity> : DbContext
        where TEntity : class
    {
        readonly CompatibilityMode _compatibilityMode;
        readonly QueryTrackingBehavior _queryTrackingBehavior;

        public SampleDbContext(CompatibilityMode compatibilityMode = CompatibilityMode.Full, QueryTrackingBehavior queryTrackingBehavior = QueryTrackingBehavior.TrackAll)
        {
            _compatibilityMode = compatibilityMode;
            _queryTrackingBehavior = queryTrackingBehavior;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer("Server=(localdb)\\v11.0;Integrated Security=true"); // Fake connection string as we're actually never connecting
            optionsBuilder.UseProjectables(options => {
                options.CompatibilityMode(_compatibilityMode); // Needed by our ComplexModelTests
            });
            optionsBuilder.UseQueryTrackingBehavior(_queryTrackingBehavior);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TEntity>();
        }
    }
}
