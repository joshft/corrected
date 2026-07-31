using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-009 (~364-398) + DD-002 (~1461) — the signer signs with the
/// TRANSCRIPT-FROZEN cosign argv `attest-blob --statement &lt;stmt&gt; --bundle &lt;out&gt;
/// --new-bundle-format=true --yes &lt;blob&gt;`, the cosign version/digest single-sourced from
/// gate/tools/cosign-pin.json, and the `--new-bundle-format=false` contingency NOT taken (RS-008
/// resolved 2026-07-30).
///
/// Two cell families:
///   * BEHAVIORAL argv — drive the happy path with a fake cosign that RECORDS its argv (injected
///     via COSIGN_BIN), then assert the recorded argv is exactly the frozen form. [Subprocess].
///   * STATIC single-sourcing / landing — scan the committed signer source: it reads the version
///     from cosign-pin.json (never a divergent in-script literal) and uses
///     --new-bundle-format=true, never =false.
///
/// [Collection("Subprocess")] is REQUIRED (the behavioral cells exec the script).
///
/// RED NOW: gate/tools/sign-determinism.sh does not exist — RequireSignerScript() fails first
/// (clean "missing script"); the static scans read the absent file via an existence assert.
///
/// DEFERRED (needs a real signed bundle): INV-009 real bundle-content assertions, the tlog /
/// signed-timestamp presence in a genuine bundle, and the from-clean offline-verify-after-expiry
/// meta-test — all out of this track's scope. The fake cosign records argv only; it produces no
/// real signature and touches no network.
///
/// AP-031: NOT triggered — the version cross-check reads the committed cosign-pin.json (a config
/// artifact this feature authors), and the recorded argv is the fake's own capture, not another
/// shipped tool's output.
/// </summary>
[Collection("Subprocess")]
public class Inv009SignerArgvPinTests
{
    // ------------------------------------------------------------------------------------------
    // BEHAVIORAL — the recorded cosign argv is exactly the transcript-frozen form.
    // ------------------------------------------------------------------------------------------

