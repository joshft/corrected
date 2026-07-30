using System.Collections.Generic;

namespace Corrected.Gate;

/// <summary>
/// The SEVEN typed post-entry determinism health findings (INV-028 <c>current_health</c>,
/// spec ~938–947). <c>current_health</c> is a SET of typed findings — staleness, a P1/P2
/// regression, and a live disagreement can co-occur simultaneously, so this is a set axis,
/// not one enum. The set is PINNED (PMB-003 / AP-022): exactly these seven, with NO
/// default/sentinel/"ok" member (a transient verifier outage is <see cref="P3VerifierUnavailable"/>,
/// NEVER represented as ok). Each kind carries a <see cref="HealthSeverity"/> via
/// <see cref="PostEntryHealth.SeverityOf"/>.
/// </summary>
public enum HealthFindingKind
{
    /// <summary>Stale baseline vs current relevant state (advisory — never a required merge-blocker).</summary>
    RefreshRequired,

    /// <summary>Resource-floor skip (advisory).</summary>
    ResourceFloorSkipped,

    /// <summary>The current P3 verifier/root is transiently unreadable (advisory; retryable; NEVER ok).</summary>
    P3VerifierUnavailable,

    /// <summary>A live two-run projection diff (hard-red — a real disagreement fails the required gate).</summary>
    Disagreement,

    /// <summary>Runner / tool fault (hard-red).</summary>
    InfrastructureInvalid,

    /// <summary>An entry/P3 receipt is rejected/tampered (hard-red).</summary>
    EvidenceIntegrityRejected,

    /// <summary>A post-entry P1 or P2 regression (hard-red — health models these too, not only P3).</summary>
    PreconditionRegression,
}

/// <summary>
/// The severity a <see cref="HealthFindingKind"/> carries (INV-028). Advisory findings fold
/// to a neutral/degraded conclusion; hard-red findings fold to a required-gate failure. The
/// kind→severity map (<see cref="PostEntryHealth.SeverityOf"/>) is TOTAL over the seven kinds
/// with no default fallthrough.
/// </summary>
public enum HealthSeverity
{
    Advisory,
    HardRed,
}

/// <summary>
/// INV-028 post-entry determinism HEALTH model + folds (spec ~938–957). Health is a SET of
/// typed findings; the fold reduces the whole set to a CI-conclusion class, and the composed
/// overall conclusion is the hard-red-wins fold of the (A)/(B) readiness verdict (obtained
/// from <see cref="LifecycleGate"/> — single-sourced, never re-derived here) with the health
/// fold. This component builds ONLY the health model + fold + composition; the closed pointer
/// schema + dangling-pointer coupling (RS-029) is a SEPARATE later sub-track.
///
/// All inputs are SYNTHETIC enum/set values — this component does NO I/O and NO crypto.
/// </summary>
public static class PostEntryHealth
{
    /// <summary>
    /// The TOTAL, pinned kind→severity map (INV-028): Advisory = {RefreshRequired,
    /// ResourceFloorSkipped, P3VerifierUnavailable}; HardRed = {Disagreement,
    /// InfrastructureInvalid, EvidenceIntegrityRejected, PreconditionRegression}. Exhaustive
    /// over <see cref="HealthFindingKind"/> — no default fallthrough (a new kind must be
    /// classified explicitly, PMB-003).
    /// </summary>
    public static HealthSeverity SeverityOf(HealthFindingKind kind) => kind switch
    {
        // Advisory — degraded/transient; folds to a neutral conclusion, never a required blocker.
        HealthFindingKind.RefreshRequired => HealthSeverity.Advisory,
        HealthFindingKind.ResourceFloorSkipped => HealthSeverity.Advisory,
        HealthFindingKind.P3VerifierUnavailable => HealthSeverity.Advisory,

        // Hard-red — a real failure of the required gate; folds to HardRedFailure.
        HealthFindingKind.Disagreement => HealthSeverity.HardRed,
        HealthFindingKind.InfrastructureInvalid => HealthSeverity.HardRed,
        HealthFindingKind.EvidenceIntegrityRejected => HealthSeverity.HardRed,
        HealthFindingKind.PreconditionRegression => HealthSeverity.HardRed,

        // No default arm ON PURPOSE (PMB-003 / AP-022): every kind is classified explicitly, so a
        // future added HealthFindingKind member surfaces as a compiler diagnostic here (an
        // unhandled-value switch), NOT a silent fallthrough that would mis-classify it.
    };

