using System.Reflection;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace EntityFrameworkCore.Projectables.Extensions;

public static class EnumExtensions
{
    /// <summary>
    /// Gets the attribute of type T applied to the enum value, or null if not present.
    /// </summary>
    /// <param name="value"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    [ProjectableEnumMethod]
    public static T? GetAttribute<T>(this Enum value) where T : Attribute
    {
        var type = value.GetType();
        var memberInfo = type.GetMember(value.ToString()).FirstOrDefault();
        return memberInfo?.GetCustomAttribute<T>();
    }
    
    /// <summary>
    /// Gets the display name of the enum value from the DisplayAttribute, or DisplayNameAttribute if present.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [ProjectableEnumMethod(typeof(DisplayAttribute), "Name")]
    [ProjectableEnumMethod(typeof(DisplayNameAttribute), "DisplayName")]
    public static string? GetDisplayName(this Enum value)
    {
        var displayAttribute = value.GetAttribute<DisplayAttribute>();
        if (displayAttribute != null)
        {
            return displayAttribute.Name;
        }

        var displayNameAttribute = value.GetAttribute<DisplayNameAttribute>();
        return displayNameAttribute?.DisplayName;
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public class ProjectableEnumMethodAttribute : Attribute
{
    public Type? AttributeType { get; }
    public string? PropertyName { get; }

    public ProjectableEnumMethodAttribute()
    {
    }

    public ProjectableEnumMethodAttribute(Type attributeType) : this()
    {
        AttributeType = attributeType;
    }

    public ProjectableEnumMethodAttribute(Type attributeType, string propertyName) : this(attributeType)
    {
        PropertyName = propertyName;
    }
}