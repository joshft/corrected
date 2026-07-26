using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

// INV-007: the SUPPLIED-(block, probeResults) corpus drives the kernel through
// every INV-005 row + a corpus-coverage meta-test. INV-007a = flip-independent
// kernel-branch corpus [unit]; INV-007b = committed-block current-state binding
// [integration, covered by INV-006].
public class Inv007FixtureCorpusTests
{
    public static readonly string[] RequiredCorpusRowIds =
    {
        "null-false-false-consistent",
        "null-true-fail",
        "null-false-true-blocked-but-actually-satisfied",
        "nonnull-unresolvable-hard-fail-declared-false",
        "nonnull-unresolvable-hard-fail-declared-true",
        "nonnull-malformed-hard-fail",
        "nonnull-resolved-crosscheck-mismatch",
        "ready-with-all-true-all-resolved-pass",
        "ready-with-any-false-fail",
        "ready-with-declared-false-consistent-fail",
        "indeterminate-fail",
        "blocked-all-probes-true",
        "satisfied-true-probe-true-unregistered-evidence-fail",
    };

    private static JsonElement LoadCorpus()
    {
        string json = File.ReadAllText(TestPaths.Fixture("kernel-corpus", "supplied-corpus.json"));
        return JsonDocument.Parse(json).RootElement;
    }

    // Tests INV-007 [unit]: corpus-coverage meta-test — every required row id present.
    [Fact]
    public void Corpus_covers_every_required_row_id()
    {
        var present = LoadCorpus().GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("id").GetString())
            .ToHashSet();
        foreach (var id in RequiredCorpusRowIds)
        {
            Assert.Contains(id, present);
        }
    }

    // Tests INV-007a [unit]: the kernel is DRIVEN over every committed corpus row and
    // produces the expected verdict. RED against the stub kernel + factories.
    [Fact]
    public void Kernel_matches_expected_verdict_for_every_corpus_row()
    {
        foreach (var row in LoadCorpus().GetProperty("rows").EnumerateArray())
        {
            var status = Enum.Parse<ReadinessStatus>(row.GetProperty("block_status").GetString()!);
            var pc = row.GetProperty("precondition");
            var id = Enum.Parse<PreconditionId>(pc.GetProperty("id").GetString()!);
            bool satisfied = pc.GetProperty("satisfied").GetBoolean();
            string? evidence = pc.GetProperty("evidence").ValueKind == JsonValueKind.Null
                ? null : pc.GetProperty("evidence").GetString();
            var probe = row.GetProperty("probe");
            bool actual = probe.GetProperty("satisfied").GetBoolean();
            var rr = Enum.Parse<ReferenceResolution>(probe.GetProperty("referenceResolution").GetString()!);
            var expected = Enum.Parse<VerdictKind>(row.GetProperty("expected_verdict").GetString()!);

            ReadinessBlock block = status == ReadinessStatus.Indeterminate
                ? ReadinessBlock.Indeterminate()
                : BuildBlock(status, id, satisfied, evidence);

            var probes = BuildProbes(id, actual, rr, fillerActual: status == ReadinessStatus.READY);
            var v = ReadinessGate.EvaluateReadiness(block, probes);
            Assert.Equal(expected, v.Kind);
        }
    }

    private static ReadinessBlock BuildBlock(ReadinessStatus status, PreconditionId id, bool satisfied, string? evidence)
    {
        // The two FILLER preconditions (not under test): for a READY block they must be
        // genuinely satisfied+evidenced so the row isolates the ONE varied precondition
        // under INV-005's global READY rule; a BLOCKED block keeps them declared-false/null
        // (the Stage-A consistent shape). Without this, a READY row could not express
        // "all preconditions satisfied" and the READY rule would be untestable.
        bool fillerSat = status == ReadinessStatus.READY;
        string? fillerEv = fillerSat ? "filler-ev" : null;
        var pcs = new List<ReadinessPrecondition>
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", fillerSat, fillerEv, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", fillerSat, fillerEv, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", fillerSat, fillerEv, Array.Empty<string>()),
        };
        pcs[(int)id] = ReadinessPrecondition.Create(id, id.ToString(), satisfied, evidence, Array.Empty<string>());
        return ReadinessBlock.TryCreate(1, status, "P1 AND P2 AND P3", pcs)!;
    }

    private static IReadOnlyDictionary<PreconditionId, ProbeResult> BuildProbes(
        PreconditionId id, bool actual, ReferenceResolution rr, bool fillerActual)
    {
        var map = new Dictionary<PreconditionId, ProbeResult>
        {
            [PreconditionId.P1] = ProbeResult.TryCreate(fillerActual, "filler", ReferenceResolution.Resolved)!,
            [PreconditionId.P2] = ProbeResult.TryCreate(fillerActual, "filler", ReferenceResolution.Resolved)!,
            [PreconditionId.P3] = ProbeResult.TryCreate(fillerActual, "filler", ReferenceResolution.Resolved)!,
        };
        map[id] = ProbeResult.TryCreate(actual, "probe", rr)!;
        return map;
    }
}