    /// <summary>
    /// The HEALTH FOLD over the whole finding SET (INV-028 conclusion fold): any hard-red kind
    /// → <see cref="LifecycleVerdict.HardRedFailure"/>; else any advisory kind →
    /// <see cref="LifecycleVerdict.Neutral"/>; else (empty set) →
    /// <see cref="LifecycleVerdict.Success"/>. A co-occurring advisory NEVER downgrades a
    /// hard-red finding.
    /// </summary>
    public static LifecycleVerdict FoldHealth(IReadOnlySet<HealthFindingKind> health)
    {
        // Deny-by-default guard: a null finding set is not a clean state — it is an ill-formed
        // input. Fail loud rather than let a NullReferenceException surface from the fold below or
        // (worse) silently read as an empty/clean set.
        if (health is null)
        {
            throw new System.ArgumentNullException(nameof(health));
        }

        // Hard-red WINS the whole-set fold (RS-019): if ANY finding is hard-red, a co-occurring
        // advisory can never soften it.
        foreach (HealthFindingKind kind in health)
        {
            if (SeverityOf(kind) == HealthSeverity.HardRed)
            {
                return LifecycleVerdict.HardRedFailure;
            }
        }

        // No hard-red finding. A NON-EMPTY set (all advisory) folds to neutral; the empty set is
        // the only clean/success state.
        return health.Count > 0 ? LifecycleVerdict.Neutral : LifecycleVerdict.Success;
    }

    /// <summary>
    /// The COMPOSED overall post-entry conclusion (INV-028 / RS-019): the hard-red-wins fold of
    /// the supplied readiness verdict (the (A)/(B) row, obtained from <see cref="LifecycleGate"/>)
    /// with <see cref="FoldHealth"/>. Precedence HardRedFailure &gt; Neutral &gt; Success — i.e.
    /// the MAX. A neutral entry-integrity row NEVER downgrades a hard-red health finding. This
    /// single-sources the (A)/(B) table via <see cref="LifecycleGate"/> and never re-derives it.
    /// </summary>
    public static LifecycleVerdict FoldOverallConclusion(
        LifecycleVerdict lifecycleVerdict, IReadOnlySet<HealthFindingKind> health)
    {
        // FoldHealth guards null; let it raise so the composition never silently absorbs a bad set.
        LifecycleVerdict healthVerdict = FoldHealth(health);

        // MAX by the EXPLICIT precedence HardRedFailure > Neutral > Success (RS-019 "hard-red always
        // wins"). Ranked explicitly rather than trusting the enum's numeric order — the safety
        // property (a neutral readiness verdict + a hard-red health set → HardRedFailure) must hold
        // regardless of how the enum is declared.
        return Rank(lifecycleVerdict) >= Rank(healthVerdict) ? lifecycleVerdict : healthVerdict;
    }

    /// <summary>
    /// The precedence rank of a verdict for the hard-red-wins MAX (RS-019):
    /// HardRedFailure (2) &gt; Neutral (1) &gt; Success (0). Total over the three verdicts, no
    /// default fallthrough — a new verdict must be ranked explicitly.
    /// </summary>
    private static int Rank(LifecycleVerdict verdict) => verdict switch
    {
        LifecycleVerdict.Success => 0,
        LifecycleVerdict.Neutral => 1,
        LifecycleVerdict.HardRedFailure => 2,
    };
}
