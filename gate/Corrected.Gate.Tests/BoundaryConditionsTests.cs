using System;
using System.IO;
using System.Reflection;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// BND-001..003 — the boundary conditions. TB-006 readiness/ADR/evidence intake +
/// TB-004 toolchain, each fail-closed.
/// </summary>
public class BoundaryConditionsTests
{
    // Tests BND-001 [unit]: the readiness block (committed, tamperable markdown) is
    // TB-006 input — a duplicate block fails closed (validation INV-001/002/003/005).
    // B5: assert the SPECIFIC typed exception + NAMED reason (MultipleReadinessBlocks),
    // not ThrowsAny. RED against the NotImplemented stub (which throws the WRONG type).
    [Fact]
    public void BND001_duplicate_readiness_block_fails_closed()
    {
        string md = File.ReadAllText(TestPaths.Fixture("readiness", "two-blocks.md"));
        var ex = Assert.Throws<ReadinessExtractionException>(
            () => ReadinessBlockParser.ExtractSingleBlock(md));
        Assert.Equal(ReadinessExtractionReason.MultipleReadinessBlocks, ex.Reason);
    }

    // Tests BND-002 [integration]: the YAML parser + analysis toolchain + SDK is
    // TB-004 input — pin+lock+CI-verify (INV-015/016). The YAML parser is pinned +
    // loadable. RED until GREEN adds YamlDotNet 18.1.0.
    [Fact]
    public void BND002_yaml_parser_is_pinned_and_loadable()
    {
        Assembly asm = Assembly.Load("YamlDotNet");
        // AssemblyVersion is frozen at 18.0.0.0 (YamlDotNet policy); the 18.1.0 pin is
        // in FileVersion. Assert FileVersion for the exact pin (see INV-015 test).
        Assert.Equal("18.1.0.0",
            System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location).FileVersion);
    }

    // Tests BND-002 [integration]: no ambient MSBuild resolution — no Microsoft.Build.*
    // PackageReference (the in-process MSBuild API INV-011 forbids). Genuine guard.
    [Fact]
    public void BND002_no_in_process_msbuild_package()
    {
        foreach (var proj in new[] { "Corrected.Gate", "Corrected.Gate.Kernel", "Corrected.Gate.Tests", "Corrected.Gate.Lint" })
        {
            Assert.DoesNotContain("Microsoft.Build",
                File.ReadAllText(TestPaths.RepoFile("gate", proj, proj + ".csproj")));
        }
    }

    // Tests BND-003 [integration]: the ADR + evidence the P1 probe reads (committed,
    // tamperable) is TB-006 input — a forged decision field (selected_route:B /
    // route-A verdict:INCOMPATIBLE) fails closed (P1 -> typed false). RED against the
    // stub parser/probe. Uses a forged inline ADR fixture.
    [Fact]
    public void BND003_forged_decision_field_fails_closed()
    {
        const string forged =
            "```yaml\nadr_lint:\n  boundary_decision: in-process-selected\n  selected_route: B\n" +
            "  status: accepted\n  superseded_by: null\n  routes:\n    - route: A\n      verdict: INCOMPATIBLE\n" +
            "      adjudication_record_id: null\n      evidence: x\n```";
        AdrParseResult r = AdrLintBlockParser.Parse(forged);
        Assert.Equal(AdrParseOutcome.Ok, r.Outcome); // parses structurally...
        Assert.NotEqual("A", r.Block!.SelectedRoute); // ...but the decision field is forged -> fail closed
    }

    // Tests BND-003 [integration]: the ADR the P1 probe reads is TB-006 input — drive
    // the REAL probe over a SYNTHESIZED migrated temp tree whose ADR carries a forged
    // selected_route:B. The (a) decision-field assert (selected_route==A) catches it ->
    // P1 typed-false with evidence-refutes (the pinned evidence files are intact, so it
    // is a semantic refutation, not a sha-malformed). RED against the stub probe.
    [Fact]
    public void BND003_selected_route_b_drives_probe_false()
    {
        using var tree = P1Tree.Build(P1Mutation.SelectedRouteB);
        ProbeResult r = new P1Probe().Evaluate(GateContext.ForRepoRoot(tree.Root));
        Assert.False(r.Satisfied);
        Assert.NotEqual(ProbeReasons.EvidenceSchemaIncomplete, r.Reason);
        Assert.Equal(ProbeReasons.EvidenceRefutes, r.Reason);
    }
}
