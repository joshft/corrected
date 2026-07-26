// STUB:TDD marker present ONLY to satisfy the RED-phase workflow-gate hook (see the
// system-io fixture). NEGATIVE FIXTURE: Compile-Removed, read as text by GREEN's
// scanner. The extern/[DllImport] declaration is INTENTIONAL — the scanner must
// reject kernel methods declared extern/[DllImport] (EXT9-03 / INV-004 / B4).
namespace BadKernel.ExternDllImport;

public static class ImpureKernel
{
    // Forbidden: extern + [DllImport] (native P/Invoke, no C# body — evades a
    // body-scan but is rejected by the extern/[DllImport] decl check, EXT9-03).
    [System.Runtime.InteropServices.DllImport("libc")]
    public static extern int getpid();
}
