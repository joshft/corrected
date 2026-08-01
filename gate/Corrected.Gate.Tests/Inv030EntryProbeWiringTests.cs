using System;
using System.IO;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 phase-entry INV-030 (Group G / MA-C part e) — the <see cref="EntryIntegrityProbe"/> makes
/// <see cref="EntryVerifier"/> LIVE-callable from the gate (the entry analog of the MA-B P3Probe
/// wiring). It resolves the durable entry-activation pointer under context.RepoRoot:
///   * ABSENT pointer  -> <see cref="EntryIntegrity.Absent"/> (the pre-entry zero-state; the src/ ban
///                        stays active, readiness stays BLOCKED). This is the PR2 LIVE state (Group G
///                        dormant, the committed block is v1) — the load-bearing no-behavior-change cell.
///   * PRESENT pointer -> parse + resolve the committed {commit-X blob, entry bundle} + drive
///                        EntryVerifier under the PRODUCTION identity -> the typed EntryIntegrity verdict.
/// A stub would return Absent for a present-valid pointer; the wired probe reaches the real verifier
/// (proven by the real ancestry gate firing + the cosign seam being invoked). The production ACCEPT
/// branch is unexercisable until P2 (RS-006/RS-011); the full Group-G activation orchestrator is P2.
///
/// [Collection("Subprocess")] — the present-valid cells fork/exec a fake cosign.
/// </summary>
[Collection("Subprocess")]
public class Inv030EntryProbeWiringTests
{
    // The POS entry fixture's commit-X (== EntryVerifyIdentity.FixtureCertWorkflowSha); the versioned
    // pointer dir under the entry-evidence fixed root uses it.
    private const string PosCommit = "25db9a3cca316e6afd1d33df98f5596ea0cb2dba";

    // ================= ABSENT pointer -> Absent (the PR2 live behavior) =================

    // Tests INV-030 [integration] (LOAD-BEARING — no behavior change): with NO entry-activation pointer
    // (the PR2 state, Group G dormant) the probe resolves the pre-entry zero-state Absent, so the src/
    // ban stays active and readiness stays BLOCKED. Driven over an injected empty temp root.
    [Fact]
    public void Absent_pointer_is_entry_integrity_absent()
    {
        string root = Path.Combine(Path.GetTempPath(), "entry-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(EntryIntegrity.Absent, EntryIntegrityProbe.Evaluate(GateContext.ForRepoRoot(root)));
        }
        finally { Cleanup(root); }
    }

