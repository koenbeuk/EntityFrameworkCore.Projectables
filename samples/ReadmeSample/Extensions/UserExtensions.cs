using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables;
using ReadmeSample.Entities;

namespace ReadmeSample.Extensions
{
    public static class UserExtensions
    {
        [Projectable]
        public static Order GetMostRecentOrderForUser(this User user, DateTime? cutoffDate)
            => user.Orders.Where(x => x.CreatedDate >= cutoffDate).OrderByDescending(x => x.CreatedDate).FirstOrDefault();
    }
}
