using System;
using System.IO;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-009 (P2) stays fail-closed with `validator-deferred` UNCONDITIONALLY until the P2
/// validator lands. INV-010 (P3) is now REAL (P3 determinism-attestation, RS-025): the P3Probe
/// calls the real verifier. The production pointer `test/attestations/inv010-determinism.json`
/// is ABSENT (P3 stays false, readiness BLOCKED), so the probe returns satisfied:false with a
/// REJECTED reason (evidence-absent / p3-not-yet-activated) — NOT `validator-deferred`. This
/// file is a NAMED migration site (RS-025): the old `validator-deferred` P3 test is replaced by
/// the real-probe assertion below. [integration].
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

    // Tests INV-010 [integration] (RS-025 real-probe migration): the P3Probe now calls the real
    // verifier. The production pointer test/attestations/inv010-determinism.json is ABSENT (P3
    // stays false, readiness BLOCKED), so the probe returns satisfied:false with a REJECTED reason
    // (evidence-absent / p3-not-yet-activated) — NOT the old `validator-deferred` stub. RED: the
    // current stub P3Probe returns `validator-deferred`, failing both the != and the membership.
    [Fact]
    public void P3_real_probe_rejects_absent_evidence_not_validator_deferred()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        ProbeResult r = new P3Probe().Evaluate(ctx);

        Assert.False(r.Satisfied);
        Assert.NotEqual(ProbeReasons.ValidatorDeferred, r.Reason);
        // A REJECTED zero-state reason — the committed pointer does not exist yet (DD-002).
        Assert.Contains(r.Reason, new[] { "evidence-absent", "p3-not-yet-activated" });
    }

    // Tests INV-010 [integration] (AP-002 real-routing — the probe actually processes the pointer):
    // drive P3Probe.Evaluate over an injected repo-root temp tree in which the pinned pointer
    // test/attestations/inv010-determinism.json is PRESENT but MALFORMED. A canned
    // `if(!File.Exists(pointer)) return evidence-absent` GREEN cannot satisfy this — a present-but-
    // malformed pointer forces the probe past the existence check into the verifier's parse/verify
    // path, which must fail closed with a malformed-* reason. RED: the stub P3Probe ignores the
    // context and returns validator-deferred.
    [Fact]
    public void P3_real_probe_routes_into_the_verifier_on_a_malformed_pointer()
    {
        string root = Path.Combine(Path.GetTempPath(), "p3-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Materialize the pinned pointer PRESENT but unparseable under the injected repo root.
            string pointer = Path.Combine(root, Path.Combine(ProbeOrchestrator.P3AttestationPath.Split('/')));
            Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
            File.WriteAllText(pointer, "{ this is not valid json <<<");

            var ctx = GateContext.ForRepoRoot(root);
            ProbeResult r = new P3Probe().Evaluate(ctx);

            Assert.False(r.Satisfied);
            Assert.NotEqual(ProbeReasons.ValidatorDeferred, r.Reason);
            // A present-but-malformed pointer is a fail-closed malformed-* reason, NOT evidence-absent
            // (which the existence check would give) — proving the probe read + processed the pointer.
            Assert.Contains("malformed", r.Reason);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { /* OS temp cleanup is the backstop */ }
        }
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
