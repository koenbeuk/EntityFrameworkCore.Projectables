using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace EntityFrameworkCore.Projectables.Infrastructure.Internal;

public class ProjectablePropertiesNotMappedConventionPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.EntityTypeAddedConventions.Add(new ProjectablePropertiesNotMappedConvention());
        return conventionSet;
    }
}
