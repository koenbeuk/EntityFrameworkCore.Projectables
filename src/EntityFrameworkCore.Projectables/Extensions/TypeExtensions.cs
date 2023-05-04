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

        private static bool IsOverridingMethodOf(this MethodInfo methodInfo, MethodInfo baseDefinition)
            => methodInfo.GetBaseDefinition() == baseDefinition;

        public static MethodInfo GetOverridingMethod(this Type derivedType, MethodInfo methodInfo)
        {
            if (!derivedType.CanHaveOverridingMethod(methodInfo))
            {
                return methodInfo;
            }
            
            var derivedMethods = derivedType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            MethodInfo? overridingMethod = null;
            if (derivedMethods is { Length: > 0 })
            {
                var baseDefinition = methodInfo.GetBaseDefinition();
                overridingMethod = derivedMethods.FirstOrDefault(derivedMethodInfo
                    => derivedMethodInfo.IsOverridingMethodOf(baseDefinition));
            }

            return overridingMethod ?? methodInfo; // If no derived methods were found, return the original methodInfo
        }

        public static PropertyInfo GetOverridingProperty(this Type derivedType, PropertyInfo propertyInfo)
        {
            var accessor = propertyInfo.GetAccessors(true).FirstOrDefault(derivedType.CanHaveOverridingMethod);
            if (accessor is null)
            {
                return propertyInfo;
            }

            var isGetAccessor = propertyInfo.GetMethod == accessor;
            
            var derivedProperties = derivedType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            PropertyInfo? overridingProperty = null;
            if (derivedProperties is { Length: > 0 })
            {
                var baseDefinition = accessor.GetBaseDefinition();
                overridingProperty = derivedProperties.FirstOrDefault(p
                    => (isGetAccessor ? p.GetMethod : p.SetMethod)?.IsOverridingMethodOf(baseDefinition) == true);
            }

            return overridingProperty ?? propertyInfo; // If no derived methods were found, return the original methodInfo
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
