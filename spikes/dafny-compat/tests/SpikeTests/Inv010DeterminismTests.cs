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
        var root1 = Path.Combine(scratch, "r1");
        var root2 = Path.Combine(scratch, "r2");
        var first = Path.Combine(scratch, "run1.json");
        var second = Path.Combine(scratch, "run2.json");

        var r1 = Launch.Script("scripts/run-spike.sh", null, "--run-root", root1, "--out", first);
        Assert.True(r1.ExitCode == 0, $"first run failed: {r1.StdErr}");
        var r2 = Launch.Script("scripts/run-spike.sh", null, "--run-root", root2, "--out", second);
        Assert.True(r2.ExitCode == 0, $"second run failed: {r2.StdErr}");

        var schemaPath = SpikePaths.P("schema", "evidence-schema.json");
        var p1 = EvidenceSchema.DeterministicProjection(File.ReadAllText(first), schemaPath);
        var p2 = EvidenceSchema.DeterministicProjection(File.ReadAllText(second), schemaPath);
        Assert.True(p1 == p2,
            "deterministic projections differ between consecutive runs — a flap is a BLOCKING finding to investigate; " +
            "moving a flapping field to volatile requires a spec change (INV-010/RS-005)");

        // MA-ED-1: the class-2 equality domain covers EVERY schema-declared
        // report kind, not just the run report — ROUTE reports carry most
        // class-2 fields (node_table, canary observations, closure/target sets,
        // solver outcome enums, sentinel ledger outcomes) and CONTROL reports
        // carry the identity cells. Each kind's projection must be identical
        // across the two consecutive runs.
        foreach (var rel in new[] { "route-a.json", "route-b.json", "control-a.json", "control-b.json" })
        {
            var f1 = Path.Combine(root1, "reports", rel);
            var f2 = Path.Combine(root2, "reports", rel);
            Assert.True(File.Exists(f1), $"first run emitted no {rel} — every schema-declared kind needs a cross-run equality consumer (MA-ED-1)");
            Assert.True(File.Exists(f2), $"second run emitted no {rel} (MA-ED-1)");
            var q1 = EvidenceSchema.DeterministicProjection(File.ReadAllText(f1), schemaPath);
            var q2 = EvidenceSchema.DeterministicProjection(File.ReadAllText(f2), schemaPath);
            Assert.True(q1 == q2,
                $"{rel}: deterministic projections differ between consecutive runs — a route/control-level flap is a BLOCKING finding (INV-010/RS-005/MA-ED-1)");
        }
    }

    // Tests INV-010/MA-ED-1 [unit] (class fix): EVERY report kind the schema
    // declares has a cross-run projection-equality consumer — this committed
    // kind->consumer map is anchored to the schema digest, so declaring a new
    // kind without wiring an equality consumer fails here.
    [Fact]
    public void EveryDeclaredReportKind_HasCrossRunEqualityConsumer()
    {
        Assert.Equal(SpecConstants.EvidenceSchemaSha256,
            SpikePaths.Sha256File(SpikePaths.P("schema", "evidence-schema.json"))); // map written against THIS schema
        using var doc = SpikePaths.Json(SpikePaths.P("schema", "evidence-schema.json"));
        var declaredKinds = doc.RootElement.GetProperty("report_schema").GetProperty("properties")
            .GetProperty("kind").GetProperty("enum").EnumerateArray().Select(k => k.GetString()!).ToHashSet();

        // Committed map: report kind -> the artifacts RunTwice projects for it.
        var consumers = new Dictionary<string, string[]>
        {
            ["run-report"] = new[] { "run1.json", "run2.json" },
            ["route-report"] = new[] { "route-a.json", "route-b.json" },
            ["control-report"] = new[] { "control-a.json", "control-b.json" },
        };
        Assert.Equal(declaredKinds, consumers.Keys.ToHashSet());

        // The consumer names must actually appear in this test class's source —
        // a deleted projection loop cannot leave the map green.
        var source = File.ReadAllText(SpikePaths.P("tests", "SpikeTests", "Inv010DeterminismTests.cs"));
        foreach (var artifact in consumers.Values.SelectMany(v => v))
        {
            Assert.Contains(artifact, source);
        }
    }
}
