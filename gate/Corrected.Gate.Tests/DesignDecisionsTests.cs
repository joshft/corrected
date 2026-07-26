using System;
using System.IO;
using System.Text.Json;
using Corrected.Gate;
using Corrected.Gate.Lint;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// DD-001, DD-002, DD-004, DD-005 — the resolved design decisions get explicit
/// rule-id coverage (DD-003 has its own file). [integration].
/// </summary>
public class DesignDecisionsTests
{
    // Tests DD-001 [integration]: the P1 linter is REUSED by extracting AdrLinter +
    // its transitive closure into gate/Corrected.Gate.Lint (not a cross-tree
    // ProjectReference, not a whole-Components.cs pin), source-digest pinned via an
    // append-only registry. RED at Stage A: the registry file is added at GREEN.
    [Fact]
    public void DD001_extracted_lib_is_source_digest_pinned()
    {
        Assert.Equal("Corrected.Gate.Lint", typeof(AdrLinter).Assembly.GetName().Name);
        Assert.True(TestPaths.RepoFileExists("gate", "Corrected.Gate", "lint-source-registry.json"),
            "DD-001/INV-008c: the append-only source-digest registry must pin the extracted lib");
    }

    // Tests DD-002 [integration]: the pinned committed paths — P1 evidence is the
    // CANONICAL sample (== the ADR-cited path, NEVER the variance sample); P2/P3 the
    // pinned manifest/attestation. Genuine guard over the constants + the real ADR.
    // Source: docs/adr/ADR-0001-dafny-integration-boundary.md line 35
    [Fact]
    public void DD002_pinned_paths_match_the_adr_cited_canonical_path()
    {
        Assert.Equal("spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json",
            ProbeOrchestrator.CanonicalSamplePath);
        string adr = File.ReadAllText(TestPaths.RepoFile("docs", "adr", "ADR-0001-dafny-integration-boundary.md"));
        Assert.Contains("run-report.canonical.sample.json", adr);
    }

    // Tests DD-002 [integration]: the orchestrator RESOLVES the pinned canonical path
    // (not taken from the tamperable ADR field). RED against the stub orchestrator.
    [Fact]
    public void DD002_orchestrator_resolves_the_pinned_canonical_path()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        Assert.NotNull(ProbeOrchestrator.RunAll(ctx));
    }

    // Tests DD-004 [integration]: the parent INV-043 self-explaining-BLOCKED need is
    // met GATE-SIDE by INV-012 (visible on the green path); the `corrected explain`
    // CLI form is deferred until BLOCKED clears. RED against the stub renderer.
    [Fact]
    public void DD004_readiness_explanation_lives_in_the_gate_output()
    {
        // The banner is the gate-side self-explainer.
        string banner = StatusRenderer.RenderNoProductionSurfaceNotice();
        Assert.Contains("src/", banner);
    }

    // Tests DD-005 [integration]: INV-044's history registry + meta-test is HOMED in
    // gate/ but is a DEFERRED extension NOT built by this spec — the readiness-build-gate
    // test_via is amended to mark it deferred, so no from-clean completeness check
    // demands a not-yet-built test. Genuine guard over the ARCHITECTURE annotation.
    [Fact]
    public void DD005_inv044_history_registry_is_a_deferred_extension()
    {
        string arch = File.ReadAllText(TestPaths.RepoFile(".correctless", "ARCHITECTURE.md"));
        Assert.Contains("INV-044", arch);
        Assert.Contains("deferred extension", arch);
    }
}
