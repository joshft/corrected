namespace Corrected.Gate.Kernel;

// Enum vocabulary for the readiness kernel (INV-002/003/004/005).
// Pure declarations only; no bodies, no I/O.

/// <summary>The three Phase-0.1 preconditions. Exactly {P1, P2, P3} (INV-002).</summary>
public enum PreconditionId
{
    P1,
    P2,
    P3,
}

/// <summary>
/// Readiness status vocabulary. BLOCKED|READY are the declared states; Indeterminate
/// is the typed value an unparseable block yields to the kernel (INV-002 RS-262) so
/// the deny-by-default branch is reachable.
/// </summary>
public enum ReadinessStatus
{
    BLOCKED,
    READY,
    Indeterminate,
}

/// <summary>
/// Reference-resolution outcome carried by a ProbeResult, populated by the
/// orchestrator, never by the pure kernel (INV-005 RS-T-06).
/// </summary>
public enum ReferenceResolution
{
    Resolved,
    Unresolvable,
    Malformed,
}

/// <summary>Kernel verdict (INV-004/005).</summary>
public enum VerdictKind
{
    Pass,
    Fail,
}