    // Tests INV-009 [integration] ("the exact single-version pinned `cosign attest-blob
    // --statement` ... frozen signing argv"): on the happy path the signer invokes cosign with the
    // frozen argv — verb `attest-blob` first, then --statement, --bundle, --new-bundle-format=true,
    // --yes, and a trailing positional blob, in that order. The `--new-bundle-format=false`
    // contingency is NOT present (DD-002 / RS-008 resolved).
    [Fact]
    public void Recorded_cosign_argv_is_the_transcript_frozen_attest_blob_form_integration()
    {
        P3SignerHarness.RequireSignerScript();
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            P3SignerHarness.Artifacts art = P3SignerHarness.BuildArtifacts(dir);

            P3SignerHarness.RunResult r = P3SignerHarness.RunSigner(
                P3SignerHarness.Env(art, fake),
                "--artifacts-dir", art.ArtifactsDir,
                "--manifest", art.ManifestFile,
                "--out", Path.Combine(dir, "out.sigstore.json"));

            Assert.True(fake.WasCalled(),
                "INV-009: the happy path must reach cosign so its argv can be checked.");
            Assert.Equal(0, r.ExitCode);

            string[] argv = fake.RecordedArgv();
            Assert.NotEmpty(argv);

            // Frozen verb + flags present.
            Assert.Equal("attest-blob", argv[0]); // the verb is first
            Assert.Contains("--statement", argv);
            Assert.Contains("--bundle", argv);
            Assert.Contains("--new-bundle-format=true", argv);
            Assert.Contains("--yes", argv);

            // The `false` contingency is NOT taken (DD-002 / RS-008), and no floating/ranged token.
            Assert.DoesNotContain("--new-bundle-format=false", argv);
            foreach (string a in argv)
            {
                Assert.False(a.Contains("latest", StringComparison.OrdinalIgnoreCase),
                    $"INV-009: cosign argv carries a floating token '{a}'.");
                // The cosign VERSION is selected by which binary runs, NOT an argv element — so no
                // vX.Y.Z semver may appear in the argv.
                Assert.False(Regex.IsMatch(a, @"^v?\d+\.\d+\.\d+$"),
                    $"INV-009: a version literal '{a}' leaked into the cosign argv (version is the binary, not argv).");
            }

            // Frozen ORDER: attest-blob < --statement < --bundle < --new-bundle-format=true < --yes.
            int iVerb = Array.IndexOf(argv, "attest-blob");
            int iStmt = Array.IndexOf(argv, "--statement");
            int iBundle = Array.IndexOf(argv, "--bundle");
            int iFmt = Array.IndexOf(argv, "--new-bundle-format=true");
            int iYes = Array.IndexOf(argv, "--yes");
            Assert.True(iVerb < iStmt && iStmt < iBundle && iBundle < iFmt && iFmt < iYes,
                "INV-009: the cosign argv must be in the transcript-frozen order " +
                "(attest-blob, --statement, --bundle, --new-bundle-format=true, --yes).");

            // --statement is a value flag: the token immediately after it is a path, not another flag.
            Assert.True(iStmt + 1 < argv.Length && !argv[iStmt + 1].StartsWith("--", StringComparison.Ordinal),
                "INV-009: --statement must be followed by a statement path.");
            // A trailing positional blob follows --yes (the receipt bytes being attested).
            Assert.True(iYes + 1 < argv.Length && !argv[iYes + 1].StartsWith("--", StringComparison.Ordinal),
                "INV-009: a trailing positional blob must follow --yes.");
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    // ------------------------------------------------------------------------------------------
    // STATIC — cosign version/digest single-sourced from cosign-pin.json; no divergent literal.
    // ------------------------------------------------------------------------------------------

    // Tests INV-009 [integration] ("the exact single-version pinned cosign ... single-sourced"):
    // the signer source READS the pin from gate/tools/cosign-pin.json (the single source of truth),
    // and does NOT embed a divergent in-script cosign version literal — any vX.Y.Z that DOES appear
    // must equal the pinned version. Cross-checks against the committed pin file, not a 2nd copy.
    [Fact]
    public void Signer_version_is_single_sourced_from_the_pin_no_divergent_literal()
    {
        Assert.True(File.Exists(P3SignerHarness.SignerScriptAbsPath()),
            "INV-009: gate/tools/sign-determinism.sh must exist (GREEN deliverable).");
        string src = File.ReadAllText(P3SignerHarness.SignerScriptAbsPath());
        string pinned = P3SignerHarness.PinnedCosignVersion();
        string pinnedCore = pinned.TrimStart('v', 'V');

        // The pin must be READ in a command position (a non-comment line), not merely mentioned in
        // a comment — else "single-sourced" is a doc claim the script does not actually honor.
        bool readsPin = src.Split('\n')
            .Any(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal)
                      && l.Contains("cosign-pin.json", StringComparison.Ordinal));
        Assert.True(readsPin,
            "INV-009: the signer must READ cosign-pin.json in a command position (not only a comment).");

        // Any version literal in the script (v-prefixed OR bare, e.g. `3.0.1`) must equal the
        // pinned version core — no divergent literal weakens the single-source pin. A clean,
        // fully single-sourced script has ZERO version literals (the loop then runs zero times).
        foreach (Match m in Regex.Matches(src, @"v?\d+\.\d+\.\d+"))
        {
            Assert.Equal(pinnedCore, m.Value.TrimStart('v', 'V'));
        }
    }

    // Tests INV-009 [integration] (DD-002 / RS-008 resolved — "new-format-offline; the
    // --new-bundle-format=false contingency was NOT taken"): the signer source uses
    // --new-bundle-format=true and NEVER --new-bundle-format=false. This is the static landing
    // check recording the transcript-spike decision.
    [Fact]
    public void Signer_source_uses_new_bundle_format_true_not_false()
    {
        Assert.True(File.Exists(P3SignerHarness.SignerScriptAbsPath()),
            "INV-009: gate/tools/sign-determinism.sh must exist (GREEN deliverable).");
        string src = File.ReadAllText(P3SignerHarness.SignerScriptAbsPath());

        Assert.Contains("--new-bundle-format=true", src);
        Assert.DoesNotContain("--new-bundle-format=false", src);
    }

    // Tests INV-009 [integration] ("cosign attest-blob --statement" is the frozen invocation): the
    // signer source invokes cosign via `attest-blob --statement` (Corrected owns Statement
    // semantics), not `sign-blob` or a bare `attest`. Source-scan companion to the behavioral cell.
    [Fact]
    public void Signer_source_uses_attest_blob_statement_invocation()
    {
        Assert.True(File.Exists(P3SignerHarness.SignerScriptAbsPath()),
            "INV-009: gate/tools/sign-determinism.sh must exist (GREEN deliverable).");
        string src = File.ReadAllText(P3SignerHarness.SignerScriptAbsPath());

        Assert.Contains("attest-blob", src);
        Assert.Contains("--statement", src);
    }
}
