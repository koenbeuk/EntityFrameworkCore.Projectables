using System.Runtime.CompilerServices;

namespace EntityFrameworkCore.Projectables.FunctionalTests
{
    public static class ModuleInitializer
    {
        [ModuleInitializer]
        public static void Initialize()
        {
#if !NET8_0
            VerifierSettings.UniqueForTargetFrameworkAndVersion();
#endif
        }
    }
}