// STUB:TDD marker present ONLY to satisfy the RED-phase workflow-gate hook (see the
// system-io fixture). NEGATIVE FIXTURE: Compile-Removed, read as text by GREEN's
// scanner. The System.Diagnostics.Process violation is INTENTIONAL (INV-004 / B4).
namespace BadKernel.Process;

public static class ImpureKernel
{
    public static void Spawn()
    {
        // Forbidden: System.Diagnostics.Process (process family).
        System.Diagnostics.Process.Start("/bin/true");
    }
}
