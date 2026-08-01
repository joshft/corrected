using Corrected.Gate.Kernel;

namespace Corrected.Gate;

/// <summary>
/// The evaluation context of a readiness block for the FUSED production-ban + verdict
/// gate (INV-027, Group G state model tables (A)/(B), spec ~949–991). Three states:
///   * <see cref="EstablishedBlocked"/> — a plain pre-entry BLOCKED (incl. the
///     <c>READY + BLOCKED</c> entry-commit state); not attempting activation. The src/
///     ban is IN FORCE; the verdict rests on the ban scan (no activation receipt).
///   * <see cref="AtActivation"/> — the PR proposes the FIRST <c>BLOCKED→ENTERED</c>
///     activation (merge-base lifecycle=BLOCKED). Table (A): the activation — and the
///     first ban-lift — is accepted ONLY on <c>entry_integrity=Verified</c>; anything
///     else FAILS CLOSED (hard-red, ban NOT lifted), even a transient <c>Unavailable</c>.
///   * <see cref="EstablishedEntered"/> — the merge-base is already lifecycle=ENTERED.
///     Table (B): the DECLARED latch is monotonic so the ban stays lifted under EVERY
///     entry_integrity (a transient outage never re-bans existing src/); only the verdict
///     changes.
/// The two spec-named contexts are at-activation and established-ENTERED; the plain
/// pre-entry BLOCKED baseline is the trivial "ban active, no activation" state.
/// </summary>
public enum TransitionContext
{
    EstablishedBlocked,
    AtActivation,
    EstablishedEntered,
}

/// <summary>
/// The FUSED readiness verdict of <see cref="LifecycleGate"/> (INV-027 / RS-001). Distinct
/// from the kernel's Pass/Fail <see cref="Corrected.Gate.Kernel.ReadinessVerdict"/>: this
/// is the CI-conclusion class of the fused ban+integrity gate.
///   * <see cref="Success"/> — the ban is satisfied/lifted AND entry_integrity is verified.
///   * <see cref="Neutral"/> — degraded: the ban stays lifted (established-ENTERED) but a
///     TRANSIENT <c>entry_integrity=Unavailable</c> fails closed to neutral (not a
///     merge-blocker; never a false-green).
///   * <see cref="HardRedFailure"/> — the ban tripped (src/ content while banned) OR the
///     entry_integrity verdict is a hard-red failure (rejected/absent, or ANY non-verified
///     first activation). Hard-red always wins the fold (RS-019).
/// </summary>
public enum LifecycleVerdict
{
    Success,
    Neutral,
    HardRedFailure,
}

/// <summary>
/// The immutable result of the fused gate: the src/ ban decision AND the readiness verdict,
/// produced TOGETHER by a single call (INV-027 — the ban and the entry_integrity verdict are
/// FUSED; there is no standalone consumer of <c>effective_lifecycle</c> that lifts the ban
/// without co-requiring the verdict).
/// </summary>
public sealed class LifecycleGateResult
{
    private LifecycleGateResult(bool banActive, LifecycleVerdict verdict)
    {
        BanActive = banActive;
        Verdict = verdict;
    }

    /// <summary>True iff the production src/ ban is IN FORCE (src/ must be empty); false iff lifted.</summary>
    public bool BanActive { get; }

    /// <summary>The fused CI-conclusion verdict (ban-scan folded with the entry_integrity verdict).</summary>
    public LifecycleVerdict Verdict { get; }

    internal static LifecycleGateResult Create(bool banActive, LifecycleVerdict verdict)
        => new(banActive, verdict);
}

