using System;
using System.Collections.Generic;
using System.Linq;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Track 5d-i — INV-028 (spec ~1089–1155) post-entry determinism HEALTH model + fold + the
/// COMPOSED overall conclusion. The <c>current_health</c> paragraph (~938–947), the
/// cross-product precedence paragraph (RS-019, ~949–957), and the (A)/(B) readiness tables
/// (~968–987) are the pinned source.
///
/// Subject: the new <see cref="PostEntryHealth"/> component in Corrected.Gate —
///   * <see cref="HealthFindingKind"/> (EXACTLY seven; set-equality pinned, PMB-003),
///   * <see cref="HealthSeverity"/> + the TOTAL <see cref="PostEntryHealth.SeverityOf"/> map,
///   * <see cref="PostEntryHealth.FoldHealth"/> (the hard-red-wins fold over the finding SET),
///   * <see cref="PostEntryHealth.FoldOverallConclusion"/> (the composed conclusion = the
///     hard-red-wins MAX of the (A)/(B) readiness verdict with the health fold).
///
/// The composition SINGLE-SOURCES the (A)/(B) table by calling the REAL
/// <see cref="LifecycleGate.EvaluateProductionBanAndVerdict"/> — this file does NOT duplicate
/// that table. All inputs are SYNTHETIC enum/set values (no cosign, no I/O).
///
/// This track covers ONLY the INV-028 health model / fold / composition (5d-i). It does NOT
/// build: the closed pointer schema + dangling-pointer coupling (5d-ii, RS-029), the CI
/// required-vs-advisory surface separation (RS-017), or the append-only versioned-path refresh.
///
/// AP-031 real-artifact clause is NOT triggered — these tests exercise in-memory enums / folds
/// / the fused gate, never a `.correctless/artifacts/` producer output from another Correctless
/// skill.
/// </summary>
public class Inv028HealthFoldTests
{
    // -----------------------------------------------------------------------------------
    // PINNED expected severity partition (INV-028 current_health paragraph, spec ~940–945).
    // These literal sets are the ORACLE the derived fold/partition tests measure against —
    // they must NOT be derived from SeverityOf (the subject under test).
    // -----------------------------------------------------------------------------------
    private static readonly HashSet<HealthFindingKind> ExpectedAdvisory = new()
    {
        HealthFindingKind.RefreshRequired,
        HealthFindingKind.ResourceFloorSkipped,
        HealthFindingKind.P3VerifierUnavailable,
    };

    private static readonly HashSet<HealthFindingKind> ExpectedHardRed = new()
    {
        HealthFindingKind.Disagreement,
        HealthFindingKind.InfrastructureInvalid,
        HealthFindingKind.EvidenceIntegrityRejected,
        HealthFindingKind.PreconditionRegression,
    };

    private static IReadOnlySet<HealthFindingKind> Set(params HealthFindingKind[] kinds)
        => kinds.ToHashSet();

    // ===================================================================================
    // (1) HealthFindingKind SET-EQUALITY — exactly the seven (PMB-003, not a count/presence).
    // ===================================================================================

    // Tests INV-028 [unit]: HealthFindingKind is EXACTLY the seven typed findings — a
    // set-equality pin (Enum.GetValues == the literal seven-set), NOT a count. Breaks if a
    // member is added, removed, renamed, or a default/sentinel/"ok" member is introduced
    // (the spec forbids representing a transient outage as ok). Passes on the stub (the enum
    // members exist); goes RED only on a vocabulary drift.
    [Fact]
    public void HealthFindingKind_is_exactly_the_seven_findings()
    {
        Assert.Equal(
            new HashSet<HealthFindingKind>
            {
                HealthFindingKind.RefreshRequired,
                HealthFindingKind.ResourceFloorSkipped,
                HealthFindingKind.P3VerifierUnavailable,
                HealthFindingKind.Disagreement,
                HealthFindingKind.InfrastructureInvalid,
                HealthFindingKind.EvidenceIntegrityRejected,
                HealthFindingKind.PreconditionRegression,
            },
            Enum.GetValues<HealthFindingKind>().ToHashSet());
    }

    // Tests INV-028 [unit]: HealthSeverity is EXACTLY {Advisory, HardRed} — the two conclusion
    // severities, set-equality pinned.
    [Fact]
    public void HealthSeverity_is_exactly_advisory_and_hardred()
    {
        Assert.Equal(
            new HashSet<HealthSeverity> { HealthSeverity.Advisory, HealthSeverity.HardRed },
            Enum.GetValues<HealthSeverity>().ToHashSet());
    }

