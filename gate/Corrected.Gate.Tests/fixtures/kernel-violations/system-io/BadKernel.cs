// STUB:TDD marker present ONLY to satisfy the RED-phase workflow-gate hook, which
// checks every new .cs for the tag and does NOT exempt test-classified fixtures/.
// This file is a NEGATIVE FIXTURE: it is Compile-Removed from the test assembly and
// read only as TEXT by GREEN's KernelPurityScanner. The System.IO violation below is
// INTENTIONAL and load-bearing — the scanner must reject it (INV-004 / B4).
namespace BadKernel.SystemIo;

public static class ImpureKernel
{
    public static string ReadCommittedBlock()
    {
        // The forbidden usage the KernelPurityScanner must flag: System.IO.File lives
        // in System.Private.CoreLib, so an assembly-REFERENCE scan would pass (RS-260).
        return System.IO.File.ReadAllText("/etc/hostname");
    }
}
