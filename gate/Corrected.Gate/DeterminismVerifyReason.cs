using System;

namespace Corrected.Gate;

// P3 determinism-attestation spec INV-012 (~453-500): the P3 probe computes an INTERNAL
// typed result (an enum/value type, NOT a free string) and maps it fail-closed to a carrier
// ProbeReasons token at the boundary. This file declares the committed typed reason enum plus
// the TOTAL internal-reason -> {rejected|unavailable} severity map.
//
// RED-phase contract: the enum + its per-member VerifySeverity annotations are the COMMITTED
// artifact the INV-012 totality cross-product test derives its expected mapping FROM (RS-010 /
// AP-022 / PMB-003 — never a test literal). The map function DeterminismVerifyReasonMap.Classify
// is the code UNDER TEST; its body is STUB:TDD (deny-by-default) so the transient-fault cells
// fail as ASSERTIONS while the fail-closed default cell passes.

/// <summary>
/// The two-valued severity a P3 internal reason resolves to (INV-012). <c>Unavailable</c> is
/// reserved for the CLOSED, positively-enumerated set of transient tool/environment faults;
/// everything else — and the DEFAULT for anything not positively identified — is
/// <c>Rejected</c> (fail-closed).
/// </summary>
public enum VerifySeverity
{
    /// <summary>Policy / crypto / staleness / ancestry failure, AND the fail-closed DEFAULT.</summary>
    Rejected,

    /// <summary>A transient tool/environment fault only (the closed 2-member set).</summary>
    Unavailable,
}

/// <summary>
/// The COMMITTED per-reason severity declaration (INV-012 / RS-010). Each
/// <see cref="DeterminismVerifyReason"/> member carries exactly one of these; the totality
/// cross-product test derives the expected <see cref="VerifySeverity"/> for every member FROM
/// this annotation (reflection), so shrinking or re-pointing the map is a reviewable diff on the
/// committed enum — not a silent test edit.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class VerifySeverityAttribute : Attribute
{
    public VerifySeverityAttribute(VerifySeverity severity) => Severity = severity;

    public VerifySeverity Severity { get; }
}

/// <summary>
/// The internal typed P3-verify reason (INV-012 ~468-484). The <c>unavailable</c> set is the
/// TWO transient faults ONLY; every other reason — plus the pinned DEFAULT
/// <see cref="UnclassifiedVerifierFault"/> — is <c>rejected</c> (fail-closed). Each member is
/// annotated with its committed <see cref="VerifySeverityAttribute"/> so the totality test can
/// bind the map to the committed enum, not a test literal.
/// </summary>
public enum DeterminismVerifyReason
{
    // ---- the CLOSED unavailable set (the ONLY two members annotated Unavailable) ----

    /// <summary>cosign binary absent / online-provisioning not completed (EA-008).</summary>
    [VerifySeverity(VerifySeverity.Unavailable)]
    VerifierUnavailable,

    /// <summary>The pinned root/binary file is present-but-unreadable — an I/O fault (EA-009).</summary>
    [VerifySeverity(VerifySeverity.Unavailable)]
    TrustRootOrToolUnreadable,

    // ---- the rejected set (policy / crypto / staleness / ancestry) ----

    [VerifySeverity(VerifySeverity.Rejected)]
    EvidenceAbsent,

    /// <summary>The expected pre-PR3 zero-state (RS-035) — rendered distinctly but classified fail-closed.</summary>
    [VerifySeverity(VerifySeverity.Rejected)]
    P3NotYetActivated,

    [VerifySeverity(VerifySeverity.Rejected)]
    MalformedReceipt,

    [VerifySeverity(VerifySeverity.Rejected)]
    MalformedBundle,

    [VerifySeverity(VerifySeverity.Rejected)]
    SignatureInvalid,

    [VerifySeverity(VerifySeverity.Rejected)]
    IdentityMismatch,

    [VerifySeverity(VerifySeverity.Rejected)]
    PredicateTypeMismatch,

    [VerifySeverity(VerifySeverity.Rejected)]
    SubjectDigestMismatch,

