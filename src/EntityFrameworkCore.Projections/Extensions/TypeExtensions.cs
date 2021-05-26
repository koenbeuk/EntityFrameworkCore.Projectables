using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projections.Extensions
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
    }
}
