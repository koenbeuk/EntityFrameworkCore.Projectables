using System;

namespace EntityFrameworkCore.Projectables
{
    /// <summary>
    /// Describes what an enum extension method returns for compile-time evaluation during projectable expansion.
    /// Used in conjunction with <see cref="ProjectableAttribute.ExpandEnumMethods"/> to expand enum method calls
    /// into ternary expression chains.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This attribute can be applied to extension methods that operate on enum values to describe what they return:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>No arguments: The method returns the attribute of type T (where T is the generic type argument)</description>
    /// </item>
    /// <item>
    /// <description>One argument (AttributeType): The method returns the attribute of the specified type</description>
    /// </item>
    /// <item>
    /// <description>Two arguments (AttributeType, PropertyName): The method returns the value of the specified property from the attribute</description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// public static class EnumExtensions
    /// {
    ///     // Returns the Display attribute's Name property
    ///     [ProjectableEnumMethod(typeof(DisplayAttribute), nameof(DisplayAttribute.Name))]
    ///     public static string? GetDisplayName(this MyEnum value) { ... }
    ///     
    ///     // Returns the Description attribute's constructor value
    ///     [ProjectableEnumMethod(typeof(DescriptionAttribute))]
    ///     public static string? GetDescription(this MyEnum value) { ... }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class ProjectableEnumMethodAttribute : Attribute
    {
        /// <summary>
        /// Gets the type of attribute to retrieve from the enum member.
        /// </summary>
        public Type? AttributeType { get; }

        /// <summary>
        /// Gets the name of the property to access on the attribute.
        /// If null, the attribute's constructor argument value is used.
        /// </summary>
        public string? PropertyName { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectableEnumMethodAttribute"/> class
        /// that indicates the method returns an attribute value from the enum member.
        /// The attribute type is inferred from the method's generic type argument or return type.
        /// </summary>
        public ProjectableEnumMethodAttribute()
        {
            AttributeType = null;
            PropertyName = null;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectableEnumMethodAttribute"/> class
        /// that indicates the method returns the constructor argument value of the specified attribute type.
        /// </summary>
        /// <param name="attributeType">The type of attribute to retrieve from the enum member.</param>
        public ProjectableEnumMethodAttribute(Type attributeType)
        {
            AttributeType = attributeType ?? throw new ArgumentNullException(nameof(attributeType));
            PropertyName = null;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectableEnumMethodAttribute"/> class
        /// that indicates the method returns the value of a specific property from the specified attribute type.
        /// </summary>
        /// <param name="attributeType">The type of attribute to retrieve from the enum member.</param>
        /// <param name="propertyName">The name of the property to access on the attribute.</param>
        public ProjectableEnumMethodAttribute(Type attributeType, string propertyName)
        {
            AttributeType = attributeType ?? throw new ArgumentNullException(nameof(attributeType));
            PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        }
    }
}
