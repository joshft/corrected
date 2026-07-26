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
        // status: indeterminate (unparseable, INV-002) -> Fail; the INV-011 ban
        // stays active while status in {BLOCKED, indeterminate}.
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
