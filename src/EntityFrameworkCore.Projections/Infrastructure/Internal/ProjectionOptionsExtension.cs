using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projections.Infrastructure.Internal
{
    public class ProjectionOptionsExtension : IDbContextOptionsExtension
    {
        public ProjectionOptionsExtension()
        {
            Info = new ExtensionInfo(this);
        }

        public DbContextOptionsExtensionInfo Info { get; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
        public void ApplyServices(IServiceCollection services)
        {
            var queryCompilerRegistration = services.FirstOrDefault(x => x.ServiceType == typeof(IQueryCompiler));
            if (queryCompilerRegistration?.ImplementationType is null)
            {
                throw new InvalidOperationException("No queryCompiler is configured yet. Please make sure to configure a database provider first"); ;
            }

            // Ensure that we can still resolve this queryCompiler
            services.Add(new ServiceDescriptor(queryCompilerRegistration.ImplementationType, queryCompilerRegistration.ImplementationType, queryCompilerRegistration.Lifetime));
            services.Remove(queryCompilerRegistration);

            services.Add(new ServiceDescriptor(
                typeof(IQueryCompiler),
                serviceProvider => new WrappedQueryCompiler((IQueryCompiler)serviceProvider.GetRequiredService(queryCompilerRegistration.ImplementationType)),
                queryCompilerRegistration.Lifetime
            ));
        }

        public void Validate(IDbContextOptions options)
        {
        }

        sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            public ExtensionInfo(IDbContextOptionsExtension extension) : base(extension)
            {
            }

            public override bool IsDatabaseProvider => false;
            public override string LogFragment => string.Empty;
            public override long GetServiceProviderHashCode() => 0;
            
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
                if (debugInfo == null)
                {
                    throw new ArgumentNullException(nameof(debugInfo));
                }
            }
        }
    }
}
