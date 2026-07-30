using System.Collections.Generic;

namespace Corrected.Gate.Kernel;

/// <summary>
/// The kernel verdict: {Pass | Fail} plus the offending precondition on Fail
/// (INV-004/005). Immutable value.
/// </summary>
public sealed class ReadinessVerdict
{
    private ReadinessVerdict(VerdictKind kind, PreconditionId? offendingPrecondition)
    {
        Kind = kind;
        OffendingPrecondition = offendingPrecondition;
    }

    public VerdictKind Kind { get; }

    /// <summary>The offending precondition on Fail; null on Pass (INV-004).</summary>
    public PreconditionId? OffendingPrecondition { get; }

    internal static ReadinessVerdict Pass() => new(VerdictKind.Pass, null);

    internal static ReadinessVerdict Fail(PreconditionId? offending) => new(VerdictKind.Fail, offending);
}

/// <summary>
/// The PURE, I/O-free decision kernel (INV-004). Lives in the isolated
/// Corrected.Gate.Kernel project. Takes caller-supplied inputs (never the live
/// probes/file) so every branch is reachable with supplied results. No I/O, no
/// clock/culture/RNG, deterministic. Verdict defined by the INV-005 total table.
/// </summary>
public static class ReadinessGate
{
    /// <summary>
    /// Evaluate readiness over a supplied block + supplied probe results
    /// (INV-004/005). Pure and deterministic — no I/O, no ambient state.
    /// </summary>
    public static ReadinessVerdict EvaluateReadiness(
        ReadinessBlock block,
        IReadOnlyDictionary<PreconditionId, ProbeResult> probeResults)
    {
        // status: indeterminate (unparseable, INV-002) -> Fail. The INV-011/INV-036
        // production-src ban is keyed off effective_lifecycle (it stays active while
        // `effective_lifecycle != ENTERED`), NOT off this status; that fused ban+verdict
        // decision lives in the impure LifecycleGate (INV-027), not in this pure kernel.
        if (block.Status == ReadinessStatus.Indeterminate)
        {
            return ReadinessVerdict.Fail(null);
        }

        // Per precondition, in declared order, decide by the INV-005 total table.
        // The FIRST failing cell is the offending precondition.
        foreach (var pc in block.Preconditions)
        {
            if (!probeResults.TryGetValue(pc.Id, out var probe) || probe is null)
            {
                // A missing probe result cannot be reconciled -> fail closed.
                return ReadinessVerdict.Fail(pc.Id);
            }

            if (CellFails(pc, probe))
            {
                return ReadinessVerdict.Fail(pc.Id);
            }
        }

        // status: READY is legal IFF every precondition is ACTUALLY satisfied AND its
        // reference is Resolved (INV-005: "status: READY legal iff every actual true ∧
        // every reference Resolved; else READY → Fail"). The per-cell loop above already
        // fails a declared-true-without-evidence or a declared≠actual mismatch — but a
        // declared-FALSE precondition that is consistently unsatisfied PASSES its cell, and
        // under READY that is still a forged-ready (a block asserting READY while openly
        // declaring P2/P3 unmet). The global READY rule below closes that (QA mini-audit).
        if (block.Status == ReadinessStatus.READY)
        {
            foreach (var pc in block.Preconditions)
            {
                ProbeResult probe = probeResults[pc.Id]; // present + non-null (verified in the loop above)
                if (!probe.Satisfied || probe.ReferenceResolution != ReferenceResolution.Resolved)
                {
                    return ReadinessVerdict.Fail(pc.Id);
                }
            }
        }

        return ReadinessVerdict.Pass();
    }

