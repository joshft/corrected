using System;
using Corrected.Gate.Kernel;

namespace Corrected.Gate;

// P3 phase-entry INV-030 (Group G / MA-C): the entry-receipt verifier computes an INTERNAL typed
// result (an enum/value type, NOT a free string) and maps it fail-closed to an EntryIntegrity
// verdict at the boundary. This mirrors DeterminismVerifyReason, but the entry severity map is
// THREE-valued — {absent | rejected | unavailable} — because EntryIntegrity carries the extra
// Absent state (no committed entry receipt where one is required keeps the src/ ban active,
// INV-027). The enum + its per-member EntrySeverity annotations are the COMMITTED artifact the
// INV-030 totality cross-product test derives its expected mapping FROM (RS-010 / AP-022 / PMB-003
// — never a test literal).

/// <summary>
/// The THREE-valued severity an entry internal reason resolves to (INV-030). Unlike determinism's
/// two-valued map, entry adds <see cref="Absent"/> for the no-committed-receipt zero-state, which
/// keeps the production ban active (INV-027) and blocks activation — distinct from an active-but-
/// <see cref="Rejected"/> tamper and a transient <see cref="Unavailable"/> tool fault. The DEFAULT
/// for anything not positively identified is <see cref="Rejected"/> (fail-closed, never accepting).
/// </summary>
public enum EntrySeverity
{
    /// <summary>Present-but-invalid: crypto / schema / ancestry failure, AND the fail-closed DEFAULT.</summary>
    Rejected,

    /// <summary>A transient tool/environment fault only (the closed 2-member set).</summary>
    Unavailable,

    /// <summary>No committed entry receipt where one is required (pre-entry zero-state; ban stays active).</summary>
    Absent,
}

/// <summary>
/// The COMMITTED per-reason severity declaration (INV-030 / RS-010). Each
/// <see cref="EntryVerifyReason"/> member carries exactly one of these; the totality cross-product
/// test derives the expected <see cref="EntrySeverity"/> for every member FROM this annotation
/// (reflection), so shrinking or re-pointing the map is a reviewable diff on the committed enum —
/// not a silent test edit.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EntrySeverityAttribute : Attribute
{
    public EntrySeverityAttribute(EntrySeverity severity) => Severity = severity;

    public EntrySeverity Severity { get; }
}

/// <summary>
/// The internal typed entry-verify reason (INV-030). The <c>unavailable</c> set is the TWO
/// transient faults ONLY; the <c>absent</c> set is the pre-entry zero-state faults; every other
/// reason — plus the pinned DEFAULT <see cref="UnclassifiedVerifierFault"/> — is <c>rejected</c>
/// (fail-closed). Each member is annotated with its committed <see cref="EntrySeverityAttribute"/>
/// so the totality test can bind the map to the committed enum, not a test literal.
/// </summary>
public enum EntryVerifyReason
{
    // ---- the CLOSED unavailable set (the ONLY two members annotated Unavailable) ----

    /// <summary>cosign binary absent / online-provisioning not completed (EA-008).</summary>
    [EntrySeverity(EntrySeverity.Unavailable)]
    VerifierUnavailable,

    /// <summary>The pinned root/binary file is present-but-unreadable — an I/O fault (EA-009).</summary>
    [EntrySeverity(EntrySeverity.Unavailable)]
    TrustRootOrToolUnreadable,

    // ---- the CLOSED absent set (pre-entry zero-state; ban stays active, never accepting) ----

    /// <summary>No committed entry bundle/receipt where the declaration requires one (INV-027 ban stays active).</summary>
    [EntrySeverity(EntrySeverity.Absent)]
    EvidenceAbsent,

    /// <summary>The expected pre-entry zero-state (no entry pointer yet) — rendered distinctly, classified Absent.</summary>
    [EntrySeverity(EntrySeverity.Absent)]
    PointerNotYetActivated,

    // ---- the rejected set (crypto / schema / ancestry) ----

    [EntrySeverity(EntrySeverity.Rejected)]
    MalformedReceipt,

    [EntrySeverity(EntrySeverity.Rejected)]
    MalformedBundle,

    [EntrySeverity(EntrySeverity.Rejected)]
    SignatureInvalid,

    /// <summary>2a: the leaf cert SAN is not the pinned entry identity (cosign identity reject).</summary>
    [EntrySeverity(EntrySeverity.Rejected)]
    IdentityMismatch,

    /// <summary>A determinism (or any non-entry) predicate type presented to the entry gate (RS-024 cross-rejection).</summary>
    [EntrySeverity(EntrySeverity.Rejected)]
    PredicateTypeMismatch,

    /// <summary>The cosign check-claims blob (the commit-X representation) sha256 != the signed commit subject digest.</summary>
    [EntrySeverity(EntrySeverity.Rejected)]
    SubjectDigestMismatch,

