using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Projectables.Infrastructure
{
    public class ProjectableOptionsBuilder
    {
        readonly DbContextOptionsBuilder _optionsBuilder;

        public ProjectableOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        {
            _optionsBuilder = optionsBuilder ?? throw new ArgumentNullException(nameof(optionsBuilder));
        }

        public ProjectableOptionsBuilder CompatibilityMode(CompatibilityMode mode)
            => WithOption(x => x.WithCompatibilityMode(mode));

        /// <summary>
        ///     Sets an option by cloning the extension used to store the settings. This ensures the builder
        ///     does not modify options that are already in use elsewhere.
        /// </summary>
        /// <param name="setAction"> An action to set the option. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        protected virtual ProjectableOptionsBuilder WithOption(Func<ProjectionOptionsExtension, ProjectionOptionsExtension> setAction)
        {
            ((IDbContextOptionsBuilderInfrastructure)_optionsBuilder).AddOrUpdateExtension(
                setAction(_optionsBuilder.Options.FindExtension<ProjectionOptionsExtension>() ?? new ProjectionOptionsExtension()));

            return this;
        }
    }
}