/// <summary>
/// INV-027: the impure orchestrator that FUSES the production-code ban (parent INV-036,
/// re-keyed to <c>effective_lifecycle</c>, NOT <c>status</c>) with the entry_integrity
/// verdict into ONE required gate. The src/ ban is active whenever
/// <c>effective_lifecycle != ENTERED</c> — including the <c>READY + BLOCKED</c> pre-entry
/// state (a status-based predicate would wrongly permit src/ there). The FIRST lift (a
/// <c>BLOCKED→ENTERED</c> activation) is accepted ONLY on <c>entry_integrity=Verified</c>
/// at activation; a forged <c>declared:ENTERED</c> yields <c>rejected/absent</c> integrity
/// → the fused gate is hard-red so src/ cannot land, even though the monotonic ban-lift is
/// moot (the RS-001 forged-ENTERED defense). Once established-ENTERED, the monotonic latch
/// governs the ban so a transient <c>Unavailable</c> never re-bans existing src/.
///
/// <c>effectiveLifecycle</c>, <c>entryIntegrity</c> and <c>srcScan</c> are SUPPLIED inputs
/// (the crypto/verification lives in the gate-side verifier, INV-030; the scan in
/// <see cref="ProductionSurfaceScanner"/>, INV-011). This function takes NO <c>status</c> —
/// it cannot key the ban off status by construction.
/// </summary>
public static class LifecycleGate
{
    /// <summary>
    /// Evaluate the fused production-ban + readiness verdict over supplied synthetic inputs.
    /// Returns the src/ ban decision AND the fused verdict together (INV-027).
    /// </summary>
    public static LifecycleGateResult EvaluateProductionBanAndVerdict(
        LifecycleState effectiveLifecycle,
        EntryIntegrity entryIntegrity,
        TransitionContext transitionContext,
        ScanOutcome srcScan)
    {
        // Ban decision — keyed off effective_lifecycle (NOT status). The ban is active
        // whenever effective_lifecycle != ENTERED, EXCEPT (A) the accepted first lift
        // (at-activation + Verified) and (B) the monotonic established-ENTERED latch.
        //
        // The at-activation rule is evaluated FIRST so the first-lift fail-closed
        // (RS-001: only a Verified receipt may lift at activation) takes precedence even
        // over an (incoherent) Entered+at-activation pair — a first activation must never
        // lift the ban on a fault/tamper. For every COHERENT cell this yields exactly the
        // state-model latch: at-activation lifts iff Verified, established-ENTERED always
        // lifts, plain BLOCKED/READY+BLOCKED stays banned.
        bool banActive;
        if (transitionContext == TransitionContext.AtActivation)
        {
            // (A) first BLOCKED->ENTERED lift accepted ONLY on a Verified activation;
            // anything else fails closed (ban NOT lifted).
            banActive = entryIntegrity != EntryIntegrity.Verified;
        }
        else if (effectiveLifecycle == LifecycleState.Entered)
        {
            // (B) established-ENTERED: monotonic latch — always lifted; a transient
            // outage never re-bans existing src/.
            banActive = false;
        }
        else
        {
            // plain BLOCKED / READY+BLOCKED: ban in force pre-entry.
            banActive = true;
        }

        // Ban-scan component: only meaningful while the ban is active. Enumerate the SAFE outcomes
        // (fail-closed on accept, QA-010/PMB-003): the ban is satisfied ONLY by VacuousPass (empty
        // surface) or Pass (a non-empty but deliberately DECLARATION-ONLY closure, permitted
        // pre-entry by the scanner's ContainsOnlyDeclarations design). Every other outcome — content
        // (Fail), an uncomputable closure (ClosureUncomputable), OR any future ScanOutcome member —
        // trips the ban -> hard-red.
        bool banViolated = banActive
            && !(srcScan == ScanOutcome.VacuousPass || srcScan == ScanOutcome.Pass);

        // entry_integrity / activation verdict component (state tables (A)/(B)).
        LifecycleVerdict integrityVerdict = transitionContext switch
        {
            // (A) at-activation: Verified accepts (success); ANY non-verified is a hard-fail
            // — even a transient Unavailable at FIRST activation (never neutral, RS-001).
            TransitionContext.AtActivation =>
                entryIntegrity == EntryIntegrity.Verified
                    ? LifecycleVerdict.Success
                    : LifecycleVerdict.HardRedFailure,

            // (B) established-ENTERED: Verified->success; transient Unavailable->neutral
            // (degraded, src/ NOT re-banned); rejected/absent (forged/tampered
            // declared:ENTERED)->hard-red so the fused gate fails and src/ cannot land.
            TransitionContext.EstablishedEntered =>
                entryIntegrity == EntryIntegrity.Verified ? LifecycleVerdict.Success
                : entryIntegrity == EntryIntegrity.Unavailable ? LifecycleVerdict.Neutral
                : LifecycleVerdict.HardRedFailure,

            // Plain pre-entry BLOCKED: no activation attempt; the verdict rests on the ban scan.
            TransitionContext.EstablishedBlocked => LifecycleVerdict.Success,

            // Fail-closed default (QA-011/PMB-003): an unknown / cast / future TransitionContext can
            // never yield the accepting Success verdict — it hard-fails.
            _ => LifecycleVerdict.HardRedFailure,
        };

        // Fuse: hard-red always wins the fold (RS-019); else neutral over success.
        LifecycleVerdict verdict =
            (banViolated || integrityVerdict == LifecycleVerdict.HardRedFailure)
                ? LifecycleVerdict.HardRedFailure
                : integrityVerdict == LifecycleVerdict.Neutral
                    ? LifecycleVerdict.Neutral
                    : LifecycleVerdict.Success;

        return LifecycleGateResult.Create(banActive, verdict);
    }
}
