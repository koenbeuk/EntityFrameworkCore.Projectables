using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projections.Generator
{
    public class ProjectableDescriptor
    {
        public IEnumerable<string> UsingDirectives { get; set; }

        public string ClassNamespace { get; set; }

        public IEnumerable<string> NestedInClassNames { get; set; }

        public string TargetClassNamespace { get; set; }

        public IEnumerable<string> TargetNestedInClassNames { get; set; }

        public string ClassName { get; set; }

        public string MemberName { get; set; }

        public string ReturnTypeName { get; set; }

        public string ParametersListString { get; set; }

        public SyntaxNode Body { get; set; }
    }
}
