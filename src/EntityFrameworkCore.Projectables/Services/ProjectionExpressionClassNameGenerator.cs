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

        public static string GenerateName(string? namespaceName, IEnumerable<string>? nestedInClassNames, string memberName)
        {
            var stringBuilder = new StringBuilder();

            return GenerateNameImpl(stringBuilder, namespaceName, nestedInClassNames, memberName);
        }

        public static string GenerateFullName(string? namespaceName, IEnumerable<string>? nestedInClassNames, string memberName)
        {
            var stringBuilder = new StringBuilder(Namespace);
            stringBuilder.Append('.');

            return GenerateNameImpl(stringBuilder, namespaceName, nestedInClassNames, memberName);
        }

        static string GenerateNameImpl(StringBuilder stringBuilder, string? namespaceName, IEnumerable<string>? nestedInClassNames, string memberName)
        {
            stringBuilder.Append(namespaceName?.Replace('.', '_'));
            stringBuilder.Append('_');
            var arity = 0;
            
            if (nestedInClassNames is not null)
            {

                foreach (var className in nestedInClassNames)
                {
                    var arityCharacterIndex = className.IndexOf('`');
                    if (arityCharacterIndex is -1)
                    {
                        stringBuilder.Append(className);
                    }
                    else
                    {
#if NETSTANDARD2_0
                        arity += int.Parse(className.Substring(arityCharacterIndex + 1));
#else
                        arity += int.Parse(className.AsSpan().Slice(arityCharacterIndex + 1));
#endif
                        stringBuilder.Append(className, 0, arityCharacterIndex);
                    }

                    stringBuilder.Append('_');
                }

            }
            stringBuilder.Append(memberName);

            if (arity > 0)
            {
                stringBuilder.Append('`');
                stringBuilder.Append(arity);
            }

            return stringBuilder.ToString();
        }
    }
}
