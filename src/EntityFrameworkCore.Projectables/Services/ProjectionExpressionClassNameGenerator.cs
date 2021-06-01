using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables.Services
{
    public static class ProjectionExpressionClassNameGenerator
    {
        public const string Namespace = "EntityFrameworkCore.Projectables.Generated";

        public static string GenerateName(string? namespaceName, IEnumerable<string> nestedInClassNames, string memberName)
        {
            var stringBuilder = new StringBuilder();

            return GenerateNameImpl(stringBuilder, namespaceName, nestedInClassNames, memberName);
        }

        public static string GenerateFullName(string? namespaceName, IEnumerable<string> nestedInClassNames, string memberName)
        {
            var stringBuilder = new StringBuilder(Namespace);
            stringBuilder.Append('.');

            return GenerateNameImpl(stringBuilder, namespaceName, nestedInClassNames, memberName);
        }

        static string GenerateNameImpl(StringBuilder stringBuilder, string? namespaceName, IEnumerable<string> nestedInClassNames, string memberName)
        {
            stringBuilder.Append(namespaceName?.Replace('.', '_'));
            stringBuilder.Append('_');
            foreach (var className in nestedInClassNames)
            {
                stringBuilder.Append(className);
                stringBuilder.Append('_');
            }
            stringBuilder.Append(memberName);

            return stringBuilder.ToString();
        }
    }
}
