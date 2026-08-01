using System;
using System.IO;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation MA-B wiring (INV-010/018/019) — the <see cref="P3Probe"/> present-and-
/// VALID branch is WIRED to <see cref="DeterminismVerifier.Verify"/>, not a stub. Drives the probe
/// over an injected temp repo root that carries a valid minimal pointer + the committed POS fixture
/// {receipt, bundle} under <c>test/attestations/inv010/&lt;commit&gt;/</c>, with the cosign + trust-root
/// seam injected. A stub P3Probe would return <c>p3-not-yet-activated</c>; the wired probe reaches
/// the real verifier and returns a typed carrier reason. The production pointer is ABSENT, so the
/// real gate keeps P3 false (guarded separately by Inv009And010ProbesTests).
/// </summary>
public class Inv019P3ProbeWiringTests
{
    private const string PosCommit = "14701a99367f76b3e46b7261afc1f5c3dd490244";

    // Materialize a temp repo root: the pointer at the pinned path + the POS fixture receipt/bundle
    // under the family root's <commit> dir. Returns the temp root.
    private static string MakePointerTree()
    {
        string root = Path.Combine(Path.GetTempPath(), "p3-wire-" + Guid.NewGuid().ToString("N"));
        string versioned = Path.Combine(root, "test", "attestations", "inv010", PosCommit);
        Directory.CreateDirectory(versioned);

        File.Copy(
            TestPaths.RepoFile("test", "attestations", "fixtures", "pos", "determinism-receipt.json"),
            Path.Combine(versioned, "determinism-receipt.json"));
        File.Copy(
            TestPaths.RepoFile("test", "attestations", "fixtures", "pos", "determinism.sigstore.json"),
            Path.Combine(versioned, "determinism.sigstore.json"));

        string pointer = Path.Combine(root, "test", "attestations", "inv010-determinism.json");
        string relDir = $"test/attestations/inv010/{PosCommit}";
        File.WriteAllText(pointer,
            "{\n" +
            "  \"family\": \"p3-active-baseline\",\n" +
            $"  \"receipt\": \"{relDir}/determinism-receipt.json\",\n" +
            $"  \"bundle\": \"{relDir}/determinism.sigstore.json\",\n" +
            $"  \"attested_commit\": \"{PosCommit}\"\n" +
            "}\n");
        return root;
    }

    private static string WriteFakeCosign(string dir, int exitCode)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "fake-cosign.sh");
        File.WriteAllText(path, $"#!/bin/sh\nexit {exitCode}\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return path;
    }

    private static string WriteTrustRoot(string dir)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "trusted_root.json");
        File.WriteAllText(path, "{}");
        return path;
    }

    // Tests INV-010/018/019 [integration] (MA-B): a present-and-valid pointer drives the REAL
    // verifier. With a fake cosign that exits 0 the crypto/byte-equality layer passes and the layer-1
    // claim policy runs; the injected temp tree carries NONE of the pinned subject files, so the
    // signed subject-manifest digest no longer matches HEAD -> the REAL staleness producer reports
    // stale -> the probe returns `stale-subject-manifest`. A stub P3Probe could never reach this.
    [Fact]
    public void Present_valid_pointer_reaches_the_verifier_and_reports_real_staleness()
    {
        string root = MakePointerTree();
        string seam = Path.Combine(Path.GetTempPath(), "p3-seam-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ctx = GateContext.ForRepoRootWithVerify(
                root, WriteFakeCosign(seam, 0), WriteTrustRoot(seam));

            ProbeResult r = new P3Probe().Evaluate(ctx);

            Assert.False(r.Satisfied);
            // Reached the verifier boundary — NOT the pre-activation stub, NOT a parse/resolve reject.
            Assert.Contains(r.Reason, DeterminismVerifier.CarrierProbeReasonTokens);
            Assert.NotEqual(ProbeReasons.P3NotYetActivated, r.Reason);
            Assert.NotEqual(ProbeReasons.ValidatorDeferred, r.Reason);
            // The real staleness producer fired: the temp tree's subject set is empty, so the signed
            // manifest digest is stale.
            Assert.Equal("stale-subject-manifest", r.Reason);
        }
        finally { Cleanup(root); Cleanup(seam); }
    }

    // Tests INV-010 [integration] (MA-B, deterministic wiring proof): a present-and-valid pointer with
    // a BOGUS cosign binary drives Verify, whose cosign launch fails -> the probe returns the typed
    // `verifier-unavailable` (retryable). This proves the wiring invokes the cosign seam (it is not a
    // stub) without depending on byte-equality.
    [Fact]
    public void Present_valid_pointer_with_bogus_cosign_is_verifier_unavailable()
    {
        string root = MakePointerTree();
        string seam = Path.Combine(Path.GetTempPath(), "p3-seam-" + Guid.NewGuid().ToString("N"));
        try
        {
            string bogusCosign = Path.Combine(seam, "does-not-exist-cosign");
            var ctx = GateContext.ForRepoRootWithVerify(root, bogusCosign, WriteTrustRoot(seam));

            ProbeResult r = new P3Probe().Evaluate(ctx);

            Assert.False(r.Satisfied);
            Assert.Equal("verifier-unavailable", r.Reason);
        }
        finally { Cleanup(root); Cleanup(seam); }
    }

    // Tests INV-028/019 [integration] (fail-closed): a present-and-valid-shaped pointer whose named
    // receipt is NOT committed (dangling) fails closed as evidence-absent — the probe resolved and
    // validated the pointer against the committed set, then rejected the missing target.
    [Fact]
    public void Present_pointer_with_dangling_target_is_evidence_absent()
    {
        string root = Path.Combine(Path.GetTempPath(), "p3-dangle-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Write ONLY the pointer (no receipt/bundle on disk -> dangling).
            string pointer = Path.Combine(root, "test", "attestations", "inv010-determinism.json");
            Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
            string relDir = $"test/attestations/inv010/{PosCommit}";
            File.WriteAllText(pointer,
                "{\n  \"family\": \"p3-active-baseline\",\n" +
                $"  \"receipt\": \"{relDir}/determinism-receipt.json\",\n" +
                $"  \"bundle\": \"{relDir}/determinism.sigstore.json\",\n" +
                $"  \"attested_commit\": \"{PosCommit}\"\n}}\n");

            var ctx = GateContext.ForRepoRoot(root);
            ProbeResult r = new P3Probe().Evaluate(ctx);

            Assert.False(r.Satisfied);
            Assert.Equal("evidence-absent", r.Reason);
        }
        finally { Cleanup(root); }
    }

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* OS temp cleanup is the backstop */ }
    }
}
