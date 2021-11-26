using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.Services;

namespace EntityFrameworkCore.Projectables.Extensions
{
    public static class QueryableExtensions
    {
        [Obsolete("Use ExpandProjectables instead")]
        public static IQueryable<TModel> ExpandQuaryables<TModel>(this IQueryable<TModel> query)
            => ExpandProjectables(query);

        /// <summary>
        /// Replaces all calls to properties and methods that are marked with the <C>Projectable</C> attribute with their respective expression tree
        /// </summary>
        public static IQueryable<TModel> ExpandProjectables<TModel>(this IQueryable<TModel> query)
            => query.Provider.CreateQuery<TModel>(query.Expression.ExpandProjectables());
    }
}
