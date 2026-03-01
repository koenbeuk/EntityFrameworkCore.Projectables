using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables.Generator
{
    public class ProjectableDescriptor
    {
        public IEnumerable<UsingDirectiveSyntax>? UsingDirectives { get; set; }

        public string? ClassNamespace { get; set; }

        public IEnumerable<string>? NestedInClassNames { get; set; }

        public string? TargetClassNamespace { get; set; }

        public IEnumerable<string>? TargetNestedInClassNames { get; set; }

        public string? ClassName { get; set; }

        public TypeParameterListSyntax? ClassTypeParameterList { get; set; }

        public SyntaxList<TypeParameterConstraintClauseSyntax>? ClassConstraintClauses { get; set; }

        public string? MemberName { get; set; }

        public string? ReturnTypeName { get; set; }

        public ParameterListSyntax? ParametersList { get; set; }

        public IEnumerable<string>? ParameterTypeNames { get; set; }

        public TypeParameterListSyntax? TypeParameterList { get; set; }
        
        public SyntaxList<TypeParameterConstraintClauseSyntax>? ConstraintClauses { get; set; }

        public ExpressionSyntax? ExpressionBody { get; set; }

        /// <summary>
        /// Whether all containing types of the projectable member are declared as partial.
        /// When true, the generated expression class will be nested inside the containing types
        /// to allow access to private/protected members.
        /// </summary>
        public bool IsContainingClassPartial { get; set; }

        /// <summary>
        /// The chain of containing type declarations (from outermost to innermost / direct containing type).
        /// Used when IsContainingClassPartial is true to generate the partial class wrappers.
        /// </summary>
        public IReadOnlyList<TypeDeclarationSyntax>? ContainingTypeChain { get; set; }
    }
}
