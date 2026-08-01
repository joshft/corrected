using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-010 (RS-014) / INV-017 / EA-008 (sections D + E — trusted-root
/// provisioning + the real-cosign path WIRED into the documented gate command). The documented
/// <c>commands.test</c> (<c>gate/run-readiness-gate.sh</c>) must run the online provisioning pre-step
/// (the pinned cosign binary AND the pinned trust root) and EXPORT the <c>COSIGN_BIN</c> +
/// <c>TRUSTED_ROOT</c> env seam BEFORE the offline verify, and the from-clean CI harness must do the
/// same — else the real cosign path never runs on a fresh clone (a phantom, AP-013 / RS-014).
///
/// AUDIT-HARDENED (PMB-001/AP-013 doc-grep phantom): every script scan STRIPS full-line comments
/// first and matches an EXECUTED invocation SHAPE (a regex over <c>bash|sh|source|./ … provision-cosign.sh</c>)
/// or a real assignment (<c>export COSIGN_BIN=</c>) — a bare <c># provision-cosign.sh</c> comment or an
/// <c>echo</c> can satisfy NOTHING. The from-clean harness additionally carries an EXECUTION net
/// (COSIGN_BIN executable + TRUSTED_ROOT present + <c>cosign version</c> == the pinned v3.1.2), and the
/// runtime cell forks the REAL cosign (AP-013 forcing function).
///
/// [Collection("Subprocess")] — the runtime cell forks/execs real cosign.
/// </summary>
[Collection("Subprocess")]
public class Inv010Inv017ProvisioningWiringTests
{
    private const string PinnedCosignVersion = "v3.1.2";

    // An EXECUTED provision invocation: bash/sh/source/./ followed by a path ending provision-cosign.sh.
    private static readonly Regex ProvisionInvocation =
        new(@"(bash|sh|source|\./)\s+\S*provision-cosign\.sh", RegexOptions.Compiled);

    // An EXECUTED gate invocation: bash/sh/source/./ followed by a path ending run-readiness-gate.sh.
    private static readonly Regex GateInvocation =
        new(@"(bash|sh|source|\./)\s+\S*run-readiness-gate\.sh", RegexOptions.Compiled);

    private static readonly Regex Sha256HexRe = new(@"\b[0-9a-f]{64}\b", RegexOptions.Compiled);

    private static string GateScript() => TestPaths.RepoFile("gate", "run-readiness-gate.sh");
    private static string FromCleanHarness() => TestPaths.RepoFile("gate", "ci", "from-clean-gate.sh");

    // Comment-stripped code lines (a full-line comment — first non-space char '#' — is dropped, so a
    // commented-out invocation / an echo'd string can never satisfy a wiring scan).
    private static string[] CodeLines(string path)
        => File.ReadAllText(path).Replace("\r\n", "\n").Split('\n')
            .Where(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal))
            .ToArray();

    private static string CodeText(string path) => string.Join("\n", CodeLines(path));

    private static int FirstIndex(string[] lines, Func<string, bool> pred)
        => Array.FindIndex(lines, new Predicate<string>(pred));

    // ================= section E — provisioning wired into the documented gate command =============

    // Tests INV-010/INV-017 [integration] (RS-014 — provisioning BEFORE the offline verify): the
    // documented gate command EXECUTES the cosign provisioning pre-step (a real
    // bash/sh/source/./ …provision-cosign.sh invocation, comment-stripped) BEFORE the `dotnet test`
    // verify step. RED: run-readiness-gate.sh does not reference provisioning at all.
    [Fact]
    public void Documented_gate_command_provisions_cosign_before_the_verify_step()
    {
        string[] lines = CodeLines(GateScript());
        int provisionIdx = FirstIndex(lines, l => ProvisionInvocation.IsMatch(l));
        int verifyIdx = FirstIndex(lines, l => l.Contains("dotnet test", StringComparison.Ordinal));

        Assert.True(provisionIdx >= 0,
            "RS-014: gate/run-readiness-gate.sh must EXECUTE gate/tools/provision-cosign.sh (an invocation, " +
            "not a comment) — the real cosign path is otherwise unreachable from clean.");
        Assert.True(verifyIdx >= 0, "the gate must run `dotnet test` (the offline verify runs in the suite).");
        Assert.True(provisionIdx < verifyIdx,
            "RS-014: provisioning must run BEFORE the `dotnet test` offline verify step, not after.");
    }

    // Tests INV-017/EA-008 [integration] (the pinned TRUST ROOT is provisioned before verify too): the
    // gate command obtains the pinned trust root (a real assignment/initialize, comment-stripped) before
    // the verify step. RED: the gate references no trusted root / provisioning at all.
    [Fact]
    public void Documented_gate_command_provisions_the_trusted_root_before_the_verify_step()
    {
        string[] lines = CodeLines(GateScript());
        int rootIdx = FirstIndex(lines, l =>
            l.Contains("TRUSTED_ROOT", StringComparison.Ordinal)
            || l.Contains("trusted_root", StringComparison.Ordinal)
            || l.Contains("initialize", StringComparison.Ordinal));
        int verifyIdx = FirstIndex(lines, l => l.Contains("dotnet test", StringComparison.Ordinal));

        Assert.True(rootIdx >= 0,
            "INV-017/EA-008: the gate command must provision/obtain the pinned trust root before the offline verify.");
        Assert.True(rootIdx < verifyIdx,
            "INV-017: the trust root must be provisioned BEFORE the `dotnet test` offline verify step.");
    }

    // Tests INV-010 [integration] (RS-014 env seam — the layer-2 tests locate cosign): after
    // provisioning, the gate command EXPORTS COSIGN_BIN + TRUSTED_ROOT as real assignments (the `=` is
    // required — a comment/echo cannot satisfy it) so the in-suite layer-2 real-cosign tests find the
    // provisioned binary + root. RED: the gate exports neither.
    [Fact]
    public void Documented_gate_command_exports_the_cosign_bin_and_trusted_root_seam()
    {
        string text = CodeText(GateScript());
        Assert.Matches(new Regex(@"export\s+COSIGN_BIN\s*="), text);
        Assert.Matches(new Regex(@"export\s+TRUSTED_ROOT\s*="), text);
    }

    // Tests INV-010/INV-017 [integration] (the FROM-CLEAN CI harness provisions too): the out-of-suite
    // from-clean harness EXECUTES provision-cosign.sh BEFORE it invokes the gate (both comment-stripped
    // executed-invocation regexes), so the from-clean job exercises the real cosign path. RED:
    // from-clean-gate.sh does not provision.
    [Fact]
    public void From_clean_harness_provisions_before_invoking_the_gate()
    {
        string[] lines = CodeLines(FromCleanHarness());
        int provisionIdx = FirstIndex(lines, l => ProvisionInvocation.IsMatch(l));
        int gateIdx = FirstIndex(lines, l => GateInvocation.IsMatch(l));

        Assert.True(provisionIdx >= 0,
            "RS-014: the from-clean harness must EXECUTE provision-cosign.sh (an invocation, not a comment).");
        Assert.True(gateIdx >= 0, "the from-clean harness must invoke gate/run-readiness-gate.sh.");
        Assert.True(provisionIdx < gateIdx, "RS-014: provisioning must precede the gate invocation from clean.");
    }

    // Tests INV-010/INV-015 [integration] (B1b EXECUTION net — not a grep): the from-clean harness, after
    // provisioning, actually VALIDATES the provisioned artifacts — asserts $COSIGN_BIN is executable,
    // $TRUSTED_ROOT exists, and `cosign version` reports the pinned v3.1.2 — so a fresh clone proves the
    // real binary is present and is the pinned version (not merely that a provision line exists). RED:
    // the from-clean harness carries no such execution net.
    [Fact]
    public void From_clean_harness_asserts_the_provisioned_cosign_version_and_files()
    {
        string harness = CodeText(FromCleanHarness());

        Assert.True(Regex.IsMatch(harness, @"-x\s+.*COSIGN_BIN"),
            "B1b: the from-clean harness must assert $COSIGN_BIN is executable ([ -x \"$COSIGN_BIN\" ]).");
        Assert.True(Regex.IsMatch(harness, @"-[ef]\s+.*TRUSTED_ROOT"),
            "B1b: the from-clean harness must assert $TRUSTED_ROOT exists ([ -f \"$TRUSTED_ROOT\" ]).");
        Assert.True(Regex.IsMatch(harness, @"(\$\{?COSIGN_BIN\}?|cosign)\s+version"),
            "B1b: the from-clean harness must run `$COSIGN_BIN version` (an execution, not a grep).");
        Assert.Contains(PinnedCosignVersion, harness); // and assert it reports the pinned v3.1.2
    }

    // Tests RS-015/AP-013 [integration] (off-RID records a TYPED reason, never a silent skip): the
    // gate/provisioning wiring, on a non-linux-x64 host, records a typed `rid-platform-mismatch`
    // (comment-stripped) rather than silently skipping the P3 verify path (EA-003), keyed off the host
    // RID/arch. NOTE the actual off-RID EXECUTION is a recorded residual (this dev/CI host is
    // linux-x64); the wiring branch is asserted here. RED: no off-RID branch / typed token exists.
    [Fact]
    public void Off_rid_gate_records_rid_platform_mismatch_never_silent_skip()
    {
        string combined = CodeText(GateScript()) + "\n" + CodeText(TestPaths.RepoFile("gate", "tools", "provision-cosign.sh"));

        Assert.Contains("rid-platform-mismatch", combined);
        Assert.True(
            combined.Contains("uname -m", StringComparison.Ordinal) ||
            combined.Contains("linux-x64", StringComparison.Ordinal) ||
            combined.Contains("RID", StringComparison.Ordinal),
            "RS-015: the off-RID branch must decide off the host RID/arch, not merely mention the token.");
    }

    // Tests INV-010 [integration] (AP-013 forcing function — the REAL cosign subprocess ACTUALLY
    // executes the frozen verify): when provisioned (COSIGN_BIN + TRUSTED_ROOT + linux-x64), running
    // the REAL pinned cosign via the hardened CosignRunner over the frozen BuildVerifyArgv against the
    // committed POS fixture EXITS OK with genuine "Verified OK" output — proof the real subprocess ran
    // (not a stub/skip). RED: the T3a placeholder BuildVerifyArgv omits the identity/type/claims flags,
    // so real cosign exits non-zero. Honest fallback (a typed non-Ok, never a skip) when unprovisioned.
    [Fact]
    public void Real_cosign_subprocess_actually_executes_the_frozen_verify()
    {
        string? bin = ResolveCosignBin();
        string? root = ResolveTrustedRoot(bin);
        bool provisioned = HostIsLinuxX64() && bin is not null && root is not null;

        string posBundle = TestPaths.RepoFile("test", "attestations", "fixtures", "pos", "determinism.sigstore.json");
        string posReceipt = TestPaths.RepoFile("test", "attestations", "fixtures", "pos", "determinism-receipt.json");
        string workDir = TestPaths.RepoFile("test", "attestations", "fixtures", "pos");

        var req = new DeterminismVerifyRequest
        {
            CosignBinPath = bin ?? "/nonexistent/pinned/cosign",
            BundlePath = posBundle,
            ReceiptPath = posReceipt,
            TrustRootPath = root ?? "/nonexistent/trusted_root.json",
            WorkingDirectory = workDir,
            ExpectedRid = "linux-x64",
            Identity = DeterminismVerifyIdentity.Fixture,
            CertWorkflowSha = DeterminismVerifyIdentity.FixtureCertWorkflowSha,
            Timeout = TimeSpan.FromSeconds(60),
        };

        CosignRunResult run = CosignRunner.Run(new CosignRunOptions
        {
            ExecutablePath = req.CosignBinPath,
            Argv = DeterminismVerifier.BuildVerifyArgv(req),
            WorkingDirectory = req.WorkingDirectory,
            FileInputs = new[] { req.BundlePath, req.ReceiptPath, req.TrustRootPath },
            Timeout = req.Timeout,
        });

        if (!provisioned)
        {
            // RS-015/AP-013: a genuinely-degraded env is a TYPED non-Ok outcome, never a silent skip.
            Assert.NotEqual(CosignOutcome.Ok, run.Outcome);
            return;
        }
        Assert.Equal(CosignOutcome.Ok, run.Outcome);                 // the real subprocess executed
        Assert.Contains("Verified OK", run.StdOut + run.StdErr);     // genuine cosign output (evidence)
    }

    // ================= section D — the trust-root END STATE (present + used + pinned) ==============

    // Tests INV-016 [integration] (the trust root is DIGEST-PINNED — end state, commit-vs-fetch is
    // GREEN's choice): a committed pin artifact associates a 64-hex sha256 with a trusted-root key (a
    // dedicated trusted-root pin config, or a `trusted_root` object carrying its digest in cosign-pin.json)
    // so the offline verify anchors to a frozen versioned root (INV-016), not an unpinned fetch. A bare
    // `trusted_root` SUBSTRING with no 64-hex — or a 64-hex unrelated to any trusted-root key (e.g. the
    // cosign BINARY digest) — does NOT satisfy it. RED: no committed trusted-root digest pin exists yet.
    [Fact]
    public void Pinned_trusted_root_digest_is_a_committed_artifact()
    {
        var candidates = new List<string>();
        foreach (string f in new[] { "trusted-root-pin.json", "trust-root-pin.json" })
        {
            string p = TestPaths.RepoFile("gate", "tools", f);
            if (File.Exists(p)) candidates.Add(File.ReadAllText(p));
        }
        candidates.Add(File.ReadAllText(TestPaths.RepoFile("gate", "tools", "cosign-pin.json")));

        bool pinned = candidates.Any(TrustedRootDigestIsPinned);
        Assert.True(
            pinned,
            "INV-016: a committed pin must associate a 64-hex sha256 with a trusted-root key " +
            "(a trusted-root pin config, or a `trusted_root` object carrying its digest).");
    }

    // Tests INV-016 [integration] (a committed root file, IF present, is digest-pinned): if GREEN
    // chooses to COMMIT the trusted root under the repo, its bytes must be digest-pinned (a committed
    // sha256) — never an unpinned blob. A fetched root leaves this vacuously satisfied (no committed
    // root file). RED-neutral until a root is committed; pairs with the pin-config assertion above.
    [Fact]
    public void Committed_trusted_root_file_if_present_is_digest_pinned()
    {
        string attestDir = TestPaths.RepoFile("test", "attestations");
        var committedRoots = Directory.Exists(attestDir)
            ? Directory.EnumerateFiles(attestDir, "trusted_root*.json", SearchOption.AllDirectories).ToList()
            : new List<string>();

        if (committedRoots.Count == 0)
        {
            // GREEN chose the FETCH design (root provisioned, not committed) — the pin lives in the
            // provisioning config, asserted by Pinned_trusted_root_digest_is_a_committed_artifact.
            return;
        }

        var pinTexts = new List<string> { File.ReadAllText(TestPaths.RepoFile("gate", "tools", "cosign-pin.json")) };
        foreach (string f in new[] { "trusted-root-pin.json", "trust-root-pin.json" })
        {
            string p = TestPaths.RepoFile("gate", "tools", f);
            if (File.Exists(p)) pinTexts.Add(File.ReadAllText(p));
        }
        foreach (string rootFile in committedRoots)
        {
            string digest = FileSha256(rootFile);
            Assert.True(
                pinTexts.Any(t => t.Contains(digest, StringComparison.OrdinalIgnoreCase)),
                $"INV-016: committed trust root {Path.GetFileName(rootFile)} must have its sha256 pinned in a committed config.");
        }
    }

    // ---- helpers ----

    // A committed pin satisfies INV-016 iff a trusted-root-named JSON key's own value (or a nested
    // sha256/digest under it) is a 64-hex sha256 — a JSON-structural check, not a bare substring.
    private static bool TrustedRootDigestIsPinned(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return false; }
        using (doc)
        {
            return WalkForTrustedRootDigest(doc.RootElement);
        }
    }

    private static bool WalkForTrustedRootDigest(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in el.EnumerateObject())
            {
                string key = new string(p.Name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                if ((key.Contains("trustedroot") || key.Contains("trustroot")) && SubtreeHasSha256(p.Value))
                {
                    return true;
                }
                if (WalkForTrustedRootDigest(p.Value)) return true;
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in el.EnumerateArray())
            {
                if (WalkForTrustedRootDigest(e)) return true;
            }
        }
        return false;
    }

    private static bool SubtreeHasSha256(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                return Sha256HexRe.IsMatch(el.GetString() ?? string.Empty);
            case JsonValueKind.Object:
                return el.EnumerateObject().Any(p => SubtreeHasSha256(p.Value));
            case JsonValueKind.Array:
                return el.EnumerateArray().Any(SubtreeHasSha256);
            default:
                return false;
        }
    }

    private static bool HostIsLinuxX64()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.OSArchitecture == Architecture.X64;

    private static string Home()
        => Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string CachedCosign() => Path.Combine(Home(), ".cache", "cosign", "v3.1.2", "cosign-linux-amd64");
    private static string SigstoreRoot()
        => Path.Combine(Home(), ".sigstore", "root", "tuf-repo-cdn.sigstore.dev", "targets", "trusted_root.json");

    private static string? ResolveCosignBin()
    {
        string? bin = FirstReadable(Environment.GetEnvironmentVariable("COSIGN_BIN"), CachedCosign());
        if (bin is null && HostIsLinuxX64()) bin = TryProvisionCosign();
        return bin;
    }

    private static string? ResolveTrustedRoot(string? cosignBin)
    {
        string? root = FirstReadable(Environment.GetEnvironmentVariable("TRUSTED_ROOT"), SigstoreRoot());
        if (root is null && cosignBin is not null && HostIsLinuxX64()) root = TryInitializeTrustedRoot(cosignBin);
        return root;
    }

    // Best-effort on-demand provisioning (mirrors the layer-2 resolver) so the runtime cell is
    // NON-VACUOUS on a networked linux-x64 host even before the section E gate wiring lands.
    private static string? TryProvisionCosign()
    {
        try
        {
            string script = TestPaths.RepoFile("gate", "tools", "provision-cosign.sh");
            if (!File.Exists(script)) return null;
            string dest = CachedCosign();
            var psi = new ProcessStartInfo("bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = TestPaths.RepoRoot(),
            };
            psi.ArgumentList.Add(script);
            psi.ArgumentList.Add("linux-x64");
            psi.ArgumentList.Add(dest);
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(120_000)) { try { p.Kill(true); } catch { } return null; }
            return p.ExitCode == 0 && File.Exists(dest) ? dest : null;
        }
        catch { return null; }
    }

    private static string? TryInitializeTrustedRoot(string cosignBin)
    {
        try
        {
            string sig = SigstoreRoot();
            if (File.Exists(sig)) return sig;
            var psi = new ProcessStartInfo(cosignBin)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("initialize");
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(120_000)) { try { p.Kill(true); } catch { } return null; }
            return File.Exists(sig) ? sig : null;
        }
        catch { return null; }
    }

    private static string? FirstReadable(params string?[] candidates)
        => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c));

    private static string FileSha256(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using FileStream fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }
}
