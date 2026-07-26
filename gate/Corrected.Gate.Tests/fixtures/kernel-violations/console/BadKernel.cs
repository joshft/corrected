// STUB:TDD marker present ONLY to satisfy the RED-phase workflow-gate hook (see the
// system-io fixture). NEGATIVE FIXTURE: Compile-Removed, read as text by GREEN's
// scanner. The System.Console violation is INTENTIONAL (INV-004 / B4).
namespace BadKernel.Console;

public static class ImpureKernel
{
    public static void Announce()
    {
        // Forbidden: System.Console (I/O family).
        System.Console.WriteLine("kernel is not supposed to write to the console");
    }
}