    /// <summary>
    /// T3b structural contract (INV-010 byte-equality): the decoded SIGNED DSSE Statement does NOT
    /// byte-equal the Statement Corrected reconstructs from the committed receipt, EVEN WHEN the
    /// subject digest matches (a mutated PREDICATE that keeps <c>sha256(receipt)</c>). DISTINCT from
    /// <see cref="SubjectDigestMismatch"/> (the subject sha differs) — cosign's --check-claims never
    /// verifies predicate CONTENT, so only Corrected's internal byte comparison catches this.
    /// Rejected (fail-closed); the byte-equality LOGIC in <c>DeterminismVerifier.Verify</c>'s
    /// cosign-Ok branch is GREEN's job.
    /// </summary>
    [VerifySeverity(VerifySeverity.Rejected)]
    StatementReconstructionMismatch,

    [VerifySeverity(VerifySeverity.Rejected)]
    ProjectionPolicyMismatch,

    [VerifySeverity(VerifySeverity.Rejected)]
    StaleSubjectManifest,

    [VerifySeverity(VerifySeverity.Rejected)]
    AttestedCommitNotAncestor,

    /// <summary>
    /// T3b structural contract (INV-011 cross-check): the certificate's workflow-SHA does NOT
    /// equal the receipt's <c>attested_commit</c>. DISTINCT from <see cref="IdentityMismatch"/>
    /// (the SAN/identity check cosign rejects first) — this is the Corrected-SIDE binding check
    /// reached only once identity has passed (the 2b negative, RS-006). Rejected (fail-closed);
    /// the real cross-check LOGIC in <c>DeterminismVerifier.Verify</c>'s cosign-Ok branch is
    /// GREEN's job.
    /// </summary>
    [VerifySeverity(VerifySeverity.Rejected)]
    CertWorkflowShaMismatch,

    /// <summary>A shallow-clone/absent-X ancestry that cannot be computed is rejected, NEVER unavailable (RS-013).</summary>
    [VerifySeverity(VerifySeverity.Rejected)]
    AncestryUncomputable,

    [VerifySeverity(VerifySeverity.Rejected)]
    RidPlatformMismatch,

    [VerifySeverity(VerifySeverity.Rejected)]
    NonPassOutcome,

    /// <summary>A root/binary digest MISMATCH — distinct from the *unreadable* transient fault.</summary>
    [VerifySeverity(VerifySeverity.Rejected)]
    TrustRootOrPinMismatch,

    /// <summary>
    /// The pinned DEFAULT branch: any cosign crash / SIGSEGV / unknown non-zero exit / timeout /
    /// output the INV-014 taxonomy does not positively match -> rejected (fail-closed). Treating
    /// it as unavailable is the seam that armed the RS-001 forged-ENTERED bypass; it is closed.
    /// </summary>
    [VerifySeverity(VerifySeverity.Rejected)]
    UnclassifiedVerifierFault,
}

/// <summary>
/// The TOTAL internal-reason -> <see cref="VerifySeverity"/> map (INV-012, RS-002/RS-010). The
/// map is total AND fail-closed by DEFAULT: any reason not positively one of the two transient
/// faults resolves to <see cref="VerifySeverity.Rejected"/>, and an out-of-range value (a future
/// reason with no explicit branch) also resolves to <c>Rejected</c> — never <c>Unavailable</c>.
///
/// RED-phase structural stub — the body is deny-by-default so the two transient-fault cells fail
/// as ASSERTIONS (the committed annotation says Unavailable, the stub returns Rejected) while the
/// fail-closed default cell passes. GREEN implements the real total map.
/// </summary>
public static class DeterminismVerifyReasonMap
{
    /// <summary>
    /// Classify an internal reason as <c>rejected</c> or <c>unavailable</c> (INV-012). Total and
    /// fail-closed: the default is <c>rejected</c>.
    /// </summary>
    public static VerifySeverity Classify(DeterminismVerifyReason reason)
    {
        // Total, fail-closed switch. ONLY the two positively-enumerated transient faults resolve
        // to Unavailable; every other named reason AND the default branch (an out-of-range /
        // future value) resolve to Rejected. The default NEVER maps to Unavailable — that seam
        // armed the RS-001 forged-ENTERED bypass and stays closed. This switch must AGREE with
        // the committed [VerifySeverity(...)] annotations (the INV-012 totality test reflects
        // over the annotations and compares them cell-by-cell to this map).
        return reason switch
        {
            DeterminismVerifyReason.VerifierUnavailable => VerifySeverity.Unavailable,
            DeterminismVerifyReason.TrustRootOrToolUnreadable => VerifySeverity.Unavailable,
            _ => VerifySeverity.Rejected,
        };
    }
}
