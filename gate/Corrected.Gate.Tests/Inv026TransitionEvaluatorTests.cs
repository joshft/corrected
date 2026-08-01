using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Track 5b — INV-026 component #1: the PURE transition evaluator
/// <see cref="ReadinessGate.EvaluateTransition"/> and its Group G state-machine
/// cross-product (spec ~993–1041 + the state-model tables (A)/(B) ~949–991).
///
/// The evaluator PROPOSES a transition {StayBlocked | ProposeEnter | HonorEntered}
/// from the 3-tuple (block, probeResults, entryIntegrity), minting/writing NOTHING.
/// entryIntegrity is a SUPPLIED enum (the impure verifier INV-030 does the crypto),
/// so these tests need NO crypto — synthetic tuples only.
///
/// This track does NOT cover: the impure orchestrator/entry-verifier (INV-030), the
/// ProductionSurfaceScanner re-key (INV-027 enforcement), health/refresh (INV-028), or
/// real cosign. The evaluator here emits ONLY the transition proposal — never the
/// Pass/Fail verdict (that is the orchestrator's, a later sub-track).
///
/// State model encoded (Group G tables A/B, spec ~968–991):
///   (A) declared BLOCKED (at-activation): ProposeEnter IFF P1∧P2∧P3 all re-derive
///       true from probeResults AND entryIntegrity==Verified; anything else StayBlocked.
///   (B) declared ENTERED (established): HonorEntered under EVERY entryIntegrity — the
///       declared latch is monotonic; a transient Unavailable still honors ENTERED, and
///       a forged Rejected/Absent still returns HonorEntered FROM THE EVALUATOR (the
///       separate verdict, computed by the orchestrator in 5c, is what fails).
///   Safety-direction invariant (INV-026 enforcement, RS-001): NO at-activation
///   (declared-BLOCKED) evaluation with entryIntegrity != Verified EVER yields
///   ProposeEnter. Asserted exhaustively over the cross-product.
///
/// Fixtures are SYNTHETIC (AP-031 real-artifact clause DORMANT — there is no committed
/// v2/ENTERED producer artifact yet; the parent block is v1 through P1/P2/P3). Blocks
/// are built via the 5a API: v1/implicit-Blocked for declared BLOCKED,
/// TryCreate(..., LifecycleState.Entered, pointer) for declared ENTERED.
/// </summary>
public class Inv026TransitionEvaluatorTests
{
    private const string EnteredPointer = ".correctless/receipts/phase-entry/entry.json";

    // ---------------------------------------------------------------------------------
    // Synthetic fixture builders. "all-true" vs "some-false" is the {preconditions}
    // dimension. It is defined by the PROBE RESULTS (the evaluator re-derives P1∧P2∧P3
    // "from probeResults", spec ~1000/1031) — the declared precondition rows are kept
    // CONSISTENT with the probes (belt-and-suspenders) so the "all re-derive true"
    // notion is unambiguous however GREEN computes it.
    // ---------------------------------------------------------------------------------

    // Probe map. all-true => P1∧P2∧P3 all Satisfied+Resolved. some-false => P2 flipped
    // to unsatisfied (P1∧P2∧P3 = true∧false∧true = false, i.e. NOT all re-derive true).
    private static IReadOnlyDictionary<PreconditionId, ProbeResult> Probes(bool allTrue)
    {
        ProbeResult P(bool satisfied) =>
            ProbeResult.TryCreate(satisfied, "probe", ReferenceResolution.Resolved)!;
        return new Dictionary<PreconditionId, ProbeResult>
        {
            [PreconditionId.P1] = P(true),
            [PreconditionId.P2] = P(allTrue),
            [PreconditionId.P3] = P(true),
        };
    }

    private static ReadinessPrecondition[] Pcs(bool allTrue) => new[]
    {
        ReadinessPrecondition.Create(PreconditionId.P1, "p1", true, "gate-id-1", Array.Empty<string>()),
        ReadinessPrecondition.Create(PreconditionId.P2, "p2", allTrue, allTrue ? "gate-id-2" : null, Array.Empty<string>()),
        ReadinessPrecondition.Create(PreconditionId.P3, "p3", true, "gate-id-3", Array.Empty<string>()),
    };

