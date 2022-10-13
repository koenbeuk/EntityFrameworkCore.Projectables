using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables.Infrastructure
{
    public enum CompatibilityMode
    {
        /// <summary>
        /// Projectables are expanded on each individual query invocation.
        /// </summary>
        Full,
        /// <summary>
        /// Projectables are expanded in the query preprocessor and afterwards cached.
        /// This yields some performance benefits over native EF with the downside of being incompatible with dynamic parameters.
        /// </summary>
        Limited
    }
}
