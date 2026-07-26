// STUB:TDD marker present ONLY to satisfy the RED-phase workflow-gate hook (see the
// system-io fixture). NEGATIVE FIXTURE: Compile-Removed, read as text by GREEN's
// scanner. The DateTime.Now ambient-clock read is INTENTIONAL (INV-004 / B4).
namespace BadKernel.DateTimeNow;

public static class ImpureKernel
{
    public static bool DecidedRecently()
    {
        // Forbidden: System.DateTime.Now (ambient-clock nondeterminism).
        return System.DateTime.Now.Hour < 12;
    }
}
