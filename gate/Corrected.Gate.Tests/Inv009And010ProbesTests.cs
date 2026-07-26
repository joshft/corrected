using System;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-009 (P2) + INV-010 (P3): fail-closed with `validator-deferred`
/// UNCONDITIONALLY for any input (absent, malformed, present-well-formed) until the
/// validator lands. [integration].
/// </summary>
public class Inv009And010ProbesTests
{
    // Tests INV-009 [integration]: P2 resolves the pinned completion manifest path
    // (DD-002) but returns validator-deferred unconditionally (a committed stub can
    // never flip P2). RED against the stub probe.
    [Fact]
    public void P2_is_validator_deferred_unconditionally()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        ProbeResult r = new P2Probe().Evaluate(ctx);
        Assert.False(r.Satisfied);
        Assert.Equal(ProbeReasons.ValidatorDeferred, r.Reason);
    }

    // Tests INV-009 [integration]: P2's pinned manifest path constant is exactly
    // test/manifests/phase-0.0-completion.json (DD-002). Genuine guard.
    [Fact]
    public void P2_manifest_path_is_pinned()
    {
        Assert.Equal("test/manifests/phase-0.0-completion.json", ProbeOrchestrator.P2ManifestPath);
    }

    // Tests INV-010 [integration]: P3 resolves the durable attestation path (DD-002)
    // but returns validator-deferred unconditionally. RED against the stub probe.
    [Fact]
    public void P3_is_validator_deferred_unconditionally()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        ProbeResult r = new P3Probe().Evaluate(ctx);
        Assert.False(r.Satisfied);
        Assert.Equal(ProbeReasons.ValidatorDeferred, r.Reason);
    }

    // Tests INV-010 [integration]: P3's durable attestation path constant is exactly
    // test/attestations/inv010-determinism.json (DD-002); a bare committed claim is
    // insufficient (provenance-bound). Genuine guard over the path constant.
    [Fact]
    public void P3_attestation_path_is_pinned_and_durable()
    {
        Assert.Equal("test/attestations/inv010-determinism.json", ProbeOrchestrator.P3AttestationPath);
        // Durability: NOT an ephemeral CI-workspace file (no out/ path).
        Assert.DoesNotContain("out/", ProbeOrchestrator.P3AttestationPath);
    }
}
