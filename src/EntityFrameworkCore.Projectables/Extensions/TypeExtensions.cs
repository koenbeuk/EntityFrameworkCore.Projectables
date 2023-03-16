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
        public static string GetSimplifiedTypeName(this Type type)
        {
            var name = type.Name;

            var backtickIndex = name.IndexOf("`");
            if (backtickIndex != -1)
            {
                name = name.Substring(0, backtickIndex);
            }

            return name;
        }

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

        private static bool CanHaveOverridingMethod(this Type derivedType, MethodInfo methodInfo)
        {
            // We only need to search for virtual instance methods who are not declared on the derivedType
            if (derivedType == methodInfo.DeclaringType || methodInfo.IsStatic || !methodInfo.IsVirtual)
            {
                return false;
            }

            if (!derivedType.IsAssignableTo(methodInfo.DeclaringType))
            {
                throw new ArgumentException("MethodInfo needs to be declared on the type hierarchy", nameof(methodInfo));
            }

            return true;
        }

        private static int? GetOverridingMethodIndex(this MethodInfo methodInfo, MethodInfo[]? allDerivedMethods)
        {
            if (allDerivedMethods is { Length: > 0 })
            {
                var baseDefinition = methodInfo.GetBaseDefinition();
                for (var i = 0; i < allDerivedMethods.Length; i++)
                {
                    var derivedMethodInfo = allDerivedMethods[i];
                    if (derivedMethodInfo.GetBaseDefinition() == baseDefinition)
                    {
                        return i;
                    }
                }
            }

            return null;
        }

        public static MethodInfo GetOverridingMethod(this Type derivedType, MethodInfo methodInfo)
        {
            if (!derivedType.CanHaveOverridingMethod(methodInfo))
            {
                return methodInfo;
            }
            
            var derivedMethods = derivedType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            return methodInfo.GetOverridingMethodIndex(derivedMethods) is { } i
                ? derivedMethods[i]
                // No derived methods were found. Return the original methodInfo
                : methodInfo;
        }

        public static PropertyInfo GetOverridingProperty(this Type derivedType, PropertyInfo propertyInfo)
        {
            var accessor = propertyInfo.GetAccessors(true)[0];

            if (!derivedType.CanHaveOverridingMethod(accessor))
            {
                return propertyInfo;
            }
            
            var derivedProperties = derivedType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var derivedPropertyMethods = derivedProperties
                                        .Select((Func<PropertyInfo, MethodInfo?>)
                                             (propertyInfo.GetMethod == accessor ? p => p.GetMethod : p => p.SetMethod))
                                        .OfType<MethodInfo>().ToArray();

            return accessor.GetOverridingMethodIndex(derivedPropertyMethods) is { } i
                ? derivedProperties[i]
                // No derived methods were found. Return the original methodInfo
                : propertyInfo;
        }

        public static MethodInfo GetImplementingMethod(this Type derivedType, MethodInfo methodInfo)
        {
            var interfaceType = methodInfo.DeclaringType;
            // We only need to search for interface methods
            if (interfaceType?.IsInterface != true || derivedType.IsInterface || methodInfo.IsStatic || !methodInfo.IsVirtual)
            {
                return methodInfo;
            }

            if (!derivedType.IsAssignableTo(interfaceType))
            {
                throw new ArgumentException("MethodInfo needs to be declared on the type hierarchy", nameof(methodInfo));
            }

            var interfaceMap = derivedType.GetInterfaceMap(interfaceType);
            for (var i = 0; i < interfaceMap.InterfaceMethods.Length; i++)
            {
                if (interfaceMap.InterfaceMethods[i] == methodInfo)
                {
                    return interfaceMap.TargetMethods[i];
                }
            }

            throw new ApplicationException(
                $"The interface map for {derivedType} doesn't contain the implemented method for {methodInfo}!");
        }

        public static PropertyInfo GetImplementingProperty(this Type derivedType, PropertyInfo propertyInfo)
        {
            var accessor = propertyInfo.GetAccessors()[0];

            var implementingAccessor = derivedType.GetImplementingMethod(accessor);
            if (implementingAccessor == accessor)
            {
                return propertyInfo;
            }

            var derivedProperties = derivedType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            return derivedProperties.First(propertyInfo.GetMethod == accessor
                ? p => p.GetMethod == implementingAccessor
                : p => p.SetMethod == implementingAccessor);
        }

        public static MethodInfo GetConcreteMethod(this Type derivedType, MethodInfo methodInfo)
            => methodInfo.DeclaringType?.IsInterface == true
                ? derivedType.GetImplementingMethod(methodInfo)
                : derivedType.GetOverridingMethod(methodInfo);

        public static PropertyInfo GetConcreteProperty(this Type derivedType, PropertyInfo propertyInfo)
            => propertyInfo.DeclaringType?.IsInterface == true
                ? derivedType.GetImplementingProperty(propertyInfo)
                : derivedType.GetOverridingProperty(propertyInfo);
    }
}
