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
            return GenerateName(namespaceName, nestedInClassNames, memberName, null);
        }

        public static string GenerateName(string? namespaceName, IEnumerable<string>? nestedInClassNames, string memberName, IEnumerable<string>? parameterTypeNames)
        {
            var stringBuilder = new StringBuilder();

            return GenerateNameImpl(stringBuilder, namespaceName, nestedInClassNames, memberName, parameterTypeNames);
        }

        public static string GenerateFullName(string? namespaceName, IEnumerable<string>? nestedInClassNames, string memberName)
        {
            return GenerateFullName(namespaceName, nestedInClassNames, memberName, null);
        }

        public static string GenerateFullName(string? namespaceName, IEnumerable<string>? nestedInClassNames, string memberName, IEnumerable<string>? parameterTypeNames)
        {
            var stringBuilder = new StringBuilder(Namespace);
            stringBuilder.Append('.');

            return GenerateNameImpl(stringBuilder, namespaceName, nestedInClassNames, memberName, parameterTypeNames);
        }

        static string GenerateNameImpl(StringBuilder stringBuilder, string? namespaceName, IEnumerable<string>? nestedInClassNames, string memberName, IEnumerable<string>? parameterTypeNames)
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

            // Add parameter types to make method overloads unique
            if (parameterTypeNames is not null)
            {
                var parameterIndex = 0;
                foreach (var parameterTypeName in parameterTypeNames)
                {
                    stringBuilder.Append("_P");
                    stringBuilder.Append(parameterIndex);
                    stringBuilder.Append('_');
                    // Replace characters that are not valid in type names with underscores
                    var sanitizedTypeName = parameterTypeName
                        .Replace("global::", "")  // Remove global:: prefix
                        .Replace('.', '_')
                        .Replace('<', '_')
                        .Replace('>', '_')
                        .Replace(',', '_')
                        .Replace(' ', '_')
                        .Replace('[', '_')
                        .Replace(']', '_')
                        .Replace('`', '_')
                        .Replace(':', '_')  // Additional safety for any remaining colons
                        .Replace('?', '_'); // Handle nullable reference types
                    stringBuilder.Append(sanitizedTypeName);
                    parameterIndex++;
                }
            }

            // Add generic arity at the very end (after parameter types)
            // This matches how the CLR names generic types
            if (arity > 0)
            {
                stringBuilder.Append('`');
                stringBuilder.Append(arity);
            }

            return stringBuilder.ToString();
        }
    }
}