    // ===================================================================================
    // (2) SEVERITY MAP — totality + correctness + complete & disjoint (derived, PMB-003).
    // ===================================================================================

    // Tests INV-028 [unit]: SeverityOf matches the PINNED Advisory/HardRed partition for EVERY
    // one of the seven kinds — derived by ENUMERATING Enum.GetValues, never a hand-picked
    // subset (PMB-003). RED against the stub (which returns HardRed for the advisory kinds).
    [Fact]
    public void SeverityOf_matches_the_pinned_partition_for_every_kind()
    {
        foreach (HealthFindingKind kind in Enum.GetValues<HealthFindingKind>())
        {
            HealthSeverity expected = ExpectedAdvisory.Contains(kind)
                ? HealthSeverity.Advisory
                : HealthSeverity.HardRed;
            Assert.Equal(expected, PostEntryHealth.SeverityOf(kind));
        }
    }

    // Tests INV-028 [unit]: SeverityOf is TOTAL — every kind maps to a DEFINED HealthSeverity
    // (no default fallthrough, no undefined/garbage value). Defensive: guards an exhaustive
    // switch from ever falling through. Passes on the stub (HardRed is defined).
    [Fact]
    public void SeverityOf_is_total_over_every_kind()
    {
        foreach (HealthFindingKind kind in Enum.GetValues<HealthFindingKind>())
        {
            HealthSeverity s = PostEntryHealth.SeverityOf(kind);
            Assert.True(Enum.IsDefined(s), $"SeverityOf({kind}) returned an undefined severity");
        }
    }

    // Tests INV-028 [unit]: the severity partition induced BY SeverityOf is COMPLETE (its two
    // classes union to all seven — no unclassified kind) AND DISJOINT (no kind is both), AND
    // equals the pinned expected sets. Derived from SeverityOf over Enum.GetValues (PMB-003 —
    // a completeness claim measured by cross-product, not a representative sample). RED against
    // the stub via the pinned-set equality (stub advisory class is empty, expected is 3).
    [Fact]
    public void Severity_partition_is_complete_disjoint_and_matches_the_pin()
    {
        HashSet<HealthFindingKind> all = Enum.GetValues<HealthFindingKind>().ToHashSet();
        HashSet<HealthFindingKind> advisory =
            all.Where(k => PostEntryHealth.SeverityOf(k) == HealthSeverity.Advisory).ToHashSet();
        HashSet<HealthFindingKind> hardRed =
            all.Where(k => PostEntryHealth.SeverityOf(k) == HealthSeverity.HardRed).ToHashSet();

        // COMPLETE: union == all seven (no kind is unclassified).
        Assert.Equal(all, advisory.Union(hardRed).ToHashSet());
        // DISJOINT: intersection empty (no kind is BOTH severities).
        Assert.Empty(advisory.Intersect(hardRed));
        // And the two classes equal the pinned expected partition.
        Assert.Equal(ExpectedAdvisory, advisory);
        Assert.Equal(ExpectedHardRed, hardRed);
    }

    // ===================================================================================
    // (3) THE HEALTH FOLD over the finding SET (INV-028 conclusion fold, ~945–947).
    // ===================================================================================

    // Tests INV-028 [unit]: the empty health set → Success (no finding → clean).
    [Fact]
    public void FoldHealth_empty_set_is_success()
    {
        Assert.Equal(LifecycleVerdict.Success, PostEntryHealth.FoldHealth(Set()));
    }

    // Tests INV-028 [unit]: a set with ONLY advisory kinds → Neutral. Both a singleton and the
    // full advisory triple fold to neutral. RED against the stub (returns Success).
    [Fact]
    public void FoldHealth_only_advisory_is_neutral()
    {
        Assert.Equal(LifecycleVerdict.Neutral,
            PostEntryHealth.FoldHealth(Set(HealthFindingKind.RefreshRequired)));
        Assert.Equal(LifecycleVerdict.Neutral,
            PostEntryHealth.FoldHealth(Set(
                HealthFindingKind.RefreshRequired,
                HealthFindingKind.ResourceFloorSkipped,
                HealthFindingKind.P3VerifierUnavailable)));
    }

