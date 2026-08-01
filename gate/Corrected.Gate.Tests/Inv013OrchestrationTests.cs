using System;
using System.Diagnostics;
using System.IO;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-013 LAYER 3 (~524) + INV-012: orchestration failure modes
/// through the injected COSIGN_BIN seam (a FAKE cosign) — absent bundle, missing binary, process
/// timeout, oversized output, a genuine cosign non-zero exit, and parse failure. Every failure maps
/// to a TYPED fail-closed result: a transient tool fault -> <c>unavailable</c>; everything else ->
/// <c>rejected</c> (the DEFAULT is rejected, RS-002 — a cosign crash/timeout/unknown fault NEVER
/// reads as unavailable, closing the RS-001 forged-ENTERED seam).
///
/// AP-002 (dead-code) guard: the timeout / oversize / non-zero-exit cells assert a PROOF-OF-INVOCATION
/// marker the fake writes as its FIRST action, so a GREEN that short-circuits every cell with
/// File.Exists pre-checks + a catch-all Rejected (never exec'ing CosignRunner) FAILS — the real
/// subprocess path must run. The timeout cell also asserts wall-clock elapsed >= the request timeout
/// (a non-exec GREEN returns instantly).
///
/// These are REAL out-of-process cells (GREEN execs the fake cosign), so the class carries
/// [Collection("Subprocess")] — concurrent fork/exec flakes otherwise. The fakes are BASH-BUILTIN
/// ONLY (no external command), so they run regardless of the verifier's env-allowlist policy.
///
/// Layer 2 (a genuine positive verify + decoded-payload byte-equality against a REAL committed
/// bundle) is T3b — OUT OF SCOPE here; this file exercises orchestration only.
///
/// DEFER TO T3b (audit finding 3 — needs T3b's trusted-root provisioning + the real Verify path):
///   * the SECOND transient fault — a chmod-000 (present-but-unreadable) trust root induced through
///     the real cosign subprocess -> Unavailable + TrustRootOrToolUnreadable (EA-009); and
///   * a swapped-digest / mismatched trust root -> Rejected + TrustRootOrPinMismatch (distinct from
///     the *unreadable* fault).
///   Both must be induced through the REAL Verify path (not synthetic layer-1 injection), so they
///   land with T3b's provisioning. Recorded here so the hand-off is not lost.
/// </summary>
[Collection("Subprocess")]
public class Inv013OrchestrationTests
{
    private static readonly string BashAbs = ResolveBashAbsolute();

    // A minimal, VALID sigstore-bundle-shaped JSON (payload "e30=" == base64 of "{}").
    private const string ValidBundleJson =
        "{\"mediaType\":\"application/vnd.dev.sigstore.bundle.v0.3+json\"," +
        "\"dsseEnvelope\":{\"payload\":\"e30=\",\"payloadType\":\"application/vnd.in-toto+json\"," +
        "\"signatures\":[]}}";

    private const string ValidTrustRootJson =
        "{\"mediaType\":\"application/vnd.dev.sigstore.trustedroot.v1+json\"}";

    // ---- fixtures ----

    private sealed class Fixture : IDisposable
    {
        internal required string Dir { get; init; }
        internal required string CosignBinPath { get; init; }
        internal required string BundlePath { get; init; }
        internal required string ReceiptPath { get; init; }
        internal required string TrustRootPath { get; init; }
        internal required string MarkerPath { get; init; }

        internal DeterminismVerifyRequest Request(TimeSpan? timeout = null) => new()
        {
            CosignBinPath = CosignBinPath,
            BundlePath = BundlePath,
            ReceiptPath = ReceiptPath,
            TrustRootPath = TrustRootPath,
            WorkingDirectory = Dir,
            ExpectedRid = "linux-x64",
            AttestedCommitAncestry = AncestryStatus.Ancestor,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };

        public void Dispose()
        {
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); }
            catch { /* OS temp cleanup is the backstop */ }
        }
    }

    /// <summary>
    /// Assemble a base fixture: a temp dir carrying the committed REAL receipt bytes, a valid
    /// bundle, a valid trust root, and a fake cosign whose body is built from the marker path by
    /// <paramref name="cosignBodyFactory"/> (default: a benign exit-0 fake that writes no marker).
    /// </summary>
    private static Fixture NewFixture(Func<string, string>? cosignBodyFactory = null)
    {
        string dir = Path.Combine(Path.GetTempPath(), "inv013-orch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        string receipt = Path.Combine(dir, "determinism-receipt.json");
        File.Copy(TestPaths.Fixture("provenance", "determinism-receipt.sample.json"), receipt);

        string bundle = Path.Combine(dir, "bundle.sigstore.json");
        File.WriteAllText(bundle, ValidBundleJson);

        string root = Path.Combine(dir, "trusted_root.json");
        File.WriteAllText(root, ValidTrustRootJson);

        string marker = Path.Combine(dir, "cosign-invoked.marker");
        string body = (cosignBodyFactory ?? (_ => BodyOk))(marker);
        string cosign = MakeFake(dir, "fake-cosign", body);

        return new Fixture
        {
            Dir = dir,
            CosignBinPath = cosign,
            BundlePath = bundle,
            ReceiptPath = receipt,
            TrustRootPath = root,
            MarkerPath = marker,
        };
    }

    // ---- cells ----

    // Tests INV-013 [integration] (layer 3, absent bundle): a missing bundle file is REJECTED
    // (evidence-absent or malformed-bundle) — never verified, never unavailable. RED: the deny stub
    // returns the P3NotYetActivated sentinel, matching neither expected reason.
    [Fact]
    public void Absent_bundle_file_is_rejected()
    {
        using Fixture fx = NewFixture();
        File.Delete(fx.BundlePath); // the bundle path now points at a missing file
        DeterminismVerifyResult r = DeterminismVerifier.Verify(fx.Request());

        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Contains(
            r.Reason,
            new DeterminismVerifyReason?[] { DeterminismVerifyReason.EvidenceAbsent, DeterminismVerifyReason.MalformedBundle });
    }

    // Tests INV-013 [integration] (layer 3, missing binary -> UNAVAILABLE): a cosign binary that is
    // absent yields the transient unavailable outcome with verifier-unavailable (EA-008) — the only
    // orchestration cell that is unavailable, not rejected. RED: the deny stub returns Rejected.
    [Fact]
    public void Missing_cosign_binary_is_unavailable()
    {
        using Fixture fx = NewFixture();
        File.Delete(fx.CosignBinPath); // an absolute path to a now-missing cosign binary
        DeterminismVerifyResult r = DeterminismVerifier.Verify(fx.Request());

        Assert.Equal(DeterminismVerifyOutcome.Unavailable, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.VerifierUnavailable, r.Reason);
    }

    // Tests INV-013 [integration] (layer 3, timeout -> fail-CLOSED, AP-002 real-routing): a cosign
    // that hangs past a SHORT (2s) request timeout is REJECTED with unclassified-verifier-fault —
    // NEVER unavailable (RS-002). The fake writes a marker as its FIRST action, so File.Exists(marker)
    // proves CosignRunner actually exec'd (a short-circuiting GREEN cannot create it); and the
    // wall-clock elapsed >= the timeout proves the real process-timeout path ran (a non-exec GREEN
    // returns instantly). RED: the deny stub returns the wrong reason, writes no marker, returns fast.
    [Fact]
    public void Hanging_cosign_times_out_and_is_rejected_fail_closed()
    {
        using Fixture fx = NewFixture(HangBody);
        var timeout = TimeSpan.FromSeconds(2);

        var sw = Stopwatch.StartNew();
        DeterminismVerifyResult r = DeterminismVerifier.Verify(fx.Request(timeout));
        sw.Stop();

        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.UnclassifiedVerifierFault, r.Reason);
        Assert.True(File.Exists(fx.MarkerPath), "AP-002: cosign was never invoked (no invocation marker).");
        Assert.True(
            sw.Elapsed >= TimeSpan.FromMilliseconds(1500),
            $"the real process-timeout path did not run: Verify returned in {sw.Elapsed} (< the 2s timeout).");
    }

    // Tests INV-013 [integration] (layer 3, oversized output -> fail-CLOSED, AP-002 real-routing): a
    // cosign that spews past the seam's output cap is REJECTED with unclassified-verifier-fault (a
    // DoS spew is an unclassified fault, fail-closed). The marker proves the fake actually ran. RED:
    // the deny stub returns the wrong reason and writes no marker.
    [Fact]
    public void Oversized_cosign_output_is_rejected_fail_closed()
    {
        using Fixture fx = NewFixture(SpewBody);
        DeterminismVerifyResult r = DeterminismVerifier.Verify(fx.Request());

        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.UnclassifiedVerifierFault, r.Reason);
        Assert.True(File.Exists(fx.MarkerPath), "AP-002: cosign was never invoked (no invocation marker).");
    }

    // Tests INV-013 [integration] (layer 3, genuine cosign non-zero exit -> fail-CLOSED, AP-002): a
    // cosign that runs to completion and EXITS 1 (valid bundle + receipt + binary present) is REJECTED
    // with unclassified-verifier-fault — a bare unknown non-zero exit the taxonomy does not positively
    // match maps to the pinned default (INV-012). The marker proves the real subprocess ran. RED: the
    // deny stub returns the P3NotYetActivated sentinel and writes no marker.
    [Fact]
    public void Cosign_nonzero_exit_is_rejected_fail_closed()
    {
        using Fixture fx = NewFixture(Exit1Body);
        DeterminismVerifyResult r = DeterminismVerifier.Verify(fx.Request());

        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.UnclassifiedVerifierFault, r.Reason);
        Assert.True(File.Exists(fx.MarkerPath), "AP-002: cosign was never invoked (no invocation marker).");
    }

    // Tests INV-013 [integration] (layer 3, malformed receipt): an unparseable receipt is REJECTED
    // with malformed-receipt. RED: the deny stub returns the P3NotYetActivated sentinel.
    [Fact]
    public void Malformed_receipt_is_rejected_as_malformed_receipt()
    {
        using Fixture fx = NewFixture();
        File.WriteAllText(fx.ReceiptPath, "not json{{{");
        DeterminismVerifyResult r = DeterminismVerifier.Verify(fx.Request());

        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.MalformedReceipt, r.Reason);
    }

    // Tests INV-013 [integration] (layer 3, malformed bundle): an unparseable bundle is REJECTED
    // with malformed-bundle. RED: the deny stub returns the P3NotYetActivated sentinel.
    [Fact]
    public void Malformed_bundle_is_rejected_as_malformed_bundle()
    {
        using Fixture fx = NewFixture();
        File.WriteAllText(fx.BundlePath, "]]not a bundle[[");
        DeterminismVerifyResult r = DeterminismVerifier.Verify(fx.Request());

        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.MalformedBundle, r.Reason);
    }

    // Tests INV-013 [integration] (fail-closed: any internal error -> false, INV-012 / item #1.4): a
    // non-absolute cosign path (the seam rejects a relative exe, CosignSubprocessSeamTests) must
    // NEVER yield Verified — a fail-closed guard that holds on the deny stub and forbids a
    // pass-through/verified on a malformed request.
    [Fact]
    public void A_non_absolute_cosign_path_never_verifies()
    {
        using Fixture fx = NewFixture();
        DeterminismVerifyRequest bad = new()
        {
            CosignBinPath = "cosign", // relative — the hardened seam rejects it
            BundlePath = fx.BundlePath,
            ReceiptPath = fx.ReceiptPath,
            TrustRootPath = fx.TrustRootPath,
            WorkingDirectory = fx.Dir,
            ExpectedRid = "linux-x64",
        };
        DeterminismVerifyResult r = DeterminismVerifier.Verify(bad);

        Assert.NotEqual(DeterminismVerifyOutcome.Verified, r.Outcome);
        Assert.False(r.Satisfied);
    }

    // ---- fake cosign bodies (BASH BUILTIN ONLY — no external command, no PATH needed) ----
    //
    // The marker write is the FIRST action so a cell can prove CosignRunner actually exec'd the fake.
    // The absolute marker path is baked in as a LITERAL so the recording survives the seam clearing
    // the child env before exec (mirrors P3SignerHarness.MakeFakeCosign).

    // Exit 0 with a small in-bounds line, no marker — a benign stand-in for the cells that fail
    // before/without reaching a genuine crypto verify. printf is a bash builtin.
    private const string BodyOk = """
        printf 'stub-cosign-ok\n'
        exit 0
        """;

    // Hang using ONLY the bash `read` builtin with a long per-iteration timeout — blocks well past
    // the test's 2s request timeout so the verifier's process-timeout path fires. No external cmd.
    private static string HangBody(string marker) =>
        "printf x > '" + marker + "'\n" +
        "while :; do read -t 300 _ 2>/dev/null || :; done\n";

    // Spew ~2.1 MiB to stdout using ONLY builtins (brace expansion + printf), past the seam's
    // default output cap, so the verifier observes an oversize-output fault. No external cmd.
    private static string SpewBody(string marker) =>
        "printf x > '" + marker + "'\n" +
        "chunk=$(printf 'A%.0s' {1..1000})\n" +
        "i=0\n" +
        "while [ \"$i\" -lt 2100 ]; do\n" +
        "  printf '%s' \"$chunk\"\n" +
        "  i=$((i + 1))\n" +
        "done\n";

    // Run to completion and exit 1 — a genuine cosign non-zero exit routed through the real seam.
    private static string Exit1Body(string marker) =>
        "printf x > '" + marker + "'\n" +
        "exit 1\n";

    private static string MakeFake(string dir, string name, string body)
    {
        string path = Path.Combine(dir, name);
        string script = "#!" + BashAbs + "\n" + body.Replace("\r\n", "\n").TrimStart('\n');
        if (!script.EndsWith("\n", StringComparison.Ordinal))
        {
            script += "\n";
        }
        File.WriteAllText(path, script);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return path;
    }

    private static string ResolveBashAbsolute()
    {
        foreach (string c in new[] { "/bin/bash", "/usr/bin/bash", "/usr/local/bin/bash" })
        {
            if (File.Exists(c))
            {
                return c;
            }
        }
        throw new FileNotFoundException("bash not found at a known absolute path — orchestration fakes cannot run.");
    }
}
