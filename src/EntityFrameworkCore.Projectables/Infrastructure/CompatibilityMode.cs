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
        /// This mode can be used when you wan't to pass scoped services to your Projectable methods
        /// </summary>
        Full,
        /// <summary>
        /// Projectables are expanded in the query preprocessor and afterwards cached.
        /// This is the default compatibility mode.
        /// </summary>
        Limited
    }
}
