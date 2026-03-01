using System.Runtime.CompilerServices;

namespace EntityFrameworkCore.Projectables.Generator.Tests;

public static class VerifyInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Uncomment the following line to enable auto-approval of snapshots. Make sure to review changes before enabling this.
        // VerifierSettings.AutoVerify();
    }
}