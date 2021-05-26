using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
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

        public void ApplyServices(IServiceCollection services)
        {
            var existingPreprocessorFactoryRegistration = services.FirstOrDefault(x => x.ServiceType == typeof(IQueryTranslationPreprocessorFactory));

            if (existingPreprocessorFactoryRegistration?.ImplementationType is null)
            {
                throw new InvalidOperationException("Expected a QueryTranslationPreprocessor to be registered. Please make sure to register your database provider first");
            }

            // Ensure that we can still resolve this factory
            services.Add(new ServiceDescriptor(existingPreprocessorFactoryRegistration.ImplementationType, existingPreprocessorFactoryRegistration.ImplementationType, existingPreprocessorFactoryRegistration.Lifetime));
            services.Remove(existingPreprocessorFactoryRegistration);

            services.Add(new ServiceDescriptor(
                typeof(IQueryTranslationPreprocessorFactory),
                serviceProvider => new WrappedQueryTranslationPreprocessorFactory((IQueryTranslationPreprocessorFactory)serviceProvider.GetRequiredService(existingPreprocessorFactoryRegistration.ImplementationType), serviceProvider.GetRequiredService<QueryTranslationPreprocessorDependencies>()),
                existingPreprocessorFactoryRegistration.Lifetime
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
