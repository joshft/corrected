using System;
using System.Collections.Generic;
using System.Linq;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Track 5c-i — INV-027 (spec ~1043–1087) + the Group G state tables (A)/(B) (spec
/// ~949–991): parent INV-036's production-code ban is re-keyed to
/// <c>effective_lifecycle</c> (NOT <c>status</c>) and FUSED with the entry_integrity
/// verdict into ONE required gate — the verdict that defeats a forged
/// <c>declared:ENTERED</c> (RS-001).
///
/// Subject: the new impure orchestrator
/// <see cref="LifecycleGate.EvaluateProductionBanAndVerdict"/> in Corrected.Gate,
/// returning <c>(BanActive, Verdict)</c> together. <c>effective_lifecycle</c>,
/// <c>entry_integrity</c> and the src/ <see cref="ScanOutcome"/> are SUPPLIED synthetic
/// inputs here — NO crypto (the gate-side verifier INV-030 / the real scanner INV-011 are
/// out of scope for this track). Reuses the 5a <see cref="ReadinessBlock"/> /
/// <see cref="LifecycleState"/> and the 5b <see cref="EntryIntegrity"/>.
///
/// This track covers ONLY INV-027. It does NOT build: the PRH-007 classifier (5c-ii), the
/// activation-diff validator (5c-iii), INV-028 health/refresh (5d), INV-029/030
/// entry-receipt (5e), or real cosign.
///
/// AP-031 real-artifact clause is NOT triggered: the cross-doc consistency test reads
/// committed PROJECT docs/source (`.correctless/specs/phase-0-1-worker.md`,
/// `.correctless/ARCHITECTURE.md`, kernel source) — none is a `.correctless/artifacts/`
/// producer output from another Correctless skill.
/// </summary>
public class Inv027ProductionBanLifecycleTests
{
    private const string EnteredPointer = "test/attestations/inv010/deadbeef/entry.json";

    // -----------------------------------------------------------------------------------
    // The state-model ORACLE (Group G tables (A)/(B) + INV-027). Derived from the axes, NOT
    // per-cell literals — one closed rule the whole cross-product is measured against
    // (PMB-003 / AP-022: a derived oracle, never a handful of representative rows).
    //
    // Ban decision keys off effective_lifecycle (NOT status): active whenever
    // effective_lifecycle != ENTERED, EXCEPT the accepted first lift (at-activation +
    // Verified) and the monotonic established-ENTERED (always lifted).
    // -----------------------------------------------------------------------------------
    private static (bool BanActive, LifecycleVerdict Verdict) Oracle(
        LifecycleState eff, EntryIntegrity integ, TransitionContext ctx, ScanOutcome scan)
    {
        bool banActive;
        if (eff == LifecycleState.Entered)
        {
            banActive = false; // (B) established-ENTERED: monotonic latch, always lifted.
        }
        else if (ctx == TransitionContext.AtActivation && integ == EntryIntegrity.Verified)
        {
            banActive = false; // (A) first BLOCKED->ENTERED lift accepted ONLY on Verified.
        }
        else
        {
            banActive = true; // plain BLOCKED / READY+BLOCKED / at-activation-not-verified.
        }

        // Ban-scan component: only meaningful while the ban is active. Enumerate the SAFE outcomes
        // (fail-closed on accept, QA-010/PMB-003): VacuousPass (empty) and Pass (declaration-only)
        // satisfy the ban; every other outcome (Fail, ClosureUncomputable, or a future member) trips it.
        bool banViolated = banActive && !(scan == ScanOutcome.VacuousPass || scan == ScanOutcome.Pass);

        // entry_integrity / activation verdict component (tables (A)/(B)).
        LifecycleVerdict integrityVerdict = ctx switch
        {
            // (A) at-activation: verified accepts (success); ANY non-verified is a hard-fail
            // — even a transient Unavailable at FIRST activation (never neutral, RS-001).
            TransitionContext.AtActivation =>
                integ == EntryIntegrity.Verified ? LifecycleVerdict.Success : LifecycleVerdict.HardRedFailure,

            // (B) established-ENTERED: verified→success; transient Unavailable→neutral/degraded;
            // rejected/absent (a forged/tampered declared:ENTERED)→hard-red.
            TransitionContext.EstablishedEntered =>
                integ == EntryIntegrity.Verified ? LifecycleVerdict.Success
                : integ == EntryIntegrity.Unavailable ? LifecycleVerdict.Neutral
                : LifecycleVerdict.HardRedFailure,

            // Plain pre-entry BLOCKED: no activation attempt; the verdict rests on the ban scan.
            _ => LifecycleVerdict.Success,
        };

        // Fuse: hard-red always wins (RS-019); else neutral over success.
        LifecycleVerdict verdict =
            (banViolated || integrityVerdict == LifecycleVerdict.HardRedFailure) ? LifecycleVerdict.HardRedFailure
            : integrityVerdict == LifecycleVerdict.Neutral ? LifecycleVerdict.Neutral
            : LifecycleVerdict.Success;

        return (banActive, verdict);
    }

    private static LifecycleGateResult Eval(
        LifecycleState eff, EntryIntegrity integ, TransitionContext ctx, ScanOutcome scan)
        => LifecycleGate.EvaluateProductionBanAndVerdict(eff, integ, ctx, scan);

    // The three COHERENT (effective_lifecycle, transition_context) states of Group G. The
    // incoherent pairs (Entered+at-activation, Blocked+established-ENTERED) are not real
    // states and are handled by the totality/safety guards below, not enumerated here.
    private static readonly (LifecycleState Eff, TransitionContext Ctx)[] CoherentStates =
    {
        (LifecycleState.Blocked, TransitionContext.EstablishedBlocked),  // plain pre-entry BLOCKED (incl. READY+BLOCKED)
        (LifecycleState.Blocked, TransitionContext.AtActivation),         // first BLOCKED->ENTERED activation (table A)
        (LifecycleState.Entered, TransitionContext.EstablishedEntered),   // established ENTERED (table B)
    };

    public static IEnumerable<object[]> CoherentCells()
    {
        foreach (var (eff, ctx) in CoherentStates)
        {
            foreach (EntryIntegrity integ in Enum.GetValues<EntryIntegrity>())
            {
                foreach (ScanOutcome scan in Enum.GetValues<ScanOutcome>())
                {
                    yield return new object[] { eff, ctx, integ, scan };
                }
            }
        }
    }

    // ===================================================================================
    // TOTALITY CROSS-PRODUCT — every coherent (state × entry_integrity × scan) cell.
    // ===================================================================================

    // Tests INV-027 [integration]: the fused gate returns the state-model (BanActive, Verdict)
    // for EVERY coherent (effective_lifecycle/context × entry_integrity × src-scan) cell.
    // Cells are DERIVED from the committed enums (Enum.GetValues), never an ad-hoc handful
    // (PMB-003 / AP-022). RED against the safe stub for every lifted/success/neutral cell.
    [Theory]
    [MemberData(nameof(CoherentCells))]
    public void Fused_gate_matches_the_state_model_for_every_coherent_cell(
        LifecycleState eff, TransitionContext ctx, EntryIntegrity integ, ScanOutcome scan)
    {
        var (expectedBan, expectedVerdict) = Oracle(eff, integ, ctx, scan);
        LifecycleGateResult r = Eval(eff, integ, ctx, scan);

        Assert.Equal(expectedBan, r.BanActive);
        Assert.Equal(expectedVerdict, r.Verdict);
    }

    // Tests INV-027 [integration]: the SAFETY-DIRECTION invariant (RS-001 / AP-001), the crux
    // of the forged-ENTERED defense. Over the FULL cross-product (INCLUDING incoherent pairs),
    // NO evaluation may fail open:
    //   (1) an at-activation with entry_integrity != Verified must NEVER yield a lifted ban
    //       or a non-hard-red verdict (a first activation must not merge on a fault/tamper);
    //   (2) an active ban over src content (Fail) must NEVER yield a non-hard-red verdict.
    // Passes on the safe stub (deny-by-default); goes RED on ANY fail-open GREEN.
    [Theory]
    [MemberData(nameof(AllCellsIncludingIncoherent))]
    public void No_fail_open_over_the_full_cross_product(
        LifecycleState eff, TransitionContext ctx, EntryIntegrity integ, ScanOutcome scan)
    {
        LifecycleGateResult r = Eval(eff, integ, ctx, scan);

        // (1) at-activation first-lift safety: only Verified may lift / avoid hard-red.
        if (ctx == TransitionContext.AtActivation && integ != EntryIntegrity.Verified)
        {
            Assert.True(r.BanActive, "fail-open: at-activation lifted the ban without a Verified receipt");
            Assert.Equal(LifecycleVerdict.HardRedFailure, r.Verdict);
        }

        // (2) an active ban over real src content must be hard-red (the ban tripped).
        if (r.BanActive && scan == ScanOutcome.Fail)
        {
            Assert.Equal(LifecycleVerdict.HardRedFailure, r.Verdict);
        }
    }

    public static IEnumerable<object[]> AllCellsIncludingIncoherent()
    {
        foreach (LifecycleState eff in Enum.GetValues<LifecycleState>())
        {
            foreach (TransitionContext ctx in Enum.GetValues<TransitionContext>())
            {
                foreach (EntryIntegrity integ in Enum.GetValues<EntryIntegrity>())
                {
                    foreach (ScanOutcome scan in Enum.GetValues<ScanOutcome>())
                    {
                        yield return new object[] { eff, ctx, integ, scan };
                    }
                }
            }
        }
    }

    // Tests INV-027 [unit]: the coherent cross-product ENUMERATES the full committed state
    // space — a count DERIVED from the enums, with a literal PIN that breaks if a member is
    // added to any axis (PMB-003 — a row-count proxy cannot detect an ABSENT cell). 3 coherent
    // states × |EntryIntegrity|=4 × |ScanOutcome|=4 = 48. QA-010: the scan axis now enumerates the
    // FULL committed ScanOutcome (incl. Pass + ClosureUncomputable), not a {VacuousPass, Fail} subset.
    [Fact]
    public void Coherent_cross_product_enumerates_the_full_state_space()
    {
        int derived = CoherentStates.Length
            * Enum.GetValues<EntryIntegrity>().Length
            * Enum.GetValues<ScanOutcome>().Length;
        Assert.Equal(48, derived);
        Assert.Equal(derived, CoherentCells().Count());
    }

    // Tests INV-027 [unit] (QA-011): an unknown / cast / future TransitionContext can NEVER yield the
    // accepting Success verdict — the integrity switch fail-closes to hard-red on its default arm. With
    // a non-tripping scan (VacuousPass) the OLD `_ => Success` default failed OPEN; the fixed default
    // hard-fails, so a new context member added without an explicit arm cannot silently accept.
    [Fact]
    public void Unknown_transition_context_fails_closed_never_success()
    {
        var cast = (TransitionContext)0x7FFF;
        Assert.DoesNotContain(cast, Enum.GetValues<TransitionContext>()); // genuinely out-of-range
        LifecycleGateResult r = Eval(LifecycleState.Blocked, EntryIntegrity.Verified, cast, ScanOutcome.VacuousPass);
        Assert.NotEqual(LifecycleVerdict.Success, r.Verdict);
        Assert.Equal(LifecycleVerdict.HardRedFailure, r.Verdict);
    }

    // ===================================================================================
    // (1) THE BAN KEYS OFF effective_lifecycle, NOT status.
    // ===================================================================================

    // Tests INV-027 [integration]: the ban is IDENTICAL for two blocks differing ONLY in
    // status (BLOCKED vs READY) while both are effective_lifecycle=BLOCKED — proving the ban
    // keys off effective_lifecycle, NOT status. A status-based predicate would wrongly PERMIT
    // src/ at READY+BLOCKED (the entry commit X: preconditions satisfied, activation not yet
    // signed). Built from REAL v2 ReadinessBlocks; the gate never receives status. RED against
    // the (safe-stub) READY+BLOCKED cell is not needed — the ASSERTION here is equality +
    // ban-trips, which fails only if a GREEN keys off status. Passes on the safe stub.
    [Fact]
    public void Ban_keys_off_effective_lifecycle_not_status_blocked_and_ready_are_identical()
    {
        ReadinessBlock blocked = BuildV2Block(ReadinessStatus.BLOCKED, LifecycleState.Blocked);
        ReadinessBlock ready = BuildV2Block(ReadinessStatus.READY, LifecycleState.Blocked);

        // The READY+BLOCKED state: status flipped to READY, lifecycle STILL BLOCKED (pre-entry).
        Assert.Equal(ReadinessStatus.READY, ready.Status);
        Assert.Equal(LifecycleState.Blocked, ready.EffectiveLifecycle);
        Assert.Equal(LifecycleState.Blocked, blocked.EffectiveLifecycle);

        LifecycleGateResult rBlocked = Eval(
            blocked.EffectiveLifecycle, EntryIntegrity.Absent, TransitionContext.EstablishedBlocked, ScanOutcome.Fail);
        LifecycleGateResult rReady = Eval(
            ready.EffectiveLifecycle, EntryIntegrity.Absent, TransitionContext.EstablishedBlocked, ScanOutcome.Fail);

        // Same effective_lifecycle -> same ban decision + verdict, whatever the status.
        Assert.Equal(rBlocked.BanActive, rReady.BanActive);
        Assert.Equal(rBlocked.Verdict, rReady.Verdict);
        // And with src content present, the ban TRIPS in both (hard-red).
        Assert.True(rReady.BanActive);
        Assert.Equal(LifecycleVerdict.HardRedFailure, rReady.Verdict);
    }

    // Tests INV-027 [integration]: the fused gate's signature CANNOT key off status — the
    // method takes (LifecycleState, EntryIntegrity, TransitionContext, ScanOutcome) and NO
    // ReadinessStatus (nor a whole ReadinessBlock through which status could leak). Structural
    // proof of "keys off effective_lifecycle, not status". Passes on the stub.
    [Fact]
    public void Fused_gate_signature_takes_no_status()
    {
        var m = typeof(LifecycleGate).GetMethod(nameof(LifecycleGate.EvaluateProductionBanAndVerdict));
        Assert.NotNull(m);
        var paramTypes = m!.GetParameters().Select(p => p.ParameterType).ToArray();
        Assert.DoesNotContain(typeof(ReadinessStatus), paramTypes);
        Assert.DoesNotContain(typeof(ReadinessBlock), paramTypes);
        // It DOES co-require the effective_lifecycle latch AND the entry_integrity verdict (fusion).
        Assert.Contains(typeof(LifecycleState), paramTypes);
        Assert.Contains(typeof(EntryIntegrity), paramTypes);
    }

    // ===================================================================================
    // (3) THE NAMED FIXTURE MATRIX — one readable test per spec cell.
    // ===================================================================================

    // Tests INV-027 [integration]: BLOCKED + src-content -> the ban TRIPS (hard-red).
    [Fact]
    public void Blocked_with_src_content_trips_the_ban()
    {
        LifecycleGateResult r = Eval(
            LifecycleState.Blocked, EntryIntegrity.Absent, TransitionContext.EstablishedBlocked, ScanOutcome.Fail);
        Assert.True(r.BanActive);
        Assert.Equal(LifecycleVerdict.HardRedFailure, r.Verdict);
    }

    // Tests INV-027 [integration]: READY + BLOCKED + src-content -> the ban TRIPS (the KEY new
    // cell). status=READY but effective_lifecycle=BLOCKED: production src/ is still banned
    // pre-entry. RED against the stub? No — the stub's safe (true, HardRed) matches; this
    // fails only on a status-keyed GREEN. The effective_lifecycle=BLOCKED is derived from a
    // REAL READY-status v2 block.
    [Fact]
    public void Ready_but_blocked_with_src_content_trips_the_ban()
    {
        ReadinessBlock ready = BuildV2Block(ReadinessStatus.READY, LifecycleState.Blocked);
        Assert.Equal(ReadinessStatus.READY, ready.Status);
        Assert.Equal(LifecycleState.Blocked, ready.EffectiveLifecycle);

        LifecycleGateResult r = Eval(
            ready.EffectiveLifecycle, EntryIntegrity.Absent, TransitionContext.EstablishedBlocked, ScanOutcome.Fail);
        Assert.True(r.BanActive);
        Assert.Equal(LifecycleVerdict.HardRedFailure, r.Verdict);
    }

    // Tests INV-027 [integration]: ENTERED + src-content + entry_integrity=Verified -> the ban
    // is LIFTED and the verdict is success. RED against the safe stub (which keeps the ban on).
    [Fact]
    public void Entered_verified_with_src_content_lifts_ban_and_succeeds()
    {
        LifecycleGateResult r = Eval(
            LifecycleState.Entered, EntryIntegrity.Verified, TransitionContext.EstablishedEntered, ScanOutcome.Fail);
        Assert.False(r.BanActive);
        Assert.Equal(LifecycleVerdict.Success, r.Verdict);
    }

    // Tests INV-027 [integration]: ENTERED + src-content + entry_integrity=Unavailable
    // (established, TRANSIENT outage) -> the ban stays LIFTED (monotonic — a transient outage
    // NEVER re-bans existing src/) and the verdict is neutral/degraded. RED against the stub.
    [Fact]
    public void Established_entered_transient_unavailable_stays_lifted_neutral()
    {
        LifecycleGateResult r = Eval(
            LifecycleState.Entered, EntryIntegrity.Unavailable, TransitionContext.EstablishedEntered, ScanOutcome.Fail);
        Assert.False(r.BanActive); // monotonic — src/ NOT re-banned.
        Assert.Equal(LifecycleVerdict.Neutral, r.Verdict);
    }

    // Tests INV-027 [integration]: BLOCKED + empty-src (VacuousPass) -> ALLOWED. The ban is in
    // force but no content is present, so the gate does not fail (the normal pre-entry state).
    // RED against the stub (whose safe default is hard-red).
    [Fact]
    public void Blocked_with_empty_src_is_allowed()
    {
        LifecycleGateResult r = Eval(
            LifecycleState.Blocked, EntryIntegrity.Absent, TransitionContext.EstablishedBlocked, ScanOutcome.VacuousPass);
        Assert.True(r.BanActive);                          // ban in force pre-entry...
        Assert.Equal(LifecycleVerdict.Success, r.Verdict); // ...but satisfied (empty src) -> allowed.
    }

    // Tests INV-027 [integration]: a FORGED declared:ENTERED + src-content + entry_integrity ∈
    // {Rejected, Absent} -> the FUSED gate is HARD-RED (the verdict fails), so src/ CANNOT
    // land. The monotonic ban is "lifted (moot)" — safe ONLY because the co-required
    // entry_integrity verdict fails in the SAME gate (the RS-001 forged-ENTERED defense). RED
    // against the stub (banActive mismatch: expects lifted-but-moot).
    [Theory]
    [InlineData(EntryIntegrity.Rejected)]
    [InlineData(EntryIntegrity.Absent)]
    public void Forged_declared_entered_is_hard_red_ban_lift_moot(EntryIntegrity integ)
    {
        LifecycleGateResult r = Eval(
            LifecycleState.Entered, integ, TransitionContext.EstablishedEntered, ScanOutcome.Fail);
        // Ban-lift is MOOT (monotonic latch); the forgery gains nothing because...
        Assert.False(r.BanActive);
        // ...the FUSED verdict is hard-red -> src/ cannot land.
        Assert.Equal(LifecycleVerdict.HardRedFailure, r.Verdict);
    }

    // Tests INV-027 [integration]: at-activation (first BLOCKED->ENTERED) with entry_integrity
    // ∈ {Unavailable, Rejected, Absent} -> activation NOT accepted, HARD-RED, ban NOT lifted.
    // Even a TRANSIENT Unavailable at first activation is a hard-fail (NEVER neutral) — this is
    // the distinction from the established-ENTERED transient row. Passes on the safe stub
    // (deny-by-default); goes RED on a fail-open GREEN.
    [Theory]
    [InlineData(EntryIntegrity.Unavailable)]
    [InlineData(EntryIntegrity.Rejected)]
    [InlineData(EntryIntegrity.Absent)]
    public void At_activation_nonverified_is_hard_red_ban_not_lifted(EntryIntegrity integ)
    {
        LifecycleGateResult r = Eval(
            LifecycleState.Blocked, integ, TransitionContext.AtActivation, ScanOutcome.Fail);
        Assert.True(r.BanActive); // activation didn't happen -> ban NOT lifted.
        Assert.Equal(LifecycleVerdict.HardRedFailure, r.Verdict);
    }

    // Tests INV-027 [integration]: the at-activation Unavailable row and the established-ENTERED
    // Unavailable row are DISTINCT — same entry_integrity, OPPOSITE outcome. First activation:
    // hard-red, ban not lifted. Established: neutral, ban lifted. Directly pins that the fused
    // gate is a transition_context × entry_integrity cross-product, not a single-axis table
    // (the AP-022 fail-open the split defends). RED against the stub for the established row.
    [Fact]
    public void At_activation_unavailable_differs_from_established_entered_unavailable()
    {
        LifecycleGateResult atActivation = Eval(
            LifecycleState.Blocked, EntryIntegrity.Unavailable, TransitionContext.AtActivation, ScanOutcome.Fail);
        LifecycleGateResult established = Eval(
            LifecycleState.Entered, EntryIntegrity.Unavailable, TransitionContext.EstablishedEntered, ScanOutcome.Fail);

        Assert.True(atActivation.BanActive);
        Assert.Equal(LifecycleVerdict.HardRedFailure, atActivation.Verdict);

        Assert.False(established.BanActive);
        Assert.Equal(LifecycleVerdict.Neutral, established.Verdict);

        Assert.NotEqual(atActivation.Verdict, established.Verdict);
    }

    // Tests INV-027 [integration]: at-activation + Verified -> activation ACCEPTED, the FIRST
    // lift, verdict success (table (A) verified row). This is the ONLY at-activation cell that
    // lifts the ban. RED against the safe stub.
    [Fact]
    public void At_activation_verified_accepts_and_lifts()
    {
        LifecycleGateResult r = Eval(
            LifecycleState.Blocked, EntryIntegrity.Verified, TransitionContext.AtActivation, ScanOutcome.Fail);
        Assert.False(r.BanActive);
        Assert.Equal(LifecycleVerdict.Success, r.Verdict);
    }

    // Tests INV-027 [integration]: DEFENSIVE — while the ban is active, an UNCOMPUTABLE closure
    // (fail-closed, distinct from empty) is a hard-red violation, never allowed. Guards the
    // fail-open where an uncomputable scan is mistaken for "no content". RED against the stub?
    // The stub's safe (true, HardRed) matches; this fails only on a fail-open GREEN.
    [Fact]
    public void Blocked_with_uncomputable_closure_is_hard_red()
    {
        LifecycleGateResult r = Eval(
            LifecycleState.Blocked, EntryIntegrity.Absent, TransitionContext.EstablishedBlocked, ScanOutcome.ClosureUncomputable);
        Assert.True(r.BanActive);
        Assert.Equal(LifecycleVerdict.HardRedFailure, r.Verdict);
    }

    // Tests INV-027 [integration]: MONOTONICITY — once established-ENTERED, the ban stays
    // lifted under EVERY entry_integrity (a transient/rejected/absent integrity never
    // re-bans existing src/). Only the verdict changes. Parametrized over all four integrities.
    // RED against the stub (which re-bans).
    [Theory]
    [InlineData(EntryIntegrity.Verified)]
    [InlineData(EntryIntegrity.Unavailable)]
    [InlineData(EntryIntegrity.Rejected)]
    [InlineData(EntryIntegrity.Absent)]
    public void Established_entered_ban_is_monotonic_under_every_integrity(EntryIntegrity integ)
    {
        LifecycleGateResult r = Eval(
            LifecycleState.Entered, integ, TransitionContext.EstablishedEntered, ScanOutcome.Fail);
        Assert.False(r.BanActive); // NEVER re-banned.
    }

    // ===================================================================================
    // ENUM VOCABULARY — set-equality (not count/presence proxy, PMB-003/AP-022). Scaffolding.
    // ===================================================================================

    // Tests INV-027 [unit]: TransitionContext is EXACTLY the three Group G contexts.
    [Fact]
    public void TransitionContext_is_exactly_the_three_contexts()
    {
        Assert.Equal(
            new HashSet<TransitionContext>
            {
                TransitionContext.EstablishedBlocked,
                TransitionContext.AtActivation,
                TransitionContext.EstablishedEntered,
            },
            Enum.GetValues<TransitionContext>().ToHashSet());
    }

    // Tests INV-027 [unit]: the fused LifecycleVerdict is EXACTLY {Success, Neutral,
    // HardRedFailure} — the three CI-conclusion classes (success / neutral-degraded / hard-red).
    [Fact]
    public void LifecycleVerdict_is_exactly_the_three_conclusions()
    {
        Assert.Equal(
            new HashSet<LifecycleVerdict>
            {
                LifecycleVerdict.Success, LifecycleVerdict.Neutral, LifecycleVerdict.HardRedFailure,
            },
            Enum.GetValues<LifecycleVerdict>().ToHashSet());
    }

    // -----------------------------------------------------------------------------------
    // Synthetic v2 block builder. status is a FREE axis; lifecycle is the declared latch.
    // -----------------------------------------------------------------------------------
    private static ReadinessBlock BuildV2Block(ReadinessStatus status, LifecycleState lifecycle)
    {
        var pcs = new[]
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", true, "gate-id-1", Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", true, "gate-id-2", Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", true, "gate-id-3", Array.Empty<string>()),
        };
        string? pointer = lifecycle == LifecycleState.Entered ? EnteredPointer : null;
        ReadinessBlock? b = ReadinessBlock.TryCreate(
            2, status, "P1 AND P2 AND P3", pcs, lifecycle, pointer);
        Assert.NotNull(b); // fixture-construction guard, not a RED assertion.
        return b!;
    }
}
