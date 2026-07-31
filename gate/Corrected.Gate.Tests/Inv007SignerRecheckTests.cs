using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-007 (~316-350, the re-check clause) — the extracted signer
/// gate/tools/sign-determinism.sh RE-VALIDATES the producer hand-off (digest / schema /
/// attested_commit / run_id / producing-job result / subject-manifest-at-attested_commit) and
/// REFUSES to sign on ANY mismatch, BEFORE any cosign invocation. Each mismatch class is an INDEPENDENT cell over a
/// fixture in which exactly ONE field is corrupted while all others stay valid — so a fail-open
/// where a single class goes unchecked is catchable (the corruptor for the unchecked field would
/// let the signer proceed to cosign, tripping the "cosign was NOT called" assert).
///
/// These are REAL out-of-process subprocess execs (bash + a fake cosign injected via COSIGN_BIN),
/// mirroring <see cref="CosignSubprocessSeamTests"/>. [Collection("Subprocess")] is REQUIRED
/// (concurrent fork/exec transiently returns LaunchFailed otherwise).
///
/// RED NOW: gate/tools/sign-determinism.sh does not exist. RequireSignerScript() fails first, so
/// every cell reads as "missing script", NOT a vacuous bash-127 masquerading as a refusal.
/// The POSITIVE CONTROL (a fully-valid fixture reaches the fake cosign) is what proves the
/// corruptor cells refuse for the RIGHT reason (the specific corruption), not because the fixture
/// is always-reject (AP-010).
///
/// DEFERRED (needs a real signed bundle / OIDC / Rekor): the actual signature, INV-007's live
/// permissions transcript, INV-009 bundle-content — all out of this track's scope. The fake
/// cosign NEVER produces a real signature or touches the network.
///
/// AP-031: NOT triggered — the fixtures are synthetic producer hand-offs the test constructs to
/// drive the re-check; they are not parsed output of another shipped Correctless tool at test time.
/// </summary>
[Collection("Subprocess")]
public class Inv007SignerRecheckTests
{
    // ------------------------------------------------------------------------------------------
    // POSITIVE CONTROL — a fully-valid hand-off REACHES cosign (proves the corruptors are honest).
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration]: with every producer-binding field valid, the signer passes its
    // re-check and INVOKES cosign (the fake's marker appears) and exits 0. Without this, an
    // "always refuse" script would make every corruptor cell below pass vacuously (AP-010).
    [Fact]
    public void Valid_handoff_passes_recheck_and_invokes_cosign_integration()
    {
        P3SignerHarness.RequireSignerScript();
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            P3SignerHarness.Artifacts art = P3SignerHarness.BuildArtifacts(dir);

            P3SignerHarness.RunResult r = RunSign(art, fake);

            Assert.True(fake.WasCalled(),
                "INV-007: a fully-valid producer hand-off must PASS the re-check and reach cosign.");
            Assert.Equal(0, r.ExitCode);
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    // ------------------------------------------------------------------------------------------
    // MISMATCH CLASS 1 — wrong artifact digest (declared receipt.sha256 != actual receipt bytes).
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] ("re-checks the producer artifacts' digest ... refusing on any
    // mismatch"): a tampered/wrong DECLARED artifact digest refuses before cosign.
    [Fact]
    public void Refuses_on_wrong_artifact_digest_integration()
    {
        AssertRefusesWithoutCosign(
            corruptDeclaredDigest: true,
            because: "wrong artifact digest (declared receipt.sha256 != actual)");
    }

    // ------------------------------------------------------------------------------------------
    // MISMATCH CLASS 1b — wrong schema (receipt.schema_version off the pinned RunReceipt contract).
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] ("re-checks the producer artifacts' digest / SCHEMA / ..."): a
    // receipt whose schema_version is not the pinned RunReceipt schema id refuses BEFORE cosign.
    // Independent of downstream INV-012 (out of this track) — the SIGNER itself gates the schema.
    [Fact]
    public void Refuses_on_wrong_schema_version_integration()
    {
        AssertRefusesWithoutCosign(
            mutator: r => r["schema_version"] = "off-contract/v0",
            because: "schema_version off the pinned RunReceipt contract");
    }

    // ------------------------------------------------------------------------------------------
    // MISMATCH CLASS 2 — wrong committed-commit / attested_commit (receipt != $GITHUB_SHA).
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] ("re-checks ... commit ... and the subject-manifest at
    // attested_commit"): a receipt whose attested_commit != the trusted trigger SHA refuses.
    [Fact]
    public void Refuses_on_wrong_attested_commit_integration()
    {
        AssertRefusesWithoutCosign(
            mutator: r => r["attested_commit"] = "0000000000000000000000000000000000000000",
            because: "attested_commit != GITHUB_SHA");
    }

    // ------------------------------------------------------------------------------------------
    // MISMATCH CLASS 3 — wrong run-id (receipt.run_id != $GITHUB_RUN_ID).
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] ("re-checks ... run-id"): a receipt whose recorded run_id is not
    // the current run refuses (a foreign/replayed producer artifact).
    [Fact]
    public void Refuses_on_wrong_run_id_integration()
    {
        AssertRefusesWithoutCosign(
            mutator: r => r["run_id"] = "99999999999",
            because: "run_id != GITHUB_RUN_ID");
    }

    // ------------------------------------------------------------------------------------------
    // MISMATCH CLASS 4 — wrong producing-job result (receipt.producing_job_result != success).
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] ("re-checks ... producing-job result"): a receipt whose
    // producing job did NOT succeed refuses (never sign the product of a failed producer job).
    [Fact]
    public void Refuses_on_non_success_producing_job_result_integration()
    {
        AssertRefusesWithoutCosign(
            mutator: r => r["producing_job_result"] = "failure",
            because: "producing_job_result != success");
    }

    // ------------------------------------------------------------------------------------------
    // MISMATCH CLASS 5 — subject-manifest-at-attested_commit mismatch.
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] ("... and the subject-manifest at attested_commit, refusing on
    // any mismatch"): a receipt whose recorded subject_manifest_digest does NOT match the manifest
    // checked out at attested_commit refuses (the signed subject must bind the real manifest —
    // INV-006/018).
    [Fact]
    public void Refuses_on_subject_manifest_digest_mismatch_integration()
    {
        AssertRefusesWithoutCosign(
            mutator: r => r["subject_manifest_digest"] = new string('b', 64),
            because: "subject_manifest_digest != manifest-at-attested_commit");
    }

    // ------------------------------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------------------------------

    private static P3SignerHarness.RunResult RunSign(
        P3SignerHarness.Artifacts art, P3SignerHarness.FakeCosign fake)
    {
        Dictionary<string, string?> env = P3SignerHarness.Env(art, fake);
        return P3SignerHarness.RunSigner(
            env,
            "--artifacts-dir", art.ArtifactsDir,
            "--manifest", art.ManifestFile,
            "--out", Path.Combine(art.ArtifactsDir, "..", "out.sigstore.json"));
    }

    private static void AssertRefusesWithoutCosign(
        string because,
        Action<Dictionary<string, object>>? mutator = null,
        bool corruptDeclaredDigest = false)
    {
        P3SignerHarness.RequireSignerScript();
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            P3SignerHarness.Artifacts art = P3SignerHarness.BuildArtifacts(
                dir, receiptMutator: mutator, corruptDeclaredDigest: corruptDeclaredDigest);

            P3SignerHarness.RunResult r = RunSign(art, fake);

            // Fail-closed: non-zero exit AND a "REFUSE" diagnostic AND cosign was NEVER invoked.
            Assert.False(fake.WasCalled(),
                $"INV-007: cosign was INVOKED despite {because} — the re-check must refuse BEFORE signing.");
            Assert.NotEqual(0, r.ExitCode);
            Assert.Contains("REFUSE", r.Combined, StringComparison.OrdinalIgnoreCase);
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }
}
