using System.Linq.Expressions;
using System.Reflection;

namespace EntityFrameworkCore.Projections.Services
{
    public interface IProjectionExpressionResolver
    {
        LambdaExpression FindGeneratedExpression(MemberInfo projectableMemberInfo);
    }
}