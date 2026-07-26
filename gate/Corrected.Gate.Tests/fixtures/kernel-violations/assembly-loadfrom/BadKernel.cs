// STUB:TDD marker present ONLY to satisfy the RED-phase workflow-gate hook (see the
// system-io fixture). NEGATIVE FIXTURE: Compile-Removed, read as text by GREEN's
// scanner. The Assembly.LoadFrom reflection-load is INTENTIONAL (INV-004 / B4).
namespace BadKernel.AssemblyLoadFrom;

public static class ImpureKernel
{
    public static object Load()
    {
        // Forbidden: System.Reflection.Assembly.LoadFrom (assembly-load family).
        return System.Reflection.Assembly.LoadFrom("plugin.dll");
    }
}
