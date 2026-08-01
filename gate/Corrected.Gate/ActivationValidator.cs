using System;
using System.Collections.Generic;
using System.Linq;
using Corrected.Gate.Kernel;

namespace Corrected.Gate;

/// <summary>
/// A synthetic binding of the entry receipt to the entry commit: the commit <c>X</c> the receipt
/// attests + the receipt's OWN committed path (used for the self-reference-safety check). Small
/// SYNTHETIC record — this track builds ONLY the activation-diff validator (architecture
/// entrypoint 4); the cosign crypto verify + the evidence-digests-against-the-historical-snapshot
/// -at-<c>X</c> are a SEPARATE seam (INV-030 / tracks 5e/T3), reduced here to the
/// <see cref="EntryIntegrity"/> input.
/// </summary>
public sealed record EntryReceiptRef(string BoundCommitX, string ReceiptPath);

/// <summary>
/// The ancestry / committed-tree snapshot the validator needs for INV-029's self-reference
/// (clause 2) and ancestor (clause 3) checks. Models "X is an ancestor of HEAD" and "the SET of
/// paths committed in X's tree" (the self-reference check asks whether the receipt's own path is
/// contained in X's tree — AP-021 circular gate).
/// </summary>
public sealed record ActivationAncestry(
    bool BoundCommitIsAncestorOfHead,
    IReadOnlySet<string> CommittedTreeAtBoundCommit);

/// <summary>
/// The activation-diff validator verdict: a bool accept + a typed reason string. On a fail-closed
/// reject <see cref="Accepted"/> is false and <see cref="Reason"/> carries the typed cause.
/// </summary>
public sealed class ActivationValidationResult
{
    private ActivationValidationResult(bool accepted, string reason)
    {
        Accepted = accepted;
        Reason = reason;
    }

    /// <summary>True iff the activation is legitimate under the two-step sign→activate protocol
    /// (activation-only diff ∧ self-reference-safe ∧ ancestor ∧ verified receipt). False == fail-closed.</summary>
    public bool Accepted { get; }

    /// <summary>Typed accept/reject reason. Non-empty on reject.</summary>
    public string Reason { get; }

    internal static ActivationValidationResult Accept() =>
        new(true, "activation-only transition; self-reference-safe; ancestor; verified receipt");

    internal static ActivationValidationResult Reject(string reason) => new(false, reason);
}