    // Tests INV-028 [unit]: any set CONTAINING a hard-red kind → HardRedFailure. RED against
    // the stub (returns Success).
    [Fact]
    public void FoldHealth_any_hard_red_is_hard_red_failure()
    {
        Assert.Equal(LifecycleVerdict.HardRedFailure,
            PostEntryHealth.FoldHealth(Set(HealthFindingKind.Disagreement)));
        Assert.Equal(LifecycleVerdict.HardRedFailure,
            PostEntryHealth.FoldHealth(Set(HealthFindingKind.PreconditionRegression)));
    }

    // Tests INV-028 [unit]: hard-red is NEVER downgraded by a co-occurring advisory — a mixed
    // set {RefreshRequired (advisory), Disagreement (hard-red)} folds to HardRedFailure. The
    // fold is over the WHOLE set; the presence of an advisory alongside cannot soften a
    // hard-red. RED against the stub.
    [Fact]
    public void FoldHealth_hard_red_never_downgraded_by_cooccurring_advisory()
    {
        Assert.Equal(LifecycleVerdict.HardRedFailure,
            PostEntryHealth.FoldHealth(Set(
                HealthFindingKind.RefreshRequired, HealthFindingKind.Disagreement)));
    }

    // Tests INV-028 [unit]: the fold matches the ORACLE over EVERY subset of the seven findings
    // (2^7 = 128 subsets) — a DERIVED exhaustive check (PMB-003), not a handful of cells. The
    // oracle is built from the PINNED expected partitions (not from SeverityOf). RED against the
    // stub for every non-empty-non-advisory... actually every non-empty subset (stub → Success).
    [Fact]
    public void FoldHealth_matches_the_oracle_over_every_subset_of_findings()
    {
        HealthFindingKind[] all = Enum.GetValues<HealthFindingKind>();
        int subsetCount = 0;
        foreach (IReadOnlyList<HealthFindingKind> subset in AllSubsets(all))
        {
            IReadOnlySet<HealthFindingKind> set = subset.ToHashSet();
            Assert.Equal(OracleFold(set), PostEntryHealth.FoldHealth(set));
            subsetCount++;
        }

        // Powerset completeness pin (PMB-003): 2^7 = 128 subsets, derived from the axis size.
        Assert.Equal(1 << all.Length, subsetCount);
        Assert.Equal(128, subsetCount);
    }

    // ===================================================================================
    // (4) THE COMPOSED OVERALL CONCLUSION — the load-bearing RS-019 property (~949–957).
    // ===================================================================================

    // Tests INV-028 [unit]: THE load-bearing RS-019 property — a neutral entry_integrity row
    // (e.g. established-ENTERED entry_integrity=Unavailable → LifecycleVerdict.Neutral) NEVER
    // downgrades a hard-red health finding (a live disagreement) to neutral. hard-red WINS. This
    // is the exact fail-open the round-7 fix closes ("the earlier text said the health fold
    // applies only in the verified row"). RED against the stub (returns Success).
    [Fact]
    public void FoldOverallConclusion_neutral_entry_never_downgrades_a_hard_red_health_finding()
    {
        Assert.Equal(LifecycleVerdict.HardRedFailure,
            PostEntryHealth.FoldOverallConclusion(
                LifecycleVerdict.Neutral, Set(HealthFindingKind.Disagreement)));
    }

    // Tests INV-028 [unit]: a hard-red lifecycle verdict with EMPTY health stays HardRedFailure
    // (the readiness verdict alone already fails). RED against the stub.
    [Fact]
    public void FoldOverallConclusion_hard_red_lifecycle_with_empty_health_is_hard_red()
    {
        Assert.Equal(LifecycleVerdict.HardRedFailure,
            PostEntryHealth.FoldOverallConclusion(LifecycleVerdict.HardRedFailure, Set()));
    }

    // Tests INV-028 [unit]: a Success lifecycle verdict with an advisory-only health set →
    // Neutral (the softer conclusion applies only when neither side is hard-red). RED against
    // the stub? The stub returns Success — expected Neutral → RED.
    [Fact]
    public void FoldOverallConclusion_success_lifecycle_with_advisory_health_is_neutral()
    {
        Assert.Equal(LifecycleVerdict.Neutral,
            PostEntryHealth.FoldOverallConclusion(
                LifecycleVerdict.Success, Set(HealthFindingKind.RefreshRequired)));
    }

