using System;
using System.IO;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-009 (P2) stays fail-closed with `validator-deferred` UNCONDITIONALLY until the P2
/// validator lands. INV-010 (P3) is now REAL (P3 determinism-attestation, RS-025): the P3Probe
/// calls the real verifier. PR3 ACTIVATED the production pointer
/// `test/attestations/inv010-determinism.json`, so over the real tree the probe verifies the
/// committed baseline (satisfied + ran-passed when cosign is provisioned; the honest
/// verifier-unavailable fallback otherwise) — never the old `validator-deferred` stub. ABSENT
/// evidence still fails closed to a REJECTED zero-state reason, asserted hermetically over a
/// synthetic root. Readiness stays BLOCKED (P2 still false). NAMED migration site (RS-025). [integration].
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

    // Tests INV-010 [integration] (RS-025 real-probe migration): the P3Probe is the REAL verifier,
    // not the old `validator-deferred` stub — and ABSENT evidence fails closed to a REJECTED
    // zero-state reason (evidence-absent / p3-not-yet-activated), NEVER validator-deferred. Driven
    // over a SYNTHETIC injected repo-root with NO pointer, so the invariant is hermetic and holds
    // regardless of the real tree — which, post-PR3, now carries the committed production baseline
    // (see P3_real_probe_verifies_the_committed_baseline below).
    [Fact]
    public void P3_real_probe_rejects_absent_evidence_not_validator_deferred()
    {
        string root = Path.Combine(Path.GetTempPath(), "p3-absent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ctx = GateContext.ForRepoRoot(root); // no pointer under this synthetic root
            ProbeResult r = new P3Probe().Evaluate(ctx);

            Assert.False(r.Satisfied);
            Assert.NotEqual(ProbeReasons.ValidatorDeferred, r.Reason);
            Assert.Contains(r.Reason, new[] { "evidence-absent", "p3-not-yet-activated" });
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { /* OS temp cleanup is the backstop */ }
        }
    }

    // Tests INV-010/INV-020 [integration] (PR3 evidence activation): over the REAL committed tree the
    // P3Probe RESOLVES the activated baseline pointer and runs the real verifier — never the
    // evidence-absent zero-state, never validator-deferred. Provisioning-aware: with cosign
    // provisioned (the gate wrapper) the committed production bundle verifies offline -> satisfied +
    // ran-passed; a bare `dotnet test` leaves the seam unset -> the honest verifier-unavailable
    // fallback (satisfied:false).
    [Fact]
    public void P3_real_probe_verifies_the_committed_baseline()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        ProbeResult r = new P3Probe().Evaluate(ctx);

        Assert.NotEqual(ProbeReasons.ValidatorDeferred, r.Reason);
        Assert.NotEqual("evidence-absent", r.Reason);
        Assert.NotEqual("p3-not-yet-activated", r.Reason);

        bool provisioned = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COSIGN_BIN"))
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRUSTED_ROOT"));
        if (provisioned)
        {
            Assert.True(r.Satisfied);
            Assert.Equal("ran-passed", r.Reason);
        }
        else
        {
            Assert.False(r.Satisfied);
            Assert.Equal("verifier-unavailable", r.Reason);
        }
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
