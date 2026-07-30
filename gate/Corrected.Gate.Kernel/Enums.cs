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

/// <summary>
/// Phase-entry lifecycle latch of a schema-v2 readiness block (Group G / INV-026 /
/// RS-021). This is a DISTINCT axis from <see cref="ReadinessStatus"/> (which carries
/// the declared BLOCKED/READY computation the gate always re-derives): the lifecycle
/// records whether phase entry has been ATTESTED. v1 blocks have no serialized
/// lifecycle key — they are interpreted as implicit <see cref="Blocked"/>.
/// COMPLETE is RESERVED conceptually and added only by a LATER schema version; it is
/// deliberately NOT a member here (no COMPLETE code in this schema).
/// </summary>
public enum LifecycleState
{
    Blocked,
    Entered,
}

/// <summary>
/// Derived integrity of the historical entry receipt at commit X, computed by the
/// impure gate-side receipt verifier (INV-030) and SUPPLIED to the pure transition
/// evaluator (INV-026 component #1) as an enum input — the kernel does NO crypto. This
/// axis is DISTINCT from the DECLARED <see cref="LifecycleState"/> latch: the latch
/// (persisted, monotonic) drives the src/ ban (INV-027); <c>entry_integrity</c> drives
/// the gate verdict. The four states are exactly {verified|rejected|unavailable|absent}
/// from the Group G state-model tables (A)/(B), spec ~981–986.
/// </summary>
public enum EntryIntegrity
{
    /// <summary>The committed entry receipt verified (signature + schema + ancestry, or a full at-activation re-derivation).</summary>
    Verified,

    /// <summary>The committed entry receipt is present but rejected/tampered (a hard-red verdict).</summary>
    Rejected,

    /// <summary>The P3 verifier/root is transiently unreadable — NEVER represented as ok; a first activation must not merge on a fault.</summary>
    Unavailable,

    /// <summary>No committed entry receipt where the declaration requires one.</summary>
    Absent,
}

/// <summary>
/// The pure transition PROPOSAL minted by <see cref="ReadinessGate.EvaluateTransition"/>
/// (INV-026 component #1): a proposal only — the evaluator mints/writes/signs NOTHING.
/// Exactly {stay-BLOCKED | propose-ENTER | honor-ENTERED} from the Group G state model.
/// This is a DISTINCT return from the retained 2-arg verdict <see cref="ReadinessVerdict"/>
/// (RS-022): the evaluator emits a transition proposal, NOT the Pass/Fail verdict (which
/// the impure orchestrator computes in a later sub-track).
/// </summary>
public enum ProposedTransition
{
    /// <summary>Declared-BLOCKED and activation NOT proposed (preconditions unmet OR entry_integrity != verified).</summary>
    StayBlocked,

    /// <summary>Declared-BLOCKED at-activation: propose BLOCKED->ENTERED (iff P1∧P2∧P3 re-derive true AND entry_integrity==verified).</summary>
    ProposeEnter,

    /// <summary>Declared-ENTERED established: honor the monotonic latch (a transient integrity fault never reverts it).</summary>
    HonorEntered,
}