    // Tests INV-028 [unit]: a Success lifecycle verdict with EMPTY health → Success (the ONLY
    // way to reach Success). Passes on the stub (Success) — this is the cell that MUST stay
    // Success; it pins that the fold does not spuriously escalate a clean state.
    [Fact]
    public void FoldOverallConclusion_success_lifecycle_with_empty_health_is_success()
    {
        Assert.Equal(LifecycleVerdict.Success,
            PostEntryHealth.FoldOverallConclusion(LifecycleVerdict.Success, Set()));
    }

    // Tests INV-028 [unit]: the composed conclusion is the hard-red-wins MAX over EVERY
    // (lifecycle verdict × health subset) cell — 3 × 128 = 384 cells, derived from the axes
    // (PMB-003). Oracle = MAX by the pinned precedence (HardRedFailure > Neutral > Success).
    // Also asserts the safety-direction: the overall is Success ONLY IF the lifecycle verdict
    // is Success AND the health set is empty. RED against the stub for every escalating cell.
    [Fact]
    public void FoldOverallConclusion_is_the_hard_red_wins_max_over_lifecycle_and_health()
    {
        HealthFindingKind[] all = Enum.GetValues<HealthFindingKind>();
        int cells = 0;
        foreach (LifecycleVerdict lifecycle in Enum.GetValues<LifecycleVerdict>())
        {
            foreach (IReadOnlyList<HealthFindingKind> subset in AllSubsets(all))
            {
                IReadOnlySet<HealthFindingKind> set = subset.ToHashSet();
                LifecycleVerdict expected = OracleOverall(lifecycle, set);
                LifecycleVerdict actual = PostEntryHealth.FoldOverallConclusion(lifecycle, set);
                Assert.Equal(expected, actual);

                // Safety-direction: Success is reachable ONLY from a Success verdict + empty set.
                if (actual == LifecycleVerdict.Success)
                {
                    Assert.Equal(LifecycleVerdict.Success, lifecycle);
                    Assert.Empty(set);
                }
                cells++;
            }
        }

        Assert.Equal(Enum.GetValues<LifecycleVerdict>().Length * (1 << all.Length), cells);
        Assert.Equal(384, cells);
    }

    // ===================================================================================
    // (5) THE transition_context × entry_integrity × health-severity CROSS-PRODUCT
    //     (INV-028 enforcement, RS-019). Composes with the REAL LifecycleGate (A)/(B) table.
    // ===================================================================================

    // The three health-severity REPRESENTATIVES: ∅, one-advisory, one-hard-red. Passed by LABEL
    // (serializable MemberData) and reconstructed to a set in-test.
    private static readonly string[] HealthReps = { "empty", "one-advisory", "one-hard-red" };

