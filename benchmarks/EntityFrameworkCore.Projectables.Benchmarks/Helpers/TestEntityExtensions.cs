using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Projectables.Benchmarks.Helpers
{
    public static class TestEntityExtensions
    {
        public static int IdPlus1ExtensionMethod(this TestEntity entity) => entity.Id + 1;
    }
}
