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

    // The run-twice-and-diff [integration] determinism guarantee (INV-010) now lives
    // in PRODUCTION — the extracted serial lane scripts/determinism-lane.sh drives the
    // two nested runs and emits the per-run/per-role receipt — exercised by
    // Pr1DeterminismLaneTests (which binds all 5 roles across the two runs). The old
    // in-test RunTwice_DeterministicProjectionsIdentical (with its silent
    // resource-floor early-return) is therefore REMOVED; the lane test carries a LOUD
    // TYPED floor skip instead. The cross-run-consumer completeness guarantee is
    // preserved and re-pointed below.

    // Tests INV-010/INV-002/MA-ED-1 [unit] (class fix, re-pointed to the PR1 lane):
    // EVERY schema-declared report KIND has a CROSS-RUN projection-equality consumer.
    // The committed role registry maps each ROLE to its kind; every declared kind must
    // be covered by a role, and the determinism-lane consumer (Pr1DeterminismLaneTests)
    // must reference EVERY role AND bind BOTH runs' evidence + projection — so
    // declaring a new kind, dropping a role's cross-run projection check, or deleting
    // the consumer's two-run binding fails here. Anchored to the schema digest
    // (RS-005 — derived against THIS schema; registries are committed, never in-test
    // literals — RS-020).
    [Fact]
    public void EveryDeclaredReportKind_HasCrossRunEqualityConsumer()
    {
        Assert.Equal(SpecConstants.EvidenceSchemaSha256,
            SpikePaths.Sha256File(SpikePaths.P("schema", "evidence-schema.json"))); // derived against THIS schema
        using var doc = SpikePaths.Json(SpikePaths.P("schema", "evidence-schema.json"));
        var declaredKinds = doc.RootElement.GetProperty("report_schema").GetProperty("properties")
            .GetProperty("kind").GetProperty("enum").EnumerateArray().Select(k => k.GetString()!).ToHashSet();

        // Every declared KIND is covered by a ROLE in the committed role registry (a
        // kind with no role would have no cross-run consumer).
        var roleToKind = DeterminismRegistries.RoleToKind(SpikePaths.P("manifest", "determinism", "role-registry.json"));
        Assert.Equal(declaredKinds, roleToKind.Values.ToHashSet());

        // The cross-run consumer (the determinism-lane test) references EVERY role,
        // binds BOTH runs' evidence, and computes the deterministic projection.
        var consumer = File.ReadAllText(SpikePaths.P("tests", "SpikeTests", "Pr1DeterminismLaneTests.cs"));
        foreach (var role in roleToKind.Keys)
        {
            Assert.Contains($"\"{role}\"", consumer);
        }
        Assert.Contains("Run1Evidence", consumer);
        Assert.Contains("Run2Evidence", consumer);
        Assert.Contains("DeterministicProjection", consumer);
    }
}
