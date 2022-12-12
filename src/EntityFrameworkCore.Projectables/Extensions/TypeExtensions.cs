using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables.Extensions
{
    public static class TypeExtensions
    {
        public static IEnumerable<Type> GetNestedTypePath(this Type type)
        {
            if (type.IsNested && type.DeclaringType is not null)
            {
                foreach (var containingType in type.DeclaringType.GetNestedTypePath())
                {
                    yield return containingType;
                }
            }

            yield return type;
        }

        public static MethodInfo GetOverridingMethod(this Type derivedType, MethodInfo methodInfo)
        {
            // We only need to search for virtual instance methods who are not declared on the derivedType
            if (derivedType == methodInfo.DeclaringType || methodInfo.IsStatic || !methodInfo.IsVirtual)
            {
                return methodInfo;
            }

            if (!derivedType.IsAssignableTo(methodInfo.DeclaringType))
            {
                throw new ArgumentException("MethodInfo needs to be declared on the type hierarchy", nameof(methodInfo));
            }

            var derivedMethods = derivedType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); 

            foreach (var derivedMethodInfo in derivedMethods)
            {
                if (HasCompatibleSignature(methodInfo, derivedMethodInfo))
                {
                    return derivedMethodInfo;
                }
            }

            // No derived methods were found. Return the original methodInfo
            return methodInfo;

            static bool HasCompatibleSignature(MethodInfo methodInfo, MethodInfo derivedMethodInfo)
            {
                if (methodInfo.Name != derivedMethodInfo.Name)
                {
                    return false;
                }

                var methodParameters = methodInfo.GetParameters();

                var derivedMethodParameters = derivedMethodInfo.GetParameters();
                if (methodParameters.Length != derivedMethodParameters.Length)
                {
                    return false;
                }

                // Match all parameters
                for (var parameterIndex = 0; parameterIndex < methodParameters.Length; parameterIndex++)
                {
                    var parameter = methodParameters[parameterIndex];
                    var derivedParameter = derivedMethodParameters[parameterIndex];

                    if (parameter.ParameterType.IsGenericParameter)
                    {
                        if (!derivedParameter.ParameterType.IsGenericParameter)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (parameter.ParameterType != derivedParameter.ParameterType)
                        {
                            return false;
                        }
                    }
                }

                // Match the number of generic type arguments
                if (methodInfo.IsGenericMethodDefinition)
                {
                    var methodGenericParameters = methodInfo.GetGenericArguments();

                    if (!derivedMethodInfo.IsGenericMethodDefinition)
                    {
                        return false;
                    }

                    var derivedGenericArguments = derivedMethodInfo.GetGenericArguments();

                    if (methodGenericParameters.Length != derivedGenericArguments.Length)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
