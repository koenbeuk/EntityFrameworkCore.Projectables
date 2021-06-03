using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables.Infrastructure.Internal
{
    public class ProjectionOptionsExtension : IDbContextOptionsExtension
    {
        CompatibilityMode _compatibilityMode = CompatibilityMode.Limited;

        public ProjectionOptionsExtension()
        {
            Info = new ExtensionInfo(this);
        }

        public ProjectionOptionsExtension(ProjectionOptionsExtension copyFrom)
            : this()
        {
            _compatibilityMode = copyFrom._compatibilityMode;
        }

        protected ProjectionOptionsExtension Clone() => new(this);

        public DbContextOptionsExtensionInfo Info { get; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
        public void ApplyServices(IServiceCollection services)
        {
            static object CreateTargetInstance(IServiceProvider services, ServiceDescriptor descriptor)
            {
                if (descriptor.ImplementationInstance is not null)
                    return descriptor.ImplementationInstance;

                if (descriptor.ImplementationFactory is not null)
                    return descriptor.ImplementationFactory(services);

                Debug.Assert(descriptor.ImplementationType is not null);

                return ActivatorUtilities.GetServiceOrCreateInstance(services, descriptor.ImplementationType!);
            }

            if (_compatibilityMode is CompatibilityMode.Full)
            {
                var targetDescriptor = services.FirstOrDefault(x => x.ServiceType == typeof(IQueryCompiler));
                if (targetDescriptor is null)
                {
                    throw new InvalidOperationException("No QueryProvider is configured yet. Please make sure to configure a database provider first"); ;
                }

                var decoratorObjectFactory = ActivatorUtilities.CreateFactory(typeof(CustomQueryProvider), new[] { targetDescriptor.ServiceType });

                services.Replace(ServiceDescriptor.Describe(
                    targetDescriptor.ServiceType,
                    serviceProvider => decoratorObjectFactory(serviceProvider, new[] { CreateTargetInstance(serviceProvider, targetDescriptor) }),
                    targetDescriptor.Lifetime
                ));
            }
            else
            {
                var targetDescriptor = services.FirstOrDefault(x => x.ServiceType == typeof(IQueryTranslationPreprocessorFactory));
                if (targetDescriptor is null)
                {
                    throw new InvalidOperationException("No QueryTranslationPreprocessorFactory is configured yet. Please make sure to configure a database provider first"); ;
                }

                var decoratorObjectFactory = ActivatorUtilities.CreateFactory(typeof(CustomQueryTranslationPreprocessorFactory), new[] { targetDescriptor.ServiceType });

                services.Replace(ServiceDescriptor.Describe(
                    targetDescriptor.ServiceType,
                    serviceProvider => decoratorObjectFactory(serviceProvider, new[] { CreateTargetInstance(serviceProvider, targetDescriptor) }),
                    targetDescriptor.Lifetime
                ));
            }
        }

        public ProjectionOptionsExtension WithCompatibilityMode(CompatibilityMode compatibilityMode)
        {
            var clone = Clone();

            clone._compatibilityMode = compatibilityMode;

            return clone;
        }

        public void Validate(IDbContextOptions options)
        {
        }

        sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            private int? _serviceProviderHash;

            public ExtensionInfo(IDbContextOptionsExtension extension) : base(extension)
            {
            }

            public override bool IsDatabaseProvider => false;
            public override string LogFragment => string.Empty;
            public override long GetServiceProviderHashCode()
            {
                if (_serviceProviderHash == null)
                {
                    var hashCode = nameof(ProjectionOptionsExtension).GetHashCode();

                    var extension = (ProjectionOptionsExtension)Extension;

                    hashCode ^= extension._compatibilityMode.GetHashCode();

                    _serviceProviderHash = hashCode;
                }

                return _serviceProviderHash.Value;
            }
            
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
                if (debugInfo == null)
                {
                    throw new ArgumentNullException(nameof(debugInfo));
                }

                debugInfo["Projectables:CompatibilityMode"] = ((ProjectionOptionsExtension)Extension)._compatibilityMode.ToString();
            }
        }
    }
}
