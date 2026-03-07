using Microsoft.CodeAnalysis;

namespace EntityFrameworkCore.Projectables.Generator;

/// <summary>
/// Plain-data snapshot of the [Projectable] attribute arguments.
/// </summary>
public readonly record struct ProjectableAttributeData
{
    public NullConditionalRewriteSupport NullConditionalRewriteSupport { get; }
    public string? UseMemberBody { get; }
    public bool ExpandEnumMethods { get; }
    public bool AllowBlockBody { get; }
    
    public ProjectableAttributeData(AttributeData attribute)
    {
        NullConditionalRewriteSupport = attribute.NamedArguments
            .Where(x => x.Key == "NullConditionalRewriteSupport")
            .Where(x => x.Value.Kind == TypedConstantKind.Enum)
            .Select(x => x.Value.Value)
            .Where(x => Enum.IsDefined(typeof(NullConditionalRewriteSupport), x))
            .Cast<NullConditionalRewriteSupport>()
            .FirstOrDefault();

        UseMemberBody = attribute.NamedArguments
            .Where(x => x.Key == "UseMemberBody")
            .Select(x => x.Value.Value)
            .OfType<string?>()
            .FirstOrDefault();

        ExpandEnumMethods = attribute.NamedArguments
            .Where(x => x.Key == "ExpandEnumMethods")
            .Select(x => x.Value.Value is bool b && b)
            .FirstOrDefault();

        AllowBlockBody = attribute.NamedArguments
            .Where(x => x.Key == "AllowBlockBody")
            .Select(x => x.Value.Value is bool b && b)
            .FirstOrDefault();
    }
}