    private static IReadOnlySet<HealthFindingKind> HealthRep(string label) => label switch
    {
        "empty" => Set(),
        "one-advisory" => Set(HealthFindingKind.RefreshRequired),
        "one-hard-red" => Set(HealthFindingKind.Disagreement),
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "unknown health rep"),
    };

    public static IEnumerable<object[]> HealthCrossProductCells()
    {
        foreach (TransitionContext ctx in Enum.GetValues<TransitionContext>())
        {
            foreach (EntryIntegrity integ in Enum.GetValues<EntryIntegrity>())
            {
                foreach (string label in HealthReps)
                {
                    yield return new object[] { ctx, integ, label };
                }
            }
        }
    }

    // Tests INV-028 [integration]: the transition_context × entry_integrity × health-severity
    // cross-product (RS-019). For EVERY cell: the (A)/(B) readiness verdict is computed by the
    // REAL LifecycleGate.EvaluateProductionBanAndVerdict (single-sourced — this test does NOT
    // re-derive the table), then folded with health via FoldOverallConclusion. Asserts:
    //   (i)  the overall is a DEFINED LifecycleVerdict (no fallthrough);
    //   (ii) the safety-direction invariant — if EITHER the lifecycle verdict is HardRedFailure
    //        OR the health severity is hard-red, the overall is HardRedFailure (hard-red wins);
    //        and the overall is Success ONLY IF the lifecycle verdict is Success AND health is
    //        empty.
    // Cells are DERIVED from the enums (Enum.GetValues), never per-cell literals. RED against
    // the stub for every cell whose safe overall is not Success.
    [Theory]
    [MemberData(nameof(HealthCrossProductCells))]
    public void Composed_conclusion_cross_product_is_defined_and_hard_red_wins(
        TransitionContext ctx, EntryIntegrity integ, string healthLabel)
    {
        // effective lifecycle latch for the fused gate: Entered iff established-ENTERED, else
        // Blocked (both plain pre-entry BLOCKED and the at-activation proposal are Blocked).
        LifecycleState effective = ctx == TransitionContext.EstablishedEntered
            ? LifecycleState.Entered
            : LifecycleState.Blocked;

        // The (A)/(B) readiness_verdict from the REAL fused gate. VacuousPass keeps the src/ ban
        // scan out of the way so this test isolates the entry_integrity × health composition.
        LifecycleVerdict lifecycleVerdict = LifecycleGate.EvaluateProductionBanAndVerdict(
            effective, integ, ctx, ScanOutcome.VacuousPass).Verdict;

        IReadOnlySet<HealthFindingKind> health = HealthRep(healthLabel);
        LifecycleVerdict overall = PostEntryHealth.FoldOverallConclusion(lifecycleVerdict, health);

        // (i) defined — no silent fallthrough to an undefined verdict.
        Assert.True(Enum.IsDefined(overall), $"overall verdict undefined for ({ctx},{integ},{healthLabel})");

        // (ii) safety-direction (RS-019): hard-red always wins.
        bool healthIsHardRed = health.Any(ExpectedHardRed.Contains);
        if (lifecycleVerdict == LifecycleVerdict.HardRedFailure || healthIsHardRed)
        {
            Assert.Equal(LifecycleVerdict.HardRedFailure, overall);
        }

        // Success is reachable ONLY from a Success readiness verdict AND an empty health set.
        if (overall == LifecycleVerdict.Success)
        {
            Assert.Equal(LifecycleVerdict.Success, lifecycleVerdict);
            Assert.Empty(health);
        }
    }

    // Tests INV-028 [unit]: the cross-product ENUMERATES the full committed axis space, with a
    // literal COUNT pin that breaks if any axis grows (PMB-003 — a count derived from the axes,
    // paired with the literal 36 so an axis addition trips it). {transition_context}=3 ×
    // {entry_integrity}=4 × {∅, one-advisory, one-hard-red}=3 = 36.
    [Fact]
    public void Health_cross_product_enumerates_all_36_cells()
    {
        int derived = Enum.GetValues<TransitionContext>().Length   // 3
                    * Enum.GetValues<EntryIntegrity>().Length       // 4
                    * HealthReps.Length;                            // 3
        Assert.Equal(36, derived);
        Assert.Equal(derived, HealthCrossProductCells().Count());
    }

    // ===================================================================================
    // Oracles + helpers (derived from the PINNED expected partition, never from the subject).
    // ===================================================================================

    // The health fold ORACLE: hard-red wins, else advisory → neutral, else success.
    private static LifecycleVerdict OracleFold(IReadOnlySet<HealthFindingKind> health)
    {
        if (health.Any(ExpectedHardRed.Contains))
        {
            return LifecycleVerdict.HardRedFailure;
        }
        if (health.Any(ExpectedAdvisory.Contains))
        {
            return LifecycleVerdict.Neutral;
        }
        return LifecycleVerdict.Success;
    }

    // The composed ORACLE: MAX by the pinned precedence HardRedFailure > Neutral > Success.
    private static LifecycleVerdict OracleOverall(
        LifecycleVerdict lifecycleVerdict, IReadOnlySet<HealthFindingKind> health)
    {
        LifecycleVerdict h = OracleFold(health);
        return Rank(lifecycleVerdict) >= Rank(h) ? lifecycleVerdict : h;
    }

    private static int Rank(LifecycleVerdict v) => v switch
    {
        LifecycleVerdict.Success => 0,
        LifecycleVerdict.Neutral => 1,
        LifecycleVerdict.HardRedFailure => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, "undefined verdict"),
    };

    // Powerset of the finding kinds (2^n subsets), for the exhaustive derived fold tests.
    private static IEnumerable<IReadOnlyList<HealthFindingKind>> AllSubsets(HealthFindingKind[] items)
    {
        int n = items.Length;
        for (int mask = 0; mask < (1 << n); mask++)
        {
            var subset = new List<HealthFindingKind>();
            for (int i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    subset.Add(items[i]);
                }
            }
            yield return subset;
        }
    }
}
