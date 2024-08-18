using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace EntityFrameworkCore.Projectables.Infrastructure.Internal;

public class ProjectablePropertiesNotMappedConvention : IEntityTypeAddedConvention
{
    public void ProcessEntityTypeAdded(IConventionEntityTypeBuilder entityTypeBuilder, IConventionContext<IConventionEntityTypeBuilder> context)
    {
        if (entityTypeBuilder.Metadata.ClrType is null)
        {
            return;
        }

        foreach (var property in entityTypeBuilder.Metadata.ClrType.GetRuntimeProperties())
        {
            if (property.GetCustomAttribute<ProjectableAttribute>() is not null)
            {
                entityTypeBuilder.Ignore(property.Name);
            }
        }
    }
}