    /// <summary>
    /// INV-026 component #1: the PURE transition evaluator. PROPOSES a lifecycle
    /// transition {stay-BLOCKED | propose-ENTER | honor-ENTERED} from the 3-tuple
    /// (block, probeResults, entryIntegrity), minting/writing/signing NOTHING. Added
    /// ALONGSIDE the retained 2-arg <see cref="EvaluateReadiness"/> (RS-022), not a
    /// replacement. <paramref name="entryIntegrity"/> is a SUPPLIED enum (the impure
    /// gate-side verifier does the crypto, INV-030) so this stays I/O-free (INV-004).
    ///
    /// State model (Group G tables A/B):
    ///   * declared BLOCKED (at-activation): ProposeEnter IFF P1∧P2∧P3 all re-derive
    ///     true from probeResults AND entryIntegrity==Verified; else StayBlocked.
    ///   * declared ENTERED (established): HonorEntered — the declared latch is
    ///     monotonic, so a transient/rejected/absent integrity still HonorsEntered from
    ///     the EVALUATOR (the separate Pass/Fail verdict is the orchestrator's, 5c).
    /// Safety-direction invariant (INV-026 enforcement): NO at-activation evaluation
    /// with entryIntegrity != Verified EVER yields ProposeEnter.
    /// </summary>
    public static ProposedTransition EvaluateTransition(
        ReadinessBlock block,
        IReadOnlyDictionary<PreconditionId, ProbeResult> probeResults,
        EntryIntegrity entryIntegrity)
    {
        // (B) established-ENTERED: the DECLARED lifecycle latch is monotonic, so a
        // declared-ENTERED block HonorsEntered under EVERY entry_integrity AND every
        // precondition shape — a transient Unavailable never reverts the latch, and a
        // forged Rejected/Absent still yields HonorEntered FROM THIS EVALUATOR (the
        // separate Pass/Fail verdict, computed by the orchestrator in 5c, is what fails
        // the forgery — RS-022). This branch is INDEPENDENT of probeResults/integrity.
        if (block.EffectiveLifecycle == LifecycleState.Entered)
        {
            return ProposedTransition.HonorEntered;
        }

        // (A) at-activation (declared BLOCKED): propose BLOCKED->ENTERED IFF every
        // precondition re-derives true from probeResults AND entry_integrity==Verified.
        // Safety direction (INV-026 / RS-001): compute the guard as a conjunction and
        // deny-by-default — any missing/unsatisfied/unresolvable probe, or any integrity
        // other than Verified, falls through to StayBlocked. Never fail open (AP-001).
        if (entryIntegrity == EntryIntegrity.Verified && AllPreconditionsReDeriveTrue(block, probeResults))
        {
            return ProposedTransition.ProposeEnter;
        }

        return ProposedTransition.StayBlocked;
    }

    /// <summary>
    /// True IFF every declared precondition {P1, P2, P3} "re-derives true" from the
    /// supplied probe results — i.e. its ProbeResult is PRESENT, <c>Satisfied == true</c>,
    /// AND <c>ReferenceResolution == Resolved</c>. A Satisfied-but-Unresolvable/Malformed
    /// probe does NOT re-derive true: per the INV-005 total table a non-Resolved reference
    /// is a hard fail, and a Satisfied+Unresolvable probe is exactly the fail-open shape
    /// INV-026 guards. Missing or null probe rows are treated as not-satisfied (deny by
    /// default). Pure and deterministic — no I/O, no ambient state.
    /// </summary>
    private static bool AllPreconditionsReDeriveTrue(
        ReadinessBlock block,
        IReadOnlyDictionary<PreconditionId, ProbeResult> probeResults)
    {
        foreach (var pc in block.Preconditions)
        {
            if (!probeResults.TryGetValue(pc.Id, out var probe) || probe is null)
            {
                return false;
            }

            if (!probe.Satisfied || probe.ReferenceResolution != ReferenceResolution.Resolved)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The INV-005 per-precondition total-table cell. True == this cell fails.</summary>
    private static bool CellFails(ReadinessPrecondition pc, ProbeResult probe)
    {
        bool declared = pc.Satisfied;
        bool actual = probe.Satisfied;

        if (pc.Evidence is null)
        {
            // evidence == null AND declared true -> Fail (a satisfied claim must cite evidence).
            if (declared)
            {
                return true;
            }

            // evidence == null AND declared false AND actual true -> Fail
            // (BLOCKED-but-actually-satisfied) — the cell that makes the P1 flip mandatory.
            // evidence == null AND declared false AND actual false -> consistent.
            return actual;
        }

        // evidence != null AND referenceResolution in {Unresolvable, Malformed}
        // -> hard Fail regardless of status/declared.
        if (probe.ReferenceResolution != ReferenceResolution.Resolved)
        {
            return true;
        }

        // evidence != null AND Resolved -> cross-check declared vs actual; mismatch
        // either direction -> Fail.
        return declared != actual;
    }
}