/// <summary>
/// INV-029 (spec ~1157–1181): architecture entrypoint (4), the <b>activation-diff validator</b> —
/// the SEPARATE, deeper protocol check that the entry receipt is activated by a
/// self-reference-safe <b>two-step sign→activate protocol</b>. This is DISTINCT from the PRH-007
/// <see cref="PrClassifier"/> (which assigns a PR class): this validator enforces the deeper
/// crypto/ancestry/self-reference protocol given the SUPPLIED verify result.
///
/// It ACCEPTS iff ALL hold (else fail closed, deny-by-default — AP-001):
///   (1) activation-only atomic transition against the merge-base (from-v1 OR from-v2-BLOCKED;
///       status/ready_predicate/every precondition IDENTICAL base→head);
///   (2) self-reference-safe: ReceiptPath ∉ X's committed tree (AP-021 circular gate);
///   (3) ancestor: X is an ancestor of HEAD;
///   (4) a verified entry receipt (entryReceiptIntegrity == Verified).
///
/// STUB:TDD — deny-by-default. The real two-step protocol check is GREEN's job.
/// </summary>
public static class ActivationValidator
{
    /// <summary>
    /// Validate a phase-entry activation under the two-step sign→activate protocol. Pure over the
    /// supplied inputs (no I/O) — the receipt crypto is reduced to <paramref name="entryReceiptIntegrity"/>.
    /// </summary>
    /// <param name="mergeBaseBlock">The VALIDATED protected-main merge-base readiness block (the base).</param>
    /// <param name="headBlock">The activation PR head readiness block.</param>
    /// <param name="changedControlledPaths">The controlled paths the activation touches (must be only the entry-receipt path).</param>
    /// <param name="entryReceiptRef">The entry-receipt reference: bound commit X + the receipt's own committed path.</param>
    /// <param name="ancestry">The ancestry/tree snapshot: X-is-ancestor-of-HEAD + the set of paths committed in X's tree.</param>
    /// <param name="entryReceiptIntegrity">The RESULT of verifying the entry receipt; only Verified may lead to acceptance.</param>
    public static ActivationValidationResult ValidateActivation(
        ReadinessBlock mergeBaseBlock,
        ReadinessBlock headBlock,
        IReadOnlyCollection<string> changedControlledPaths,
        EntryReceiptRef entryReceiptRef,
        ActivationAncestry ancestry,
        EntryIntegrity entryReceiptIntegrity)
    {
        // ---------------------------------------------------------------------------------------
        // Clause 0 (deny-by-default boundary, AP-001): any null supplied REFERENCE input fails
        // closed. entryReceiptIntegrity is a value-type enum — it has no null cell.
        // ---------------------------------------------------------------------------------------
        if (mergeBaseBlock is null || headBlock is null || changedControlledPaths is null ||
            entryReceiptRef is null || ancestry is null)
        {
            return ActivationValidationResult.Reject("null-input (deny-by-default)");
        }

        // ---------------------------------------------------------------------------------------
        // Clause 4 (verifying-receipt required): a FIRST activation strictly requires a
        // from-clean-VERIFYING receipt. Rejected/Unavailable/Absent all fail closed — the
        // monotonic "later-unavailable does not un-enter" (INV-026/027) does NOT apply to a first
        // activation.
        // ---------------------------------------------------------------------------------------
        if (entryReceiptIntegrity != EntryIntegrity.Verified)
        {
            return ActivationValidationResult.Reject(
                $"entry receipt not verified (entry_integrity={entryReceiptIntegrity})");
        }

        // ---------------------------------------------------------------------------------------
        // Clause 3 (ancestor): the bound commit X must be an ancestor of HEAD.
        // ---------------------------------------------------------------------------------------
        if (!ancestry.BoundCommitIsAncestorOfHead)
        {
            return ActivationValidationResult.Reject("bound commit X is not an ancestor of HEAD");
        }

        // ---------------------------------------------------------------------------------------
        // Clause 2 (self-reference-safe, AP-021 circular gate): the entry commit X must NOT
        // contain the receipt that binds it. A null committed-tree snapshot cannot prove
        // self-reference-safety → fail closed. A null receipt path is a malformed ref → fail closed.
        // ---------------------------------------------------------------------------------------
        string? receiptPath = entryReceiptRef.ReceiptPath;
        if (receiptPath is null)
        {
            return ActivationValidationResult.Reject("entry receipt path is null (malformed ref)");
        }

        IReadOnlySet<string>? committedTree = ancestry.CommittedTreeAtBoundCommit;
        if (committedTree is null)
        {
            return ActivationValidationResult.Reject(
                "committed-tree snapshot at bound commit X is null (cannot prove self-reference-safety)");
        }

        if (committedTree.Contains(receiptPath))
        {
            return ActivationValidationResult.Reject(
                "self-reference: the entry commit X contains its own binding receipt (AP-021)");
        }

        // ---------------------------------------------------------------------------------------
        // Clause 1a (activation-only ATOMIC transition shape) against the merge-base. The head MUST
        // reach lifecycle=ENTERED with a newly-set entry_evidence_pointer, and the schema move must
        // be EXACTLY one of the two legal shapes. Inspects ONLY the parsed DECLARED wire latch
        // (kernel `Lifecycle`) — the derived latch has a single fused consumer elsewhere and is
        // deliberately NOT referenced here (Inv027 cross-doc scan). A v1 base has Lifecycle=Blocked
        // (implicit pre-entry); a v2-BLOCKED base has Lifecycle=Blocked explicitly.
        //
        //   from-v1        : base.schema==1 ∧ head.schema==2 ∧ base BLOCKED ∧ head ENTERED
        //                    ∧ base pointer null ∧ head pointer set
        //   from-v2-BLOCKED: base.schema==2 ∧ base BLOCKED ∧ head.schema==2 ∧ head ENTERED
        //                    ∧ base pointer null ∧ head pointer set
        // ---------------------------------------------------------------------------------------
        bool baseIsPreEntry = mergeBaseBlock.Lifecycle == LifecycleState.Blocked;
        bool headReachesEntered = headBlock.Lifecycle == LifecycleState.Entered;
        bool pointerNewlySet =
            mergeBaseBlock.EntryEvidencePointer is null && headBlock.EntryEvidencePointer is not null;

        bool fromV1 =
            mergeBaseBlock.SchemaVersion == 1 && headBlock.SchemaVersion == 2 &&
            baseIsPreEntry && headReachesEntered && pointerNewlySet;

        bool fromV2Blocked =
            mergeBaseBlock.SchemaVersion == 2 && headBlock.SchemaVersion == 2 &&
            baseIsPreEntry && headReachesEntered && pointerNewlySet;

        if (!fromV1 && !fromV2Blocked)
        {
            // Covers: base already ENTERED (no BLOCKED→ENTERED to perform), head never reached
            // ENTERED / pointer not set, and any schema move that is neither legal shape.
            return ActivationValidationResult.Reject(
                "transition is not an activation-only atomic BLOCKED->ENTERED (from-v1 or from-v2-BLOCKED)");
        }

        // ---------------------------------------------------------------------------------------
        // Clause 1b (everything else UNCHANGED base→head): status, ready_predicate, and — for EVERY
        // precondition — BOTH satisfied AND evidence must be identical. Comparing only `satisfied`
        // would fail OPEN on an evidence-only swap (the load-bearing cross-product cells).
        // ---------------------------------------------------------------------------------------
        if (mergeBaseBlock.Status != headBlock.Status)
        {
            return ActivationValidationResult.Reject("status changed — exceeds the activation-only diff");
        }

        if (!string.Equals(mergeBaseBlock.ReadyPredicate, headBlock.ReadyPredicate, StringComparison.Ordinal))
        {
            return ActivationValidationResult.Reject("ready_predicate changed — exceeds the activation-only diff");
        }

        foreach (PreconditionId id in new[] { PreconditionId.P1, PreconditionId.P2, PreconditionId.P3 })
        {
            ReadinessPrecondition basePc = mergeBaseBlock.Preconditions.First(p => p.Id == id);
            ReadinessPrecondition headPc = headBlock.Preconditions.First(p => p.Id == id);

            if (basePc.Satisfied != headPc.Satisfied ||
                !string.Equals(basePc.Evidence, headPc.Evidence, StringComparison.Ordinal))
            {
                return ActivationValidationResult.Reject(
                    $"precondition {id} changed (satisfied/evidence) — exceeds the activation-only diff");
            }
        }

        // ---------------------------------------------------------------------------------------
        // Clause 1c (changed-paths confinement): the activation must touch ONLY the entry-receipt
        // family — the receipt's own path or anything under the entry-receipt root (the directory
        // of the receipt path, e.g. test/attestations/entry/**). Any path outside that family is an
        // evidence path smuggled into the activation PR → fail closed.
        // ---------------------------------------------------------------------------------------
        string entryRoot = EntryRootOf(receiptPath);
        foreach (string path in changedControlledPaths)
        {
            if (path is null)
            {
                return ActivationValidationResult.Reject("changed controlled path is null (malformed input)");
            }

            bool confined =
                string.Equals(path, receiptPath, StringComparison.Ordinal) ||
                (entryRoot.Length > 0 && path.StartsWith(entryRoot, StringComparison.Ordinal));

            if (!confined)
            {
                return ActivationValidationResult.Reject(
                    $"changed path '{path}' is outside the entry-receipt family — exceeds the activation-only diff");
            }
        }

        // All clauses hold — a legitimate self-reference-safe two-step activation.
        return ActivationValidationResult.Accept();
    }

    /// <summary>
    /// The entry-receipt-family root: the directory prefix of the receipt path (the substring up to
    /// and including the last '/'). A path is confined to the family iff it equals the receipt path
    /// or starts with this root. Returns empty when the receipt path has no directory segment (then
    /// only an exact match is confined).
    /// </summary>
    private static string EntryRootOf(string receiptPath)
    {
        int lastSlash = receiptPath.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : receiptPath.Substring(0, lastSlash + 1);
    }
}
