using System;

namespace Corrected.Gate;

// P3 determinism-attestation spec INV-013 LAYER 1 (~507-508): a PURE policy matrix over
// ALREADY-AUTHENTICATED typed receipts — NO cosign. This layer runs after crypto authenticity
// is established (layer 2/3) and decides claim policy only: outcome, RID, staleness, ancestry.
//
// IMPORTANT (meta-invariant, INV-013): a layer-1 row NEVER invokes cosign. This classifier is a
// PURE function over a supplied view — it references no subprocess seam. A source-scan meta-test
// asserts this file contains no cosign / Process reference, so the "no cosign at layer 1" claim
// is structurally enforced, not merely asserted.

/// <summary>
/// The <c>attested_commit</c>-vs-HEAD ancestry status supplied to the layer-1 classifier
/// (INV-012/019). Computed OUTSIDE the pure classifier (git ancestry is an impure I/O fact) and
/// handed in as a typed input. <c>Uncomputable</c> (a shallow clone / absent commit) must map to
/// a REJECTED reason, never <c>unavailable</c> (RS-013).
/// </summary>
public enum AncestryStatus
{
    Ancestor,
    NotAncestor,
    Uncomputable,
}

/// <summary>
/// The policy-relevant projection of an already-AUTHENTICATED determinism receipt (INV-013 layer
/// 1). Carries only the fields the pure claim policy inspects — the crypto authenticity is a
/// precondition established by layers 2/3, never re-checked here. Staleness and ancestry are
/// supplied as typed inputs (they are impure gate-side facts), never recomputed by the classifier.
/// </summary>
public sealed record AuthenticatedReceiptView
{
    /// <summary>The receipt's <c>execution_status</c> (a pass requires <c>completed</c>).</summary>
    public required string ExecutionStatus { get; init; }

    /// <summary>The receipt's <c>comparison_status</c> (a pass requires <c>equal</c>).</summary>
    public required string ComparisonStatus { get; init; }

    /// <summary>The receipt's recorded platform RID (a pass requires it equal the expected RID).</summary>
    public required string Rid { get; init; }

    /// <summary>True iff the signed subject-manifest digest no longer matches HEAD (INV-018/019).</summary>
    public required bool ManifestStale { get; init; }

    /// <summary>The <c>attested_commit</c>-vs-HEAD ancestry status (INV-012/019).</summary>
    public required AncestryStatus AttestedCommitAncestry { get; init; }
}

/// <summary>
/// The layer-1 pure policy classifier (INV-013). Given an already-authenticated receipt view and
/// the expected RID, returns <c>null</c> to ACCEPT (equal ∧ completed ∧ rid==expected ∧
/// non-stale ∧ attested-commit ancestor-of-HEAD), or the SPECIFIC
/// <see cref="DeterminismVerifyReason"/> for the single policy violation. No cosign, no I/O.
///
/// RED-phase structural stub — the body is deny-by-default (returns a fixed reject reason) so the
/// ACCEPT cell and every SPECIFIC-reason reject cell fail as ASSERTIONS until GREEN wires the real
/// policy matrix. Deny-by-default keeps the fail-CLOSED direction while the tests are red.
/// </summary>
public static class DeterminismPolicyClassifier
{
    /// <summary>
    /// Classify an authenticated receipt against the pinned claim policy (INV-013 layer 1).
    /// Returns <c>null</c> on accept, else the specific reject reason.
    /// </summary>
    public static DeterminismVerifyReason? Classify(AuthenticatedReceiptView receipt, string expectedRid)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        // ALLOWLIST, fail-closed (AP-001): accept ONLY the exact pass shape; every other value is
        // a reject. The outcome check is an ALLOWLIST — comparison MUST equal "equal" and execution
        // MUST equal "completed" — so the OTHER legal non-pass values (comparison "different" /
        // "not_evaluated"; execution "resource_floor_skipped" / "infrastructure_invalid") AND any
        // unknown value all reject as non-pass-outcome. A denylist that only rejected "different"
        // would fail OPEN on "not_evaluated".
        if (!string.Equals(receipt.ComparisonStatus, "equal", StringComparison.Ordinal))
        {
            return DeterminismVerifyReason.NonPassOutcome;
        }

        if (!string.Equals(receipt.ExecutionStatus, "completed", StringComparison.Ordinal))
        {
            return DeterminismVerifyReason.NonPassOutcome;
        }

        // A receipt RID other than the expected pinned RID rejects with the SPECIFIC reason — never
        // a silent skip (RS-015). The off-RID host is a typed reject, not a pass.
        if (!string.Equals(receipt.Rid, expectedRid, StringComparison.Ordinal))
        {
            return DeterminismVerifyReason.RidPlatformMismatch;
        }

        // A stale subject manifest (the signed digest no longer matches HEAD) rejects specifically.
        if (receipt.ManifestStale)
        {
            return DeterminismVerifyReason.StaleSubjectManifest;
        }

        // attested_commit-vs-HEAD ancestry. NotAncestor and Uncomputable each reject with their
        // OWN specific reason. Uncomputable (a shallow clone / absent commit) is REJECTED, NEVER
        // unavailable (RS-013) — a shallow clone cannot degrade into the non-failing class.
        switch (receipt.AttestedCommitAncestry)
        {
            case AncestryStatus.Ancestor:
                break;
            case AncestryStatus.NotAncestor:
                return DeterminismVerifyReason.AttestedCommitNotAncestor;
            case AncestryStatus.Uncomputable:
                return DeterminismVerifyReason.AncestryUncomputable;
            default:
                // Fail-closed: an unknown ancestry value is treated as uncomputable (rejected).
                return DeterminismVerifyReason.AncestryUncomputable;
        }

        // Every allowlist condition held — accept.
        return null;
    }
}