    // Tests INV-030 [integration] (the REAL production repo is pre-entry): over the ACTUAL committed
    // repo root, the entry-activation pointer is ABSENT (Group G dormant), so the live probe yields
    // Absent — never the accepting Verified. This pins the PR2 production behavior directly.
    [Fact]
    public void Real_repo_root_entry_pointer_is_absent()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        Assert.Equal(EntryIntegrity.Absent, EntryIntegrityProbe.Evaluate(ctx));
    }

    // ================= PRESENT pointer -> reaches the real verifier =================

    // Tests INV-030 [integration] (wiring proof — reaches the verifier + real ancestry fires): a
    // present-and-valid pointer to the committed POS entry fixture, with a fake cosign that exits 0, so
    // the crypto/schema layer passes and the layer's real ancestry gate runs. The injected temp tree is
    // NOT a git repo, so GitAncestry cannot compute commit-X ancestry -> the verifier rejects
    // (ancestry-uncomputable) -> the probe returns Rejected. A stub would return Absent; a fake-ok
    // cosign alone can never reach Verified because the real ancestry input is fail-closed.
    [Fact]
    public void Present_valid_pointer_reaches_verifier_and_real_ancestry_fails_closed()
    {
        string root = MakePointerTree();
        string seam = Path.Combine(Path.GetTempPath(), "entry-seam-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ctx = GateContext.ForRepoRootWithVerify(root, WriteFakeCosign(seam, 0), WriteTrustRoot(seam));

            EntryIntegrity r = EntryIntegrityProbe.Evaluate(ctx);

            Assert.NotEqual(EntryIntegrity.Verified, r); // never Verified (fail-closed)
            Assert.NotEqual(EntryIntegrity.Absent, r);   // NOT the pre-activation stub — it reached the verifier
            Assert.Equal(EntryIntegrity.Rejected, r);    // the real ancestry gate fired (temp root is non-git)
        }
        finally { Cleanup(root); Cleanup(seam); }
    }

    // Tests INV-030 [integration] (wiring proof — invokes the cosign seam): a present-and-valid pointer
    // with a BOGUS cosign binary drives Verify, whose cosign launch fails -> the probe returns the typed
    // Unavailable (retryable). Proves the wiring invokes the cosign seam (not a stub) without depending
    // on byte-equality.
    [Fact]
    public void Present_valid_pointer_with_bogus_cosign_is_unavailable()
    {
        string root = MakePointerTree();
        string seam = Path.Combine(Path.GetTempPath(), "entry-seam-" + Guid.NewGuid().ToString("N"));
        try
        {
            string bogusCosign = Path.Combine(seam, "does-not-exist-cosign");
            var ctx = GateContext.ForRepoRootWithVerify(root, bogusCosign, WriteTrustRoot(seam));

            Assert.Equal(EntryIntegrity.Unavailable, EntryIntegrityProbe.Evaluate(ctx));
        }
        finally { Cleanup(root); Cleanup(seam); }
    }

    // Tests INV-030 [integration] (missing seam -> Unavailable): a present-and-valid pointer with NO
    // cosign/trust-root seam (neither injected nor in the env) is a fail-closed Unavailable, never a
    // silent accept.
    [Fact]
    public void Present_valid_pointer_without_cosign_seam_is_unavailable()
    {
        string root = MakePointerTree();
        try
        {
            // ForRepoRoot injects no seam; clear the env so the fallback is genuinely absent.
            string? savedBin = Environment.GetEnvironmentVariable("COSIGN_BIN");
            string? savedRoot = Environment.GetEnvironmentVariable("TRUSTED_ROOT");
            Environment.SetEnvironmentVariable("COSIGN_BIN", null);
            Environment.SetEnvironmentVariable("TRUSTED_ROOT", null);
            try
            {
                Assert.Equal(EntryIntegrity.Unavailable, EntryIntegrityProbe.Evaluate(GateContext.ForRepoRoot(root)));
            }
            finally
            {
                Environment.SetEnvironmentVariable("COSIGN_BIN", savedBin);
                Environment.SetEnvironmentVariable("TRUSTED_ROOT", savedRoot);
            }
        }
        finally { Cleanup(root); }
    }

    // ================= PRESENT pointer -> fail-closed shapes =================

    // Tests INV-030 [integration] (dangling -> Absent): a present-and-valid-shaped pointer whose named
    // target is NOT committed (dangling) fails closed to Absent — the probe resolved + validated the
    // pointer against the committed set, then classified the missing target as the zero-state.
    [Fact]
    public void Present_pointer_with_dangling_target_is_absent()
    {
        string root = Path.Combine(Path.GetTempPath(), "entry-dangle-" + Guid.NewGuid().ToString("N"));
        try
        {
            WritePointer(root, "entry-evidence", PosCommit); // pointer only, no fixture on disk -> dangling
            Assert.Equal(EntryIntegrity.Absent, EntryIntegrityProbe.Evaluate(GateContext.ForRepoRoot(root)));
        }
        finally { Cleanup(root); }
    }

    // Tests INV-030 [integration] (malformed pointer -> Rejected): a pointer file that is not valid JSON
    // fails closed to Rejected (a present-but-broken pointer is a tamper, not the benign zero-state).
    [Fact]
    public void Present_pointer_malformed_json_is_rejected()
    {
        string root = Path.Combine(Path.GetTempPath(), "entry-malformed-" + Guid.NewGuid().ToString("N"));
        try
        {
            string pointer = Path.Combine(root, "test", "attestations", "entry-activation.json");
            Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
            File.WriteAllText(pointer, "{ not valid json ]]]");
            Assert.Equal(EntryIntegrity.Rejected, EntryIntegrityProbe.Evaluate(GateContext.ForRepoRoot(root)));
        }
        finally { Cleanup(root); }
    }

    // Tests INV-030 [integration] (RS-024 cross-rejection — wrong family -> Rejected): an entry-activation
    // pointer carrying the DETERMINISM family (p3-active-baseline) is rejected — the entry gate only
    // accepts the entry-evidence family.
    [Fact]
    public void Present_pointer_wrong_family_is_rejected()
    {
        string root = Path.Combine(Path.GetTempPath(), "entry-wrongfam-" + Guid.NewGuid().ToString("N"));
        try
        {
            WritePointer(root, "p3-active-baseline", PosCommit); // wrong family for the entry gate
            Assert.Equal(EntryIntegrity.Rejected, EntryIntegrityProbe.Evaluate(GateContext.ForRepoRoot(root)));
        }
        finally { Cleanup(root); }
    }

    // ---- helpers ----

    // Materialize a temp repo root: the entry-activation pointer at the pinned path + the committed POS
    // entry fixture {commit-X blob, bundle} under the entry-evidence family root's <commit> dir.
    private static string MakePointerTree()
    {
        string root = Path.Combine(Path.GetTempPath(), "entry-wire-" + Guid.NewGuid().ToString("N"));
        string versioned = Path.Combine(root, "test", "attestations", "entry", PosCommit);
        Directory.CreateDirectory(versioned);

        File.Copy(
            TestPaths.RepoFile("test", "attestations", "fixtures", "entry", "pos", "entry-commit.blob"),
            Path.Combine(versioned, "entry-commit.blob"));
        File.Copy(
            TestPaths.RepoFile("test", "attestations", "fixtures", "entry", "pos", "entry.sigstore.json"),
            Path.Combine(versioned, "entry.sigstore.json"));

        WritePointer(root, "entry-evidence", PosCommit);
        return root;
    }

    // Write the durable entry-activation pointer (receipt = the commit-X blob; bundle = the sigstore bundle).
    private static void WritePointer(string root, string family, string commit)
    {
        string pointer = Path.Combine(root, "test", "attestations", "entry-activation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
        string relDir = $"test/attestations/entry/{commit}";
        File.WriteAllText(pointer,
            "{\n" +
            $"  \"family\": \"{family}\",\n" +
            $"  \"receipt\": \"{relDir}/entry-commit.blob\",\n" +
            $"  \"bundle\": \"{relDir}/entry.sigstore.json\",\n" +
            $"  \"attested_commit\": \"{commit}\"\n" +
            "}\n");
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

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* OS temp cleanup is the backstop */ }
    }
}
