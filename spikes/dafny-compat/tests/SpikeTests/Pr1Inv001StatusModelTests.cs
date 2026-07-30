// PR1 (Group A) RED tests — P3 determinism-attestation spec
// (.correctless/specs/p3-determinism-attestation.md), INV-001 slice ONLY.
//
// INV-001: a three-artifact status model (RunnerInvocationOutcome / RunReceipt /
// AttestedRunReceipt) with a PURE TOTAL classifier over the CLOSED legal-status
// table (only the four rows: completed×equal, completed×different,
// resource_floor_skipped×not_evaluated, infrastructure_invalid×not_evaluated).
// Any infrastructure fault => infrastructure_invalid/not_evaluated, NEVER
// comparison_status=different. The silent early-return becomes a TYPED
// resource_floor_skipped status. Signing outcome + ran-passed live OUTSIDE the
// receipt (ran-passed is probe-derived).
//
// RED: the DeterminismClassifier / RunReceiptCodec bodies throw
// NotImplementedException (STUB:TDD), so every behavioral assertion below fails
// AS AN ASSERTION, not as a compile error. The closed legal-cell set is derived
// from the COMMITTED manifest/determinism/legal-status-table.json (AP-022 /
// PMB-003 — never a test literal).
using System.Text.Json;
using System.Text.RegularExpressions;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Pr1Inv001StatusModelTests
{
    private static string LegalTablePath => SpikePaths.P("manifest", "determinism", "legal-status-table.json");

    // PascalCase enum member -> snake_case wire token (a stable naming convention,
    // NOT the legal-cell set — that is derived from the committed file below).
    private static string Wire(Enum member) =>
        Regex.Replace(member.ToString(), "(?<!^)([A-Z])", "_$1").ToLowerInvariant();

    private static HashSet<(string, string)> CommittedLegalPairs()
    {
        using var doc = SpikePaths.Json(LegalTablePath);
        return doc.RootElement.GetProperty("legal_pairs").EnumerateArray()
            .Select(p => (p.GetProperty("execution_status").GetString()!,
                          p.GetProperty("comparison_status").GetString()!))
            .ToHashSet();
    }

    // Tests INV-001 [unit]: the pure classifier maps EVERY raw runner outcome to
    // exactly one legal (execution_status, comparison_status) pair. In particular
    // an infrastructure fault maps to infrastructure_invalid/not_evaluated and a
    // below-floor observation to the TYPED resource_floor_skipped/not_evaluated —
    // never comparison_status=different.
    [Fact]
    public void Classifier_MapsEveryRawOutcome_ToItsLegalPair()
    {
        Assert.Equal(new ReceiptStatus(ExecutionStatus.Completed, ComparisonStatus.Equal),
            DeterminismClassifier.Classify(RunnerOutcomeKind.CompletedProjectionsEqual));
        Assert.Equal(new ReceiptStatus(ExecutionStatus.Completed, ComparisonStatus.Different),
            DeterminismClassifier.Classify(RunnerOutcomeKind.CompletedProjectionsDiffer));
        Assert.Equal(new ReceiptStatus(ExecutionStatus.ResourceFloorSkipped, ComparisonStatus.NotEvaluated),
            DeterminismClassifier.Classify(RunnerOutcomeKind.BelowResourceFloor));
        Assert.Equal(new ReceiptStatus(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.NotEvaluated),
            DeterminismClassifier.Classify(RunnerOutcomeKind.InfrastructureFault));
    }

    // Tests INV-001 [unit] (AP-022 / PMB-003): the closed legal-status table is
    // enforced by a CROSS-PRODUCT enumeration over ExecutionStatus × ComparisonStatus
    // (3×3 = 9 cells). For every cell IsLegalStatusPair must equal committed-file
    // membership; exactly four cells are legal; every other combination is
    // schema-invalid. The expected legal set is DERIVED FROM THE COMMITTED FILE,
    // so shrinking the table is a reviewable diff, never a test edit.
    [Fact]
    public void LegalStatusTable_IsExhaustive_CrossProductFromCommittedFile()
    {
        var legal = CommittedLegalPairs();
        Assert.Equal(4, legal.Count); // the committed table is exactly the four rows

        var covered = 0;
        foreach (var e in Enum.GetValues<ExecutionStatus>())
        {
            foreach (var c in Enum.GetValues<ComparisonStatus>())
            {
                var expected = legal.Contains((Wire(e), Wire(c)));
                Assert.Equal(expected, DeterminismClassifier.IsLegalStatusPair(e, c));
                covered++;
            }
        }
        Assert.Equal(9, covered); // the full cross-product was enumerated, no cell skipped
    }

    // Tests INV-001 [unit] (totality — A1: a future 5th RunnerOutcomeKind absorbed
    // by a `_ =>` default must not fail open): EVERY RunnerOutcomeKind maps to a
    // LEGAL pair on the closed table, and NEVER to a fail-open `different` on a
    // non-completed execution — a default must be infrastructure_invalid, never
    // `different`. Iterating Enum.GetValues covers a member added later, unlike the
    // hand-listed mapping test above.
    [Fact]
    public void Classifier_IsTotalOverRunnerOutcomeKind_NeverFailsOpen()
    {
        var mintEligibleKinds = new List<RunnerOutcomeKind>();
        foreach (var k in Enum.GetValues<RunnerOutcomeKind>())
        {
            var status = DeterminismClassifier.Classify(k);
            Assert.True(DeterminismClassifier.IsLegalStatusPair(status.Execution, status.Comparison),
                $"{k} maps to an (execution, comparison) pair outside the closed legal table (INV-001)");
            if (status.Comparison == ComparisonStatus.Different)
            {
                // the never-`different`-on-fault safety direction
                Assert.Equal(ExecutionStatus.Completed, status.Execution);
            }
            if (status is { Execution: ExecutionStatus.Completed, Comparison: ComparisonStatus.Equal })
            {
                mintEligibleKinds.Add(k);
            }
        }

        // MA-FO-002 — the DANGEROUS direction (symmetric to the never-`different`
        // guard above): the mint-eligible (completed, equal) cell must be reachable
        // from EXACTLY ONE RunnerOutcomeKind — the legitimate CompletedProjectionsEqual.
        // A future kind (or a regressed arm/default) that also maps to (completed, equal)
        // would silently mint-attest a non-equal outcome; this fails it RED.
        Assert.Equal(new[] { RunnerOutcomeKind.CompletedProjectionsEqual }, mintEligibleKinds);

        // The fail-closed default must NOT be mint-eligible: an unknown/future kind
        // (an out-of-range value hitting the `_ =>` arm) absorbs to
        // infrastructure_invalid/not_evaluated, never the (completed, equal) cell.
        var unknown = DeterminismClassifier.Classify((RunnerOutcomeKind)0xBEEF);
        Assert.Equal(new ReceiptStatus(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.NotEvaluated), unknown);
    }

    // Tests INV-001 [unit] (MA-CC-001/MA-FO-001, AP-022): the SHIPPED receipt-status
    // mapping — the one DeterminismReceiptWriter.Build actually derives the emitted
    // execution/comparison pair from — is EXHAUSTIVELY fail-closed-tested over EVERY
    // ComparisonStatus value (the closed comparison domain) PLUS the out-of-range
    // default. This exercises the PRODUCTION surface (MapComparisonToReceiptStatus),
    // not the dead-surrogate Classify: mutating its default or its non-Equal arm to
    // (completed, equal) turns THIS test RED. Every cell must be a legal pair; the
    // mint-eligible (completed, equal) cell is reachable ONLY from ComparisonStatus.Equal;
    // Different is a completed disagreement; not_evaluated and the default fail closed to
    // infrastructure_invalid/not_evaluated — never a fail-open different, never illegal.
    [Fact]
    public void ShippedReceiptStatusMapping_IsExhaustive_OnlyEqualIsMintEligible()
    {
        var mintEligible = new List<ComparisonStatus>();
        foreach (var c in Enum.GetValues<ComparisonStatus>())
        {
            var status = DeterminismReceiptWriter.MapComparisonToReceiptStatus(c);
            Assert.True(DeterminismClassifier.IsLegalStatusPair(status.Execution, status.Comparison),
                $"comparison_status={c} maps to an (execution, comparison) pair outside the closed legal table (INV-001)");
            if (status is { Execution: ExecutionStatus.Completed, Comparison: ComparisonStatus.Equal })
            {
                mintEligible.Add(c);
            }
        }

        // Exactly-once mint reachability: only Equal yields the attestable cell. A
        // regressed Different/default arm (or a future ComparisonStatus) mapping to
        // (completed, equal) breaks this — the load-bearing fail-open guard.
        Assert.Equal(new[] { ComparisonStatus.Equal }, mintEligible);

        // The exact behavior-preserving per-value mapping (the happy Equal path is
        // byte-identical to before the single-sourcing refactor).
        Assert.Equal(new ReceiptStatus(ExecutionStatus.Completed, ComparisonStatus.Equal),
            DeterminismReceiptWriter.MapComparisonToReceiptStatus(ComparisonStatus.Equal));
        Assert.Equal(new ReceiptStatus(ExecutionStatus.Completed, ComparisonStatus.Different),
            DeterminismReceiptWriter.MapComparisonToReceiptStatus(ComparisonStatus.Different));
        Assert.Equal(new ReceiptStatus(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.NotEvaluated),
            DeterminismReceiptWriter.MapComparisonToReceiptStatus(ComparisonStatus.NotEvaluated));

        // The fail-closed default: an out-of-range/future comparison value absorbs to
        // infrastructure_invalid/not_evaluated — a legal, non-mint-eligible pair.
        var defaulted = DeterminismReceiptWriter.MapComparisonToReceiptStatus((ComparisonStatus)0xBEEF);
        Assert.Equal(new ReceiptStatus(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.NotEvaluated), defaulted);
        Assert.True(DeterminismClassifier.IsLegalStatusPair(defaulted.Execution, defaulted.Comparison));
    }

    // Tests INV-001 [unit]: the C# ExecutionStatus/ComparisonStatus enums are
    // set-equal to the committed domains — neither axis silently drifted from the
    // legal-status table (a drifted enum would make the cross-product above vacuous).
    [Fact]
    public void StatusEnums_AreSetEqualToCommittedDomains()
    {
        using var doc = SpikePaths.Json(LegalTablePath);
        var exec = doc.RootElement.GetProperty("execution_status_domain").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
        var comp = doc.RootElement.GetProperty("comparison_status_domain").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
        Assert.Equal(exec, Enum.GetValues<ExecutionStatus>().Select(x => Wire(x)).ToHashSet());
        Assert.Equal(comp, Enum.GetValues<ComparisonStatus>().Select(x => Wire(x)).ToHashSet());
    }

    // Tests INV-001 [unit] (IB-001): WireEnum.Parse is CLOSED over the enum's DEFINED
    // members — a NUMERIC wire token ("3", "999", "-1") that Enum.TryParse would accept
    // as an UNDEFINED underlying value is REJECTED (the Enum.IsDefined guard), so a
    // forged numeric status token can never smuggle an out-of-domain enum value in. The
    // real snake_case tokens still round-trip (the guard does not over-reject).
    [Fact]
    public void WireEnumParse_RejectsUndefinedNumericTokens_ButRoundTripsRealTokens()
    {
        foreach (var token in new[] { "3", "999", "-1" })
        {
            Assert.Throws<InvalidOperationException>(() => WireEnum.Parse<ExecutionStatus>(token));
            Assert.Throws<InvalidOperationException>(() => WireEnum.Parse<ComparisonStatus>(token));
        }
        Assert.Equal(ExecutionStatus.ResourceFloorSkipped, WireEnum.Parse<ExecutionStatus>("resource_floor_skipped"));
        Assert.Equal(ComparisonStatus.NotEvaluated, WireEnum.Parse<ComparisonStatus>("not_evaluated"));
    }

    // Tests INV-001 [unit]: the safety-direction invariant — no infrastructure
    // fault may be recorded as comparison_status=different. The committed table
    // never legalizes a `different` comparison outside `completed`, and the
    // classifier rejects the specific fail-open cell (infrastructure_invalid,
    // different) and (resource_floor_skipped, different).
    [Fact]
    public void SafetyDirection_NoInfraFaultOrSkip_IsEverDifferent()
    {
        // Committed-file half (green guard): no legal `different` outside completed.
        foreach (var (e, c) in CommittedLegalPairs())
        {
            if (c == "different")
            {
                Assert.Equal("completed", e);
            }
        }
        // Behavioral half (RED via stub): the two fail-open cells are illegal.
        Assert.False(DeterminismClassifier.IsLegalStatusPair(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.Different));
        Assert.False(DeterminismClassifier.IsLegalStatusPair(ExecutionStatus.ResourceFloorSkipped, ComparisonStatus.Different));
    }

    // Tests INV-001 [unit]: a MISSING RunReceipt is classified EXTERNALLY (a
    // receipt-write failure cannot self-report) as infrastructure_invalid /
    // not_evaluated — never comparison_status=different, never a parse of a
    // status pair outside the table.
    [Fact]
    public void MissingReceipt_ClassifiedExternally_AsInfrastructureInvalid()
    {
        Assert.Equal(new ReceiptStatus(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.NotEvaluated),
            DeterminismClassifier.ClassifyMissingReceipt());
    }

    // Tests INV-001 [unit] (structural guard): the signed subject (RunReceipt)
    // carries NO attestation/verification status and NO ran-passed/satisfied
    // field — those live OUTSIDE every receipt; ran-passed is probe-derived. It
    // DOES carry the required execution/comparison/attested_commit/manifest-digest
    // fields. Fails if a future edit adds a forbidden field to the signed subject.
    [Fact]
    public void RunReceiptSubject_HasNoAttestationOrRanPassedField()
    {
        var props = typeof(RunReceipt).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var forbidden in new[] { "ranpassed", "satisfied", "attestation", "attested", "verification", "verified", "signed", "signingoutcome", "bundle", "proberesult" })
        {
            Assert.DoesNotContain(props, p => p.Replace("_", "").Equals(forbidden, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var required in new[] { "Execution", "Comparison", "AttestedCommit", "SubjectManifestDigest", "PolicyVersion" })
        {
            Assert.Contains(required, props);
        }
    }

    // Tests INV-001 [unit]: the silent Console.Error.WriteLine(...) + return
    // early-return (Inv010DeterminismTests.cs:52-60) is REMOVED — it becomes the
    // typed resource_floor_skipped status. RED until GREEN deletes the construct:
    // a `ProcessorCount < coreFloor` guard whose body silently `return;`s.
    [Fact]
    public void SilentResourceFloorEarlyReturn_IsRemovedFromInv010()
    {
        var src = File.ReadAllLines(SpikePaths.P("tests", "SpikeTests", "Inv010DeterminismTests.cs"));
        var offenders = new List<int>();
        for (var i = 0; i < src.Length; i++)
        {
            if (!src[i].Contains("Environment.ProcessorCount <", StringComparison.Ordinal))
            {
                continue;
            }
            for (var j = i; j < Math.Min(i + 8, src.Length); j++)
            {
                if (src[j].TrimEnd().EndsWith("return;", StringComparison.Ordinal))
                {
                    offenders.Add(i + 1);
                    break;
                }
            }
        }
        Assert.True(offenders.Count == 0,
            "the silent resource-floor early-return must be replaced by a TYPED resource_floor_skipped status (INV-001/INV-004); "
            + "still present at Inv010DeterminismTests.cs line(s): " + string.Join(", ", offenders));
    }

    // The INV-001 [integration] Exit test (the emitted receipt carries a LEGAL
    // status pair from the REAL two-nested-run controller) now lives in
    // Pr1DeterminismLaneTests.Lane_StatusPairIsLegal_FromRealRun, which drives
    // scripts/determinism-lane.sh once via the shared fixture (CI-separation trait
    // "determinism-lane"). The from-clean unit tests above are the fast signal.
}
