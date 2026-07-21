// Tests INV-010: probe verdicts are stable across repeated runs.
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Inv010DeterminismTests
{
    // Tests INV-010 [unit] (RS-005): the comparison set derives from the SCHEMA
    // FILE, so shrinking the projection is a reviewable diff, never a test edit.
    [Fact]
    public void ComparisonSet_DerivesFromSchemaFile_NotFromTestCode()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("schema", "evidence-schema.json"));
        var class2 = doc.RootElement.GetProperty("field_partition")
            .GetProperty("class_2_deterministic_projection")
            .EnumerateArray().Select(f => f.GetString()!).ToList();
        // The equality domain must include the verdict-bearing members; their
        // absence would make INV-010 vacuous.
        Assert.Contains("route_verdicts", class2);
        Assert.Contains("per_probe_results", class2);
        Assert.Contains("node_table", class2);
        Assert.Contains("option_manifest_readback", class2);
        Assert.Contains("loaded_assembly_identities", class2);
    }

    // Tests INV-010 [integration]: run-twice-and-diff — two consecutive harness
    // runs on the same tree produce IDENTICAL deterministic projections (class 2
    // of INV-009's committed partition); binding and volatile fields are
    // excluded by construction. An observed flap is a blocking finding, never a
    // retry-until-green (RS-020).
    [Fact]
    public void RunTwice_DeterministicProjectionsIdentical()
    {
        var scratch = SpikePaths.TestScratch("inv010-run-twice");
        var first = Path.Combine(scratch, "run1.json");
        var second = Path.Combine(scratch, "run2.json");

        var r1 = Launch.Script("scripts/run-spike.sh", null, "--out", first);
        Assert.True(r1.ExitCode == 0, $"first run failed: {r1.StdErr}");
        var r2 = Launch.Script("scripts/run-spike.sh", null, "--out", second);
        Assert.True(r2.ExitCode == 0, $"second run failed: {r2.StdErr}");

        var schemaPath = SpikePaths.P("schema", "evidence-schema.json");
        var p1 = EvidenceSchema.DeterministicProjection(File.ReadAllText(first), schemaPath);
        var p2 = EvidenceSchema.DeterministicProjection(File.ReadAllText(second), schemaPath);
        Assert.True(p1 == p2,
            "deterministic projections differ between consecutive runs — a flap is a BLOCKING finding to investigate; " +
            "moving a flapping field to volatile requires a spec change (INV-010/RS-005)");
    }
}