    /// <summary>
    /// The decoded SIGNED entry Statement fails <see cref="Corrected.Provenance.Entry.EntryAttestation.ValidateEntrySchema"/>
    /// — a bad subject cardinality/name/order, a broken subject&lt;-&gt;manifest-root binding, a
    /// ref-string (non-full-closure) manifest, or a wrong predicate type. This is the entry analog
    /// of the determinism INV-010 byte-equality: the entry statement is self-describing (its
    /// predicate carries the full closures and the subjects bind to their manifest roots), so a
    /// mutated predicate that keeps subjects[0] is caught by the internal subject&lt;-&gt;manifest
    /// binding here, not a separate reconstruction.
    /// </summary>
    [EntrySeverity(EntrySeverity.Rejected)]
    EntrySchemaInvalid,

    /// <summary>
    /// 2b: the certificate's workflow-SHA does NOT equal the entry receipt's commit-X. DISTINCT
    /// from <see cref="IdentityMismatch"/> (the SAN/identity check cosign rejects first) — this is
    /// the Corrected-side binding check reached only once identity has passed (RS-006).
    /// </summary>
    [EntrySeverity(EntrySeverity.Rejected)]
    CertWorkflowShaMismatch,

    /// <summary>The entry commit X is NOT an ancestor of HEAD (a non-ancestor activation).</summary>
    [EntrySeverity(EntrySeverity.Rejected)]
    AttestedCommitNotAncestor,

    /// <summary>A shallow-clone/absent-X ancestry that cannot be computed is rejected, NEVER unavailable (RS-013).</summary>
    [EntrySeverity(EntrySeverity.Rejected)]
    AncestryUncomputable,

    /// <summary>A root/binary digest MISMATCH — distinct from the *unreadable* transient fault.</summary>
    [EntrySeverity(EntrySeverity.Rejected)]
    TrustRootOrPinMismatch,

    /// <summary>
    /// The pinned DEFAULT branch: any cosign crash / unknown non-zero exit / timeout / output the
    /// taxonomy does not positively match -> rejected (fail-closed). Treating it as unavailable (or
    /// absent) is the seam that would arm a forged-ENTERED bypass; it is closed.
    /// </summary>
    [EntrySeverity(EntrySeverity.Rejected)]
    UnclassifiedVerifierFault,
}

/// <summary>
/// The TOTAL internal-reason -> <see cref="EntrySeverity"/> map (INV-030, RS-002/RS-010). Total AND
/// fail-closed by DEFAULT: any reason not positively one of the transient faults or the absent
/// zero-state resolves to <see cref="EntrySeverity.Rejected"/>, and an out-of-range value (a future
/// reason with no explicit branch) also resolves to <c>Rejected</c> — never an accepting verdict.
/// </summary>
public static class EntryVerifyReasonMap
{
    /// <summary>
    /// Classify an internal reason as <c>absent</c> / <c>rejected</c> / <c>unavailable</c>
    /// (INV-030). Total and fail-closed: the default is <c>rejected</c>. ONLY the two positively-
    /// enumerated transient faults resolve to Unavailable and ONLY the two zero-state faults resolve
    /// to Absent; every other named reason AND the default branch (an out-of-range / future value)
    /// resolve to Rejected — never an accepting verdict. This switch must AGREE with the committed
    /// [EntrySeverity(...)] annotations (the INV-030 totality test reflects over them cell-by-cell).
    /// </summary>
    public static EntrySeverity Classify(EntryVerifyReason reason)
        => reason switch
        {
            EntryVerifyReason.VerifierUnavailable => EntrySeverity.Unavailable,
            EntryVerifyReason.TrustRootOrToolUnreadable => EntrySeverity.Unavailable,
            EntryVerifyReason.EvidenceAbsent => EntrySeverity.Absent,
            EntryVerifyReason.PointerNotYetActivated => EntrySeverity.Absent,
            _ => EntrySeverity.Rejected,
        };

    /// <summary>
    /// Map an <see cref="EntrySeverity"/> to the carrier <see cref="EntryIntegrity"/> verdict
    /// (INV-030 boundary). Total, fail-closed: Absent->Absent, Unavailable->Unavailable,
    /// Rejected->Rejected, and any out-of-range value -> Rejected (never the accepting Verified).
    /// The accepting <see cref="EntryIntegrity.Verified"/> is NOT reachable from a reason — it is
    /// the null-reason accept path in <see cref="EntryVerifier"/>.
    /// </summary>
    public static EntryIntegrity ToIntegrity(EntrySeverity severity)
        => severity switch
        {
            EntrySeverity.Absent => EntryIntegrity.Absent,
            EntrySeverity.Unavailable => EntryIntegrity.Unavailable,
            _ => EntryIntegrity.Rejected,
        };
}
