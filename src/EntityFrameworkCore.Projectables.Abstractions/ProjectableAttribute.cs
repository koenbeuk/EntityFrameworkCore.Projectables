using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables
{
    /// <summary>
    /// Declares this property or method to be Projectable. 
    /// A companion Expression tree will be generated
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class ProjectableAttribute : Attribute
    {
        /// <summary>
        /// Get or set how null-conditional operators are handeled
        /// </summary>
        public NullConditionalRewriteSupport NullConditionalRewriteSupport { get; set; }

        /// <summary>
        /// Get or set from which member to get the expression,
        /// or null to get it from the current member.
        /// </summary>
        public string? UseMemberBody { get; set; }

        /// <summary>
        /// Get or set whether to expand enum method/extension calls by evaluating them at compile time
        /// and generating ternary expressions for each enum value.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When enabled, method calls on enum values are resolved at compile time by evaluating 
        /// the method for each possible enum value and generating a chain of ternary expressions.
        /// </para>
        /// <para>
        /// For example, <c>MyEnumValue.GetDescription()</c> would be expanded to:
        /// <c>MyEnumValue == Value1 ? "Description 1" : MyEnumValue == Value2 ? "Description 2" : null</c>
        /// </para>
        /// <para>
        /// This is useful for <c>Where()</c> and <c>OrderBy()</c> clauses where the expression
        /// needs to be translated to SQL.
        /// </para>
        /// </remarks>
        public bool ExpandEnumMethods { get; set; }
    }
}
