using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-008 (~352-362) — the signer records GITHUB_RUN_ATTEMPT and
/// REFUSES to sign unless it is exactly "1"; a re-run mints NOTHING and a new reviewed commit
/// (a fresh attempt-1 run) is required. This is the reversible / no-real-bundle slice: it drives
/// the extracted gate/tools/sign-determinism.sh with a fake cosign (injected via COSIGN_BIN) and
/// asserts the attempt GUARD gates the cosign call.
///
/// [Collection("Subprocess")] is REQUIRED (real fork/exec).
///
/// RED NOW: the script does not exist; RequireSignerScript() fails first (clean "missing script"),
/// never a vacuous bash-127. The attempt==1 POSITIVE cell (reaches the fake cosign) proves the
/// negative cells refuse for the attempt reason and not because the fixture is always-reject.
///
/// AP-031: NOT triggered — synthetic hand-off fixtures, not another shipped tool's parsed output.
/// </summary>
[Collection("Subprocess")]
public class Inv008SignerRunAttemptGuardTests
{
    // Tests INV-008 [integration] ("refuses to sign unless [GITHUB_RUN_ATTEMPT] is 1"): with a
    // valid hand-off and GITHUB_RUN_ATTEMPT=2, the signer REFUSES — non-zero, the exact RS-036
    // message ("re-runs never mint" / "push a new reviewed commit"), and cosign is NEVER invoked.
    [Fact]
    public void Attempt_2_refuses_with_exact_message_and_no_cosign_integration()
    {
        P3SignerHarness.RequireSignerScript();
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            // The receipt records attempt "2" AND the env presents GITHUB_RUN_ATTEMPT=2, so this is
            // a genuine rerun, not a producer/env disagreement.
            P3SignerHarness.Artifacts art = P3SignerHarness.BuildArtifacts(dir, attempt: "2");

            P3SignerHarness.RunResult r = RunSign(art, fake, attempt: "2");

            Assert.False(fake.WasCalled(),
                "INV-008: a rerun (GITHUB_RUN_ATTEMPT=2) must mint NOTHING — cosign was invoked.");
            Assert.NotEqual(0, r.ExitCode);
            // Exact RS-036 refusal wording: reruns never mint; a new reviewed commit is required.
            Assert.Contains("re-runs never mint", r.Combined, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("push a new reviewed commit", r.Combined, StringComparison.OrdinalIgnoreCase);
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    // Tests INV-008 [integration] ("... unless it is 1"): with GITHUB_RUN_ATTEMPT=1 and an
    // otherwise-valid hand-off, the signer PASSES the attempt guard and reaches the fake cosign.
    // This is the positive control that keeps the negatives honest (AP-010).
    [Fact]
    public void Attempt_1_passes_the_guard_and_reaches_cosign_integration()
    {
        P3SignerHarness.RequireSignerScript();
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            P3SignerHarness.Artifacts art = P3SignerHarness.BuildArtifacts(dir, attempt: "1");

            P3SignerHarness.RunResult r = RunSign(art, fake, attempt: "1");

            Assert.True(fake.WasCalled(),
                "INV-008: GITHUB_RUN_ATTEMPT=1 must PASS the attempt guard and reach the signing step.");
            Assert.Equal(0, r.ExitCode);
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    // Tests INV-008 [integration] ("the attempt is recorded"): the signer RECORDS the attempt
    // value it observed (echoed to its output) — an attempt that is never recorded is INV-008's
    // own Violated-when. On the valid attempt-1 path the observed attempt "1" appears in output.
    [Fact]
    public void Attempt_value_is_recorded_in_output_integration()
    {
        P3SignerHarness.RequireSignerScript();
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            P3SignerHarness.Artifacts art = P3SignerHarness.BuildArtifacts(dir, attempt: "1");

            P3SignerHarness.RunResult r = RunSign(art, fake, attempt: "1");

            // The recorded-attempt line must name the attempt token ATTRIBUTABLY — `run_attempt=1`
            // / `run_attempt: 1`. A bare Contains("1") is vacuous (run_id/SHA are full of 1s), so
            // require the attributable `run_attempt`-adjacent 1, not a stray digit anywhere.
            Assert.Matches(@"run_attempt[\s=:]+1\b", r.Combined);
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    // Tests INV-008 [integration] ("the signer records GITHUB_RUN_ATTEMPT ... refuses to sign
    // unless it is 1" — the receipt half): env GITHUB_RUN_ATTEMPT=1 but the producer receipt
    // records run_attempt=2 (an env↔receipt DISAGREEMENT) must refuse, no cosign. A GREEN that
    // checks ONLY the env var (not that the recorded receipt attempt also == 1) passes here — so
    // this cell has independent value. The run_id is shared (same-run carried-over receipt), so
    // INV-007's run_id check does NOT cover this.
    [Fact]
    public void Env_attempt_1_but_receipt_attempt_2_disagreement_refuses_integration()
    {
        P3SignerHarness.RequireSignerScript();
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            // receipt.run_attempt = "2" (via the fixture), but the ENV presents attempt = "1".
            // Every other field (run_id, attested_commit, digest, manifest) stays valid.
            P3SignerHarness.Artifacts art = P3SignerHarness.BuildArtifacts(dir, attempt: "2");

            P3SignerHarness.RunResult r = RunSign(art, fake, attempt: "1");

            Assert.False(fake.WasCalled(),
                "INV-008: env attempt=1 but receipt.run_attempt=2 disagreement must refuse — cosign was invoked.");
            Assert.NotEqual(0, r.ExitCode);
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    // Tests INV-008 [integration] (fail-closed defensive edge): a MISSING attempt env must NOT be
    // treated as 1 — an absent GITHUB_RUN_ATTEMPT fails closed (refuse, no cosign), never a silent
    // default-to-attempt-1. Defends the "the attempt is recorded / must be 1" contract against an
    // empty/unset environment.
    [Fact]
    public void Missing_attempt_env_fails_closed_not_treated_as_one_integration()
    {
        AssertAttemptEnvFailsClosed(attempt: null, label: "missing/unset GITHUB_RUN_ATTEMPT");
    }

    // Tests INV-008 [integration] (fail-closed defensive edge): an EMPTY attempt env likewise fails
    // closed — an empty string must not coerce to 1.
    [Fact]
    public void Empty_attempt_env_fails_closed_not_treated_as_one_integration()
    {
        AssertAttemptEnvFailsClosed(attempt: "", label: "empty GITHUB_RUN_ATTEMPT");
    }

    // ------------------------------------------------------------------------------------------

    private static void AssertAttemptEnvFailsClosed(string? attempt, string label)
    {
        P3SignerHarness.RequireSignerScript();
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            // The receipt itself is a valid attempt-1 producer artifact; only the ENV attempt is
            // absent/empty, isolating the "env attempt not treated as 1" defensive contract.
            P3SignerHarness.Artifacts art = P3SignerHarness.BuildArtifacts(dir, attempt: "1");

            P3SignerHarness.RunResult r = RunSign(art, fake, attempt: attempt);

            Assert.False(fake.WasCalled(),
                $"INV-008: {label} must fail closed — cosign must NOT be invoked (never default to attempt 1).");
            Assert.NotEqual(0, r.ExitCode);
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    private static P3SignerHarness.RunResult RunSign(
        P3SignerHarness.Artifacts art, P3SignerHarness.FakeCosign fake, string? attempt)
    {
        Dictionary<string, string?> env = P3SignerHarness.Env(art, fake, attempt: attempt);
        // A null attempt means REMOVE the env var (harness treats a null value as removal).
        if (attempt is null)
        {
            env["GITHUB_RUN_ATTEMPT"] = null;
        }
        return P3SignerHarness.RunSigner(
            env,
            "--artifacts-dir", art.ArtifactsDir,
            "--manifest", art.ManifestFile,
            "--out", Path.Combine(art.ArtifactsDir, "..", "out.sigstore.json"));
    }
}
