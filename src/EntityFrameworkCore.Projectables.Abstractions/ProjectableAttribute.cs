using System;

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
        /// <c>true</c> will allow you to request for this property by
        /// explicitly calling .Include(x => x.Property) on the query,
        /// <c>false</c> will always consider this query to be included.
        /// </summary>
        public bool OnlyOnInclude { get; set; }
    }
}
