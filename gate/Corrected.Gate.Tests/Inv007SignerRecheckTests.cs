using System;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-007 (~316-350, the re-check clause) — the extracted signer
/// gate/tools/sign-determinism.sh RE-VALIDATES the same-run producer hand-off and REFUSES to sign
/// on ANY mismatch, BEFORE any cosign invocation. After the Statement-builder reconciliation the
/// hand-off is 5 files and the re-check spans SEVEN independent classes:
///   (1) digest           receipt.sha256 != actual SHA-256(determinism-receipt.json)
///   (2) schema           the receipt does not parse as a RunReceipt (missing policy_version)
///   (3) attested_commit  receipt.attested_commit != $GITHUB_SHA
///   (4) run_id           ci-context.run_id != $GITHUB_RUN_ID          [now in ci-context.json]
///   (5) producing-job    ci-context.producing_job_result != "success" [now in ci-context.json]
///   (6) manifest         receipt.subject_manifest_digest != SHA-256(&lt;MANIFEST_FILE&gt;)
///   (7) statement        determinism-statement.json ABSENT / wrong subject-digest / wrong
///                        predicate-type / wrong subject-name (the NEW Statement-binding class —
///                        the signer signs the CORRECTED-BUILT Statement and fails closed on it)
/// Each class is an INDEPENDENT cell over a fixture in which exactly ONE field deviates while all
/// others stay valid — so a fail-open where a single class goes unchecked is catchable (the
/// corruptor for the unchecked field would let the signer proceed to cosign, tripping the "cosign
/// was NOT called" assert).
///
/// These are REAL out-of-process subprocess execs (bash + a fake cosign injected via COSIGN_BIN),
/// mirroring <see cref="CosignSubprocessSeamTests"/>. [Collection("Subprocess")] is REQUIRED
/// (concurrent fork/exec transiently returns LaunchFailed otherwise).
///
/// RED NOW: the CURRENT placeholder signer reads run_id/run_attempt from the RECEIPT (now absent,
/// re-homed to ci-context.json) and BUILDS its own Statement — so the POSITIVE CONTROL cannot reach
/// cosign (it refuses on the missing receipt run_id) and the class-7 statement cells are unmet.
/// The POSITIVE CONTROL failing is the RED signal; it is also what keeps the corruptor cells honest
/// at GREEN (they must refuse for the SPECIFIC corruption, not because the fixture is always-reject
/// — AP-010).
///
/// DEFERRED (needs a real signed bundle / OIDC / Rekor): the actual signature, INV-007's live
/// permissions transcript, INV-009 bundle-content — all out of this track's scope. The fake cosign
/// NEVER produces a real signature or touches the network.
///
/// AP-031: NOT triggered — the fixtures are synthetic producer hand-offs the test constructs to
/// drive the re-check (the receipt SUBJECT is seeded from the committed real PR1 fixture, but no
/// shipped Correctless tool's output is parsed at test time).
/// </summary>
[Collection("Subprocess")]
public class Inv007SignerRecheckTests
{
    // ------------------------------------------------------------------------------------------
    // POSITIVE CONTROL — a fully-valid hand-off REACHES cosign (proves the corruptors are honest).
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration]: with every producer-binding field valid (receipt + ci-context +
    // Corrected-built statement all consistent), the signer passes its re-check and INVOKES cosign
    // (the fake's marker appears) and exits 0. Without this, an "always refuse" script would make
    // every corruptor cell below pass vacuously (AP-010). RED: the current signer builds its own
    // statement and reads run_id from the receipt (absent) -> refuses -> cosign not reached.
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
    // CLASS 1 — wrong artifact digest (declared receipt.sha256 != actual receipt bytes).
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
    // CLASS 2 — wrong schema (the receipt does not parse as a RunReceipt: a required field is gone).
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] ("re-checks the producer artifacts' digest / SCHEMA / ..."): a
    // receipt that does not parse as a determinism RunReceipt (here a MISSING policy_version — a
    // required RunReceipt field, isolated from the manifest-digest class which does not read it)
    // refuses BEFORE cosign. Independent of downstream INV-012 — the SIGNER itself gates the shape.
    [Fact]
    public void Refuses_on_receipt_that_is_not_a_valid_runreceipt_integration()
    {
        AssertRefusesWithoutCosign(
            receiptMutator: r => r.Remove("policy_version"),
            because: "receipt does not parse as a RunReceipt (missing policy_version)");
    }

    // ------------------------------------------------------------------------------------------
    // CLASS 3 — wrong committed-commit / attested_commit (receipt != $GITHUB_SHA).
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] ("re-checks ... commit ... and the subject-manifest at
    // attested_commit"): a receipt whose attested_commit != the trusted trigger SHA refuses.
    [Fact]
    public void Refuses_on_wrong_attested_commit_integration()
    {
        AssertRefusesWithoutCosign(
            receiptMutator: r => r["attested_commit"] = "0000000000000000000000000000000000000000",
            because: "attested_commit != GITHUB_SHA");
    }

    // ------------------------------------------------------------------------------------------
    // CLASS 4 — wrong run-id (ci-context.run_id != $GITHUB_RUN_ID).
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] ("re-checks ... run-id"): a ci-context whose recorded run_id is
    // not the current run refuses (a foreign/replayed producer artifact). run_id is CI-run metadata
    // and now lives in ci-context.json, NOT the RunReceipt subject.
    [Fact]
    public void Refuses_on_wrong_run_id_integration()
    {
        AssertRefusesWithoutCosign(
            ciContextMutator: c => c["run_id"] = "99999999999",
            because: "ci-context.run_id != GITHUB_RUN_ID");
    }

    // ------------------------------------------------------------------------------------------
    // CLASS 5 — wrong producing-job result (ci-context.producing_job_result != success).
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] ("re-checks ... producing-job result"): a ci-context whose
    // producing job did NOT succeed refuses (never sign the product of a failed producer job).
    [Fact]
    public void Refuses_on_non_success_producing_job_result_integration()
    {
        AssertRefusesWithoutCosign(
            ciContextMutator: c => c["producing_job_result"] = "failure",
            because: "ci-context.producing_job_result != success");
    }

    // ------------------------------------------------------------------------------------------
    // CLASS 6 — subject-manifest-at-attested_commit mismatch.
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] ("... and the subject-manifest at attested_commit, refusing on
    // any mismatch"): a receipt whose recorded subject_manifest_digest does NOT match the manifest
    // checked out at attested_commit refuses (the signed subject must bind the real manifest —
    // INV-006/018).
    [Fact]
    public void Refuses_on_subject_manifest_digest_mismatch_integration()
    {
        AssertRefusesWithoutCosign(
            receiptMutator: r => r["subject_manifest_digest"] = new string('b', 64),
            because: "subject_manifest_digest != manifest-at-attested_commit");
    }

    // ------------------------------------------------------------------------------------------
    // CLASS 7 — the Corrected-built Statement must bind the subject (absent / tampered).
    // Each sub-cell deviates EXACTLY ONE bound field; the signer signs the Statement and must
    // fail closed on any deviation — it NEVER builds its own Statement.
    // ------------------------------------------------------------------------------------------

    // Tests INV-007 [integration] (class-7 "statement absent"): if determinism-statement.json is
    // MISSING, the signer must fail closed (never synthesize its own Statement and sign it).
    [Fact]
    public void Refuses_when_corrected_statement_is_absent_integration()
    {
        AssertRefusesWithoutCosign(
            statementTransform: P3SignerHarness.DeleteStatement,
            because: "determinism-statement.json is absent (signer must not build its own)");
    }

    // Tests INV-007 [integration] (class-7 "statement subject-digest mismatch"): a Statement whose
    // subject sha256 does NOT equal SHA-256(receipt bytes) refuses — the signed Statement must bind
    // the exact receipt subject (INV-006/010 byte-equality precondition).
    [Fact]
    public void Refuses_when_statement_subject_digest_does_not_bind_receipt_integration()
    {
        AssertRefusesWithoutCosign(
            statementTransform: P3SignerHarness.StatementWithWrongSubjectDigest,
            because: "statement subject sha256 != SHA-256(receipt bytes)");
    }

    // Tests INV-007 [integration] (class-7 "wrong predicate-type"): a Statement whose predicateType
    // is off the frozen Corrected determinism URI refuses (a mis-typed predicate is not the
    // Corrected-built Statement — CVE-2026-39395 predicate-type binding, DD-002).
    [Fact]
    public void Refuses_when_statement_predicate_type_is_not_frozen_uri_integration()
    {
        AssertRefusesWithoutCosign(
            statementTransform: P3SignerHarness.StatementWithWrongPredicateType,
            because: "statement predicateType != the frozen determinism URI");
    }

    // Tests INV-007 [integration] (class-7 "wrong subject-name"): a Statement whose subject name is
    // the OLD placeholder ("determinism-receipt.json") rather than the canonical
    // "determinism-run-receipt" refuses — this is exactly the drift the placeholder signer minted.
    [Fact]
    public void Refuses_when_statement_subject_name_is_not_canonical_integration()
    {
        AssertRefusesWithoutCosign(
            statementTransform: P3SignerHarness.StatementWithWrongSubjectName,
            because: "statement subject name != determinism-run-receipt");
    }

    // ------------------------------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------------------------------

    private static P3SignerHarness.RunResult RunSign(
        P3SignerHarness.Artifacts art, P3SignerHarness.FakeCosign fake)
    {
        var env = P3SignerHarness.Env(art, fake);
        return P3SignerHarness.RunSigner(
            env,
            "--artifacts-dir", art.ArtifactsDir,
            "--manifest", art.ManifestFile,
            "--out", Path.Combine(art.ArtifactsDir, "..", "out.sigstore.json"));
    }

    private static void AssertRefusesWithoutCosign(
        string because,
        Action<JsonObject>? receiptMutator = null,
        Action<JsonObject>? ciContextMutator = null,
        Func<string, string?>? statementTransform = null,
        bool corruptDeclaredDigest = false)
    {
        P3SignerHarness.RequireSignerScript();
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            P3SignerHarness.Artifacts art = P3SignerHarness.BuildArtifacts(
                dir,
                receiptMutator: receiptMutator,
                ciContextMutator: ciContextMutator,
                statementTransform: statementTransform,
                corruptDeclaredDigest: corruptDeclaredDigest);

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