    // A block with the given DECLARED lifecycle + precondition-satisfaction shape.
    //  * declared BLOCKED: v1 / implicit-Blocked (EffectiveLifecycle == Blocked). all-true
    //    realizes the INV-027 scenario (entry commit X: status=READY while declared
    //    lifecycle=BLOCKED); some-false is a plain BLOCKED block.
    //  * declared ENTERED: v2 via the 5a API (pointer REQUIRED). Built for BOTH all-true
    //    and some-false to prove HonorEntered is INDEPENDENT of precondition state
    //    (the monotonic latch — even a regressed ENTERED still honors).
    private static ReadinessBlock BuildBlock(LifecycleState declared, bool allTrue)
    {
        var status = allTrue ? ReadinessStatus.READY : ReadinessStatus.BLOCKED;
        var pcs = Pcs(allTrue);
        ReadinessBlock? b = declared == LifecycleState.Entered
            ? ReadinessBlock.TryCreate(2, status, "P1 AND P2 AND P3", pcs, LifecycleState.Entered, EnteredPointer)
            : ReadinessBlock.TryCreate(1, status, "P1 AND P2 AND P3", pcs); // v1 implicit-Blocked
        Assert.NotNull(b); // fixture must construct; a null here is a test-setup defect, not a RED assertion
        Assert.Equal(declared, b!.EffectiveLifecycle);
        return b;
    }

    // The state-model oracle (Group G tables A/B). Derived from the axes, NOT per-cell
    // literals — one closed rule the whole cross-product is measured against.
    private static ProposedTransition Expected(LifecycleState declared, EntryIntegrity integrity, bool allTrue)
    {
        if (declared == LifecycleState.Entered)
        {
            // (B) established-ENTERED: monotonic latch honored under EVERY integrity.
            return ProposedTransition.HonorEntered;
        }

        // (A) at-activation (declared BLOCKED): ProposeEnter IFF all re-derive true AND Verified.
        if (allTrue && integrity == EntryIntegrity.Verified)
        {
            return ProposedTransition.ProposeEnter;
        }

        return ProposedTransition.StayBlocked;
    }

    // The TOTALITY cross-product: {declared: Blocked, Entered} × {entryIntegrity: all 4}
    // × {preconditions: all-true, some-false}. Cells are DERIVED from the committed enums
    // (Enum.GetValues) — not an ad-hoc handful of representative rows (PMB-003 / AP-022).
    public static IEnumerable<object[]> Cells()
    {
        foreach (LifecycleState declared in Enum.GetValues<LifecycleState>())
        {
            foreach (EntryIntegrity integrity in Enum.GetValues<EntryIntegrity>())
            {
                foreach (bool allTrue in new[] { true, false })
                {
                    yield return new object[] { declared, integrity, allTrue };
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // TOTALITY CROSS-PRODUCT — the proposed transition for EVERY cell.
    // ---------------------------------------------------------------------------------

    // Tests INV-026 [unit]: EvaluateTransition proposes the state-model transition for
    // EVERY (declared × entryIntegrity × preconditions) cell. RED against the stub for
    // the ProposeEnter cell (Blocked+all-true+Verified) and all 8 HonorEntered cells
    // (every declared-ENTERED cell); the 7 StayBlocked cells pass in the stub too.
    [Theory]
    [MemberData(nameof(Cells))]
    public void Transition_matches_the_state_model_for_every_cell(
        LifecycleState declared, EntryIntegrity integrity, bool allTrue)
    {
        var block = BuildBlock(declared, allTrue);
        var probes = Probes(allTrue);

        ProposedTransition actual = ReadinessGate.EvaluateTransition(block, probes, integrity);

        Assert.Equal(Expected(declared, integrity, allTrue), actual);
    }

    // Tests INV-026 [unit]: the SAFETY-DIRECTION invariant (RS-001), the crux of INV-026
    // enforcement — over the SAME totality cross-product, NO evaluation may yield
    // ProposeEnter unless it is declared-BLOCKED ∧ all-true ∧ entryIntegrity==Verified.
    // Passes trivially in the stub (never proposes); this is the ONE-DIRECTIONAL guard
    // that goes RED on any fail-open GREEN (an at-activation ProposeEnter under a
    // non-Verified integrity or an unsatisfied precondition — AP-001).
    [Theory]
    [MemberData(nameof(Cells))]
    public void No_propose_enter_unless_blocked_alltrue_and_verified(
        LifecycleState declared, EntryIntegrity integrity, bool allTrue)
    {
        ProposedTransition actual =
            ReadinessGate.EvaluateTransition(BuildBlock(declared, allTrue), Probes(allTrue), integrity);

        if (actual == ProposedTransition.ProposeEnter)
        {
            Assert.Equal(LifecycleState.Blocked, declared);
            Assert.True(allTrue, "fail-open: ProposeEnter with a precondition unsatisfied");
            Assert.Equal(EntryIntegrity.Verified, integrity);
        }
    }

    // Tests INV-026 [unit]: the cross-product ENUMERATES the full committed state space —
    // a count DERIVED from the enums (not a literal N used as the oracle). The literal 16
    // PINS the currently-committed enum sizes (|LifecycleState|=2 × |EntryIntegrity|=4 ×
    // {all-true, some-false}=2): if a member is ever added to either enum, this pin breaks
    // and forces the cross-product to be re-derived (PMB-003 — a row-count/presence proxy
    // cannot detect an ABSENT cell; here the derived count auto-scales AND the pin flags it).
    [Fact]
    public void Cross_product_enumerates_the_full_state_space()
    {
        int derived = Enum.GetValues<LifecycleState>().Length
                    * Enum.GetValues<EntryIntegrity>().Length
                    * 2;
        Assert.Equal(16, derived);
        Assert.Equal(derived, Cells().Count());
    }

    // ---------------------------------------------------------------------------------
    // ENUM VOCABULARY — set-equality (not count/presence proxy, PMB-003/AP-022).
    // Scaffolding for this track; passes in the stub.
    // ---------------------------------------------------------------------------------

    // Tests INV-026 [unit]: EntryIntegrity is EXACTLY {Verified, Rejected, Unavailable,
    // Absent} — the four Group G integrity states, set-equality.
    [Fact]
    public void EntryIntegrity_is_exactly_the_four_integrity_states()
    {
        var set = Enum.GetValues<EntryIntegrity>().ToHashSet();
        Assert.Equal(
            new HashSet<EntryIntegrity>
            {
                EntryIntegrity.Verified, EntryIntegrity.Rejected,
                EntryIntegrity.Unavailable, EntryIntegrity.Absent,
            },
            set);
    }

    // Tests INV-026 [unit]: ProposedTransition is EXACTLY {StayBlocked, ProposeEnter,
    // HonorEntered} — the three Group G proposals, set-equality. (No Pass/Fail here — the
    // evaluator emits a transition PROPOSAL, never the verdict; RS-022.)
    [Fact]
    public void ProposedTransition_is_exactly_the_three_proposals()
    {
        var set = Enum.GetValues<ProposedTransition>().ToHashSet();
        Assert.Equal(
            new HashSet<ProposedTransition>
            {
                ProposedTransition.StayBlocked, ProposedTransition.ProposeEnter,
                ProposedTransition.HonorEntered,
            },
            set);
    }

    // ---------------------------------------------------------------------------------
    // TABLE (A) — at-activation (declared BLOCKED), READABLE row-by-row encoding.
    // ---------------------------------------------------------------------------------

    // Tests INV-026 [unit]: (A) row `verified` — declared BLOCKED, all preconditions
    // re-derive true, entryIntegrity==Verified => ProposeEnter (activation proposed).
    // RED against the stub (returns StayBlocked).
    [Fact]
    public void At_activation_verified_alltrue_proposes_enter()
    {
        ProposedTransition t = ReadinessGate.EvaluateTransition(
            BuildBlock(LifecycleState.Blocked, allTrue: true), Probes(allTrue: true), EntryIntegrity.Verified);
        Assert.Equal(ProposedTransition.ProposeEnter, t);
    }

    // Tests INV-026 [unit]: (A) rows `unavailable` / `rejected` / `absent` — declared
    // BLOCKED, all preconditions re-derive true, but entryIntegrity != Verified =>
    // StayBlocked (activation NOT proposed — a first activation must NOT propose entry on
    // a fault/tamper/absence). PASSES in the stub (the safety direction), RED on any
    // fail-open GREEN that proposes entry without a verified receipt.
    [Theory]
    [InlineData(EntryIntegrity.Unavailable)]
    [InlineData(EntryIntegrity.Rejected)]
    [InlineData(EntryIntegrity.Absent)]
    public void At_activation_nonverified_stays_blocked_even_when_alltrue(EntryIntegrity integrity)
    {
        ProposedTransition t = ReadinessGate.EvaluateTransition(
            BuildBlock(LifecycleState.Blocked, allTrue: true), Probes(allTrue: true), integrity);
        Assert.Equal(ProposedTransition.StayBlocked, t);
    }

    // Tests INV-026 [unit]: (A) — declared BLOCKED + Verified but NOT all preconditions
    // re-derive true => StayBlocked. Parametrized over WHICH single precondition is
    // unsatisfied (P1, P2, or P3) to prove ProposeEnter requires ALL THREE (a per-position
    // totality — one unmet precondition anywhere blocks entry). PASSES in the stub; RED on
    // a GREEN that proposes entry with a precondition unmet.
    [Theory]
    [InlineData(PreconditionId.P1)]
    [InlineData(PreconditionId.P2)]
    [InlineData(PreconditionId.P3)]
    public void At_activation_verified_with_any_precondition_unmet_stays_blocked(PreconditionId unmet)
    {
        ProbeResult P(bool sat) => ProbeResult.TryCreate(sat, "probe", ReferenceResolution.Resolved)!;
        var probes = new Dictionary<PreconditionId, ProbeResult>
        {
            [PreconditionId.P1] = P(unmet != PreconditionId.P1),
            [PreconditionId.P2] = P(unmet != PreconditionId.P2),
            [PreconditionId.P3] = P(unmet != PreconditionId.P3),
        };
        ProposedTransition t = ReadinessGate.EvaluateTransition(
            BuildBlock(LifecycleState.Blocked, allTrue: false), probes, EntryIntegrity.Verified);
        Assert.Equal(ProposedTransition.StayBlocked, t);
    }

    // Tests INV-026 [unit]: DEFENSIVE — declared BLOCKED + Verified, all probes report
    // Satisfied=true, but ONE reference is Unresolvable => StayBlocked (an unresolvable
    // reference cannot re-derive a precondition as true).
    // DECISION: treated "re-derive true" as requiring ReferenceResolution==Resolved,
    // grounded in the INV-005 table (evidence!=null AND Unresolvable/Malformed -> hard
    // Fail; a precondition with an unresolvable reference is NOT satisfied). Chose this
    // over a Satisfied-only check because a Satisfied-but-Unresolvable probe is exactly
    // the fail-open shape INV-026 guards. Surfaced for test-audit review.
    [Fact]
    public void At_activation_verified_with_unresolvable_reference_stays_blocked()
    {
        var probes = new Dictionary<PreconditionId, ProbeResult>
        {
            [PreconditionId.P1] = ProbeResult.TryCreate(true, "probe", ReferenceResolution.Resolved)!,
            [PreconditionId.P2] = ProbeResult.TryCreate(true, "probe", ReferenceResolution.Unresolvable)!,
            [PreconditionId.P3] = ProbeResult.TryCreate(true, "probe", ReferenceResolution.Resolved)!,
        };
        ProposedTransition t = ReadinessGate.EvaluateTransition(
            BuildBlock(LifecycleState.Blocked, allTrue: true), probes, EntryIntegrity.Verified);
        Assert.Equal(ProposedTransition.StayBlocked, t);
    }

    // ---------------------------------------------------------------------------------
    // TABLE (B) — established-ENTERED (declared ENTERED), READABLE row-by-row encoding.
    // ---------------------------------------------------------------------------------

    // Tests INV-026 [unit]: (B) — declared ENTERED honors the latch under EVERY
    // entryIntegrity AND regardless of precondition state (the monotonic declared latch;
    // the src/ ban keys off the DECLARED effective_lifecycle, not entry_integrity). RED
    // against the stub for all 8 (integrity × preconditions) combinations.
    [Theory]
    [InlineData(EntryIntegrity.Verified, true)]
    [InlineData(EntryIntegrity.Verified, false)]
    [InlineData(EntryIntegrity.Unavailable, true)]
    [InlineData(EntryIntegrity.Unavailable, false)]
    [InlineData(EntryIntegrity.Rejected, true)]
    [InlineData(EntryIntegrity.Rejected, false)]
    [InlineData(EntryIntegrity.Absent, true)]
    [InlineData(EntryIntegrity.Absent, false)]
    public void Established_entered_honors_entered_under_every_integrity(EntryIntegrity integrity, bool allTrue)
    {
        ProposedTransition t = ReadinessGate.EvaluateTransition(
            BuildBlock(LifecycleState.Entered, allTrue), Probes(allTrue), integrity);
        Assert.Equal(ProposedTransition.HonorEntered, t);
    }

    // Tests INV-026 [unit]: (B) row `unavailable` (transient outage) — declared ENTERED
    // under a transient entryIntegrity==Unavailable HONORS ENTERED (the monotonic latch is
    // NOT reverted; the src/ ban stays lifted — a transient outage never re-bans existing
    // src/). RED against the stub. This is the deliberate narrow monotonic exception.
    [Fact]
    public void Established_entered_monotonic_under_transient_unavailable()
    {
        ProposedTransition t = ReadinessGate.EvaluateTransition(
            BuildBlock(LifecycleState.Entered, allTrue: true), Probes(allTrue: true), EntryIntegrity.Unavailable);
        Assert.Equal(ProposedTransition.HonorEntered, t);
    }

    // Tests INV-026 [unit]: (B) rows `rejected` / `absent` — a FORGED declared-ENTERED with
    // a rejected/absent integrity STILL returns HonorEntered FROM THE EVALUATOR (the src/
    // ban is monotonic off the declared latch, so it is not re-banned). The forgery gains
    // the forger NOTHING because the SEPARATE Pass/Fail verdict — computed by the
    // orchestrator in 5c, NOT here — fails hard-red. This test pins that the evaluator
    // emits ONLY the transition proposal, never the verdict. RED against the stub.
    [Theory]
    [InlineData(EntryIntegrity.Rejected)]
    [InlineData(EntryIntegrity.Absent)]
    public void Forged_entered_with_rejected_or_absent_still_honors_entered_from_the_evaluator(EntryIntegrity integrity)
    {
        ProposedTransition t = ReadinessGate.EvaluateTransition(
            BuildBlock(LifecycleState.Entered, allTrue: false), Probes(allTrue: false), integrity);
        Assert.Equal(ProposedTransition.HonorEntered, t);
    }

    // ---------------------------------------------------------------------------------
    // KERNEL PURITY — the evaluator does no I/O (INV-004 / INV-026 component #1).
    // ---------------------------------------------------------------------------------

    // Tests INV-026 [unit]: BEHAVIORAL determinism — EvaluateTransition returns identical
    // proposals across repeated calls with identical supplied inputs, under a MUTATED
    // ambient culture (so an ambient-state read a denylist missed still fails determinism).
    // Directly exercises the NEW evaluator's purity (complements the whole-project INV-004
    // scan). PASSES in the stub (constant), and must STAY green once GREEN implements it.
    [Fact]
    public void Evaluator_is_deterministic_under_mutated_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var block = BuildBlock(LifecycleState.Blocked, allTrue: true);
            var probes = Probes(allTrue: true);
            var t1 = ReadinessGate.EvaluateTransition(block, probes, EntryIntegrity.Verified);
            var t2 = ReadinessGate.EvaluateTransition(block, probes, EntryIntegrity.Verified);
            Assert.Equal(t1, t2);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // Tests INV-026 [unit]: adding EvaluateTransition to the Kernel keeps the project
    // BCL-only — the Kernel .csproj still declares NO ProjectReference/PackageReference
    // (carrier INV-004 kernel-purity must still hold). Structural regression guard tied to
    // this track. PASSES (the .csproj is clean).
    [Fact]
    public void Kernel_project_stays_reference_free_after_adding_the_evaluator()
    {
        string xml = File.ReadAllText(
            TestPaths.RepoFile("gate", "Corrected.Gate.Kernel", "Corrected.Gate.Kernel.csproj"));
        Assert.DoesNotContain("<ProjectReference", xml);
        Assert.DoesNotContain("<PackageReference", xml);
    }
}
