using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Corrected.Gate;
using Corrected.Provenance.Determinism;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-013 LAYER 2 (~509-544) — the REAL pinned-cosign integration
/// against the COMMITTED fixture bundles (<c>test/attestations/fixtures/{pos,shaneg}/**</c>). This
/// is T3b: it drives the full <see cref="DeterminismVerifier.Verify"/> real-cosign path (NOT a
/// stub/always-pass double — AP-012) and asserts INV-010 (the SIGNED DSSE payload byte-equals the
/// Corrected-reconstructed Statement), INV-011 (cert workflow-SHA cross-checked to the receipt's
/// <c>attested_commit</c>), and the reason-SPECIFIC crypto/policy negatives.
///
/// LOCATING THE PROVISIONED COSIGN (the env seam — the design DECISION, mirroring the signer's
/// COSIGN_BIN seam in <see cref="P3SignerHarness"/>): the documented gate command
/// <c>gate/run-readiness-gate.sh</c> (the project's <c>commands.test</c>) runs the online
/// provisioning pre-step (<c>provision-cosign.sh</c>, INV-017/EA-008) and EXPORTS
/// <c>COSIGN_BIN</c> + <c>TRUSTED_ROOT</c> before the offline verify (section E / RS-014). These
/// tests read those two env vars; a well-known provisioned-cache fallback + a best-effort
/// on-demand provision keep the REAL path live on a linux-x64 host with network even before the
/// GREEN wiring lands, so the RED signal is genuine (not a phantom).
///
/// RS-015 / AP-013 — NEVER A SILENT SKIP: when cosign is genuinely unavailable (air-gapped) or the
/// host is off-RID, each cell records a TYPED reason via the real Verify path (verifier-unavailable
/// / a non-Verified typed reject) — it does NOT [Fact(Skip)] or return vacuously. The POSITIVE
/// cells require a real verify, so <c>commands.test</c> provisions cosign; the section E from-clean
/// assertion is the forcing function that a genuine cosign subprocess actually executed.
///
/// [Collection("Subprocess")] — these fork/exec real cosign (serialize with the other subprocess
/// classes to avoid EAGAIN/ENOMEM spawn flakes).
/// </summary>
[Collection("Subprocess")]
public class Inv010Inv011Layer2RealCosignTests
{
    // ---- committed fixture identity constants (README-frozen; single source of truth is
    //      DeterminismVerifyIdentity — pinned again here so a drift is a reviewable diff) ----
    private const string FixtureAttestedCommit = "14701a99367f76b3e46b7261afc1f5c3dd490244";
    private const string ShanegAttestedCommit = "0000000000000000000000000000000000000000";

    // ================= section A / B — the real Verify cells =================

    // Tests INV-010/INV-011 [integration] (LAYER 2 POSITIVE): the genuine POS bundle, driven through
    // the full real-cosign Verify under the FIXTURE identity, VERIFIES; AND (INV-010) the decoded
    // .dsseEnvelope.payload byte-equals SerializeStatementJson(receiptBytes, RunReceipt.FromJson);
    // AND (INV-011) the cert workflow-SHA equals the receipt's attested_commit. RED: the T3a
    // cosign-Ok branch is a fail-closed placeholder (returns UnclassifiedVerifierFault) AND the stub
    // argv omits the identity flags, so Verify never returns Verified.
    [Fact]
    public void Pos_fixture_verifies_and_byte_equal_payload_and_cert_sha_positive()
    {
        // INV-010 byte-equality is a fixture/reconstruction contract (passes now — it validates the
        // committed fixture is honest); the RED DRIVER is the Verified assertion below.
        byte[] posReceiptBytes = File.ReadAllBytes(FixtureFile("pos", "determinism-receipt.json"));
        byte[] decodedPayload = DecodeDssePayload(FixtureFile("pos", "determinism.sigstore.json"));
        string reconstruction =
            DeterminismAttestation.SerializeStatementJson(posReceiptBytes, RunReceipt.FromJson(posReceiptBytes));
        Assert.Equal(reconstruction, Encoding.UTF8.GetString(decodedPayload)); // INV-010

        // INV-011 positive: the fixture cert workflow-SHA equals the POS receipt's attested_commit.
        Assert.Equal(FixtureAttestedCommit, RunReceipt.FromJson(posReceiptBytes).AttestedCommit);
        Assert.Equal(DeterminismVerifyIdentity.FixtureCertWorkflowSha, FixtureAttestedCommit);

        var rc = RealCosign.Resolve();
        using var fx = FixtureCopy.Of("pos");
        DeterminismVerifyRequest req = rc.FixtureRequest(fx, DeterminismVerifyIdentity.Fixture, FixtureAttestedCommit);

        DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

        if (!rc.Provisioned)
        {
            AssertHonestUnavailableFallback(r);
            return;
        }
        Assert.Equal(DeterminismVerifyOutcome.Verified, r.Outcome); // RED driver
        Assert.True(r.Satisfied);
        Assert.Null(r.Reason);
    }

    // Tests INV-013 [integration] (2a — the identity constant is READ + value-specific): the genuine
    // POS bundle driven through the exact PRODUCTION --certificate-identity is REJECTED with
    // identity-mismatch SPECIFICALLY (not a generic reject) — proving the production identity
    // constant is read and value-specific (an always-reject / typo'd / default-accept production
    // verifier cannot pass this). The production ACCEPT branch stays a recorded PR3 residual.
    // RED: the T3a NonZeroExit branch returns UnclassifiedVerifierFault (no stderr classification).
    [Fact]
    public void Pos_through_production_identity_rejects_identity_mismatch()
    {
        var rc = RealCosign.Resolve();
        using var fx = FixtureCopy.Of("pos");
        DeterminismVerifyRequest req = rc.FixtureRequest(fx, DeterminismVerifyIdentity.Production, FixtureAttestedCommit);

        DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.IdentityMismatch, r.Reason); // reason-specific (RED)
    }

    // Tests INV-011 [integration] (2b — the SHA cross-check, reached only once identity has passed):
    // the SHANEG bundle (genuine crypto, attested_commit 0000… != cert workflow-SHA 14701a9) driven
    // through the fixture-ACCEPTING argv (fixture identity + the fixture's frozen workflow-SHA so
    // cosign accepts) is REJECTED attributable SPECIFICALLY to the Corrected-side cert-SHA ↔
    // attested_commit cross-check (CertWorkflowShaMismatch) — DISTINCT from identity-mismatch.
    // DECISION: the T3a reason enum had no cert-SHA cross-check token (only identity-mismatch and the
    // INV-012/019 ancestry reasons, which are different checks); INV-011's Enforcement clause names a
    // Corrected-side "probe assertion cross-checking cert-SHA == receipt.attested_commit" that MUST
    // be reason-specific and distinct from identity (RS-006). T3b adds the CertWorkflowShaMismatch
    // reason (structural contract; the cross-check LOGIC is GREEN's). RED: the placeholder cosign-Ok
    // branch returns UnclassifiedVerifierFault.
    [Fact]
    public void Shaneg_through_fixture_accepting_argv_rejects_cert_sha_cross_check()
    {
        // Precondition (AP-010): the fixture genuinely embeds the mismatch the cross-check must catch.
        byte[] shanegReceipt = File.ReadAllBytes(FixtureFile("shaneg", "determinism-receipt.json"));
        Assert.Equal(ShanegAttestedCommit, RunReceipt.FromJson(shanegReceipt).AttestedCommit);
        Assert.NotEqual(DeterminismVerifyIdentity.FixtureCertWorkflowSha, ShanegAttestedCommit);

        var rc = RealCosign.Resolve();
        using var fx = FixtureCopy.Of("shaneg");
        // fixture-ACCEPTING: fixture identity + the fixture's frozen workflow-SHA (14701a9) so cosign
        // accepts SHANEG's genuine crypto; Corrected then cross-checks 14701a9 != attested_commit(0000).
        DeterminismVerifyRequest req = rc.FixtureRequest(fx, DeterminismVerifyIdentity.Fixture, FixtureAttestedCommit);

        DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.CertWorkflowShaMismatch, r.Reason); // reason-specific (RED)
    }

    // Tests INV-013 [integration] (crypto negative — tampered DSSE payload → signature-invalid): a
    // POS bundle with ONE flipped base64 payload byte (INV-013: mutating a signed bundle tests CRYPTO
    // rejection, never policy) is REJECTED with signature-invalid. cosign reports the tlog/envelope
    // payload-hash mismatch. RED: the T3a NonZeroExit branch returns UnclassifiedVerifierFault.
    [Fact]
    public void Tampered_dsse_payload_rejects_signature_invalid()
    {
        var rc = RealCosign.Resolve();
        using var fx = FixtureCopy.Of("pos", tamperBundle: TamperFlipPayloadByte);
        DeterminismVerifyRequest req = rc.FixtureRequest(fx, DeterminismVerifyIdentity.Fixture, FixtureAttestedCommit);

        DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.SignatureInvalid, r.Reason);
    }

    // Tests INV-013 [integration] (crypto negative — wrong predicate-type ARGV → predicate-type-
    // mismatch): the intact POS bundle driven with a WRONG --type argv (the bundle is NOT mutated —
    // the argv's predicate type differs from the signed payload's) is REJECTED with
    // predicate-type-mismatch. cosign reports "invalid predicate type, expected … got …". RED: the
    // T3a stub argv omits --type entirely and the NonZeroExit branch returns UnclassifiedVerifierFault.
    [Fact]
    public void Wrong_predicate_type_argv_rejects_predicate_type_mismatch()
    {
        var rc = RealCosign.Resolve();
        using var fx = FixtureCopy.Of("pos");
        DeterminismVerifyIdentity wrongType =
            DeterminismVerifyIdentity.Fixture with { PredicateType = "https://correctless.org/attestations/WRONG/v9" };
        DeterminismVerifyRequest req = rc.FixtureRequest(fx, wrongType, FixtureAttestedCommit);

        DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.PredicateTypeMismatch, r.Reason);
    }

    // Tests INV-010/INV-013 [integration] (crypto negative — receipt blob whose sha256 != the bundle
    // subject digest → subject-digest-mismatch): the receipt blob is byte-mutated (a trailing space
    // appended) so sha256(blob) no longer equals the signed subject digest; cosign reports "provided
    // artifact digests do not match digests in statement". This is the INV-010 decoded-payload ≠
    // reconstruction branch realized through the subject-digest binding (section B). RED: the T3a
    // NonZeroExit branch returns UnclassifiedVerifierFault.
    [Fact]
    public void Receipt_blob_sha_not_matching_subject_digest_rejects_subject_digest_mismatch()
    {
        var rc = RealCosign.Resolve();
        using var fx = FixtureCopy.Of("pos", tamperReceipt: bytes => bytes.Concat(new byte[] { (byte)' ' }).ToArray());
        DeterminismVerifyRequest req = rc.FixtureRequest(fx, DeterminismVerifyIdentity.Fixture, FixtureAttestedCommit);

        DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.SubjectDigestMismatch, r.Reason);
    }

    // Tests INV-013 [integration] (crypto negative — structurally malformed bundle → malformed-
    // bundle): a JSON-VALID but not-a-sigstore-bundle input ({"foo":"bar"}) passes Corrected's
    // pre-cosign JSON parse and reaches cosign, which rejects it as a bundle-format failure. GREEN
    // maps the format error to malformed-bundle (distinct from the pre-cosign invalid-JSON path the
    // orchestration suite already covers). INV-013: a malformed bundle is a crypto/structure reject,
    // never mutated to test a POLICY row. RED: the T3a NonZeroExit branch returns
    // UnclassifiedVerifierFault.
    [Fact]
    public void Json_valid_non_bundle_rejects_malformed_bundle()
    {
        var rc = RealCosign.Resolve();
        using var fx = FixtureCopy.Of("pos", replaceBundle: "{\"foo\":\"bar\"}");
        DeterminismVerifyRequest req = rc.FixtureRequest(fx, DeterminismVerifyIdentity.Fixture, FixtureAttestedCommit);

        DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.MalformedBundle, r.Reason);
    }

    // Tests INV-010 [integration] (B2 - the byte-equality lives INSIDE Verify, not fixture honesty):
    // a FAKE cosign that exits 0 (the COSIGN_BIN seam) + a crafted bundle whose decoded
    // .dsseEnvelope.payload is a Statement S' that KEEPS the matching subject digest (sha256(receipt))
    // but MUTATES the predicate (one projectionFact sha), so S' != SerializeStatementJson(receiptBytes,
    // RunReceipt.FromJson(receiptBytes)). cosign passes AND the subject digest matches, so ONLY
    // Corrected's internal decoded-payload==reconstruction byte comparison can catch it (cosign's
    // --check-claims never verifies predicate CONTENT). This isolates + FORCES the byte-equality: a
    // GREEN that returns Verified on cosign-Ok + cert-SHA match WITHOUT the internal comparison fails
    // here. DECISION: the divergence is a PREDICATE-content mismatch while the subject digest MATCHES,
    // so no existing reason fits (subject-digest-mismatch needs a differing subject; projection-policy-
    // mismatch is the narrower manifest-policy check) - T3b adds StatementReconstructionMismatch. NOT
    // gated on Provisioned: the fake cosign runs regardless. RED: the T3a cosign-Ok placeholder
    // returns UnclassifiedVerifierFault.
    [Fact]
    public void Verify_rejects_when_decoded_payload_diverges_from_reconstruction()
    {
        FixtureCopy fx = FixtureCopy.Of("pos"); // the real POS receipt is the reconstruction source
        try
        {
            // Craft S' = the POS statement (byte-equal to the reconstruction) with ONE projectionFact
            // sha mutated; the subject digest is untouched, so sha256(receipt) still matches. base64 it
            // as the bundle DSSE payload. A single predicate-byte change makes S' != reconstruction.
            string statement = Encoding.UTF8.GetString(File.ReadAllBytes(FixtureFile("pos", "determinism-statement.json")));
            const string origPf = "9838713255bf681ed6e579089c0573e730f5f00fb0ca7c253b47c306676d8c73";
            Assert.Contains(origPf, statement); // the target predicate field is present (AP-010)
            string sPrime = statement.Replace(origPf, "0" + origPf.Substring(1)); // flip 1 hex char
            Assert.NotEqual(statement, sPrime);

            string payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(sPrime));
            File.WriteAllText(fx.BundlePath,
                "{\"mediaType\":\"application/vnd.dev.sigstore.bundle.v0.3+json\"," +
                "\"dsseEnvelope\":{\"payload\":\"" + payloadB64 + "\"," +
                "\"payloadType\":\"application/vnd.in-toto+json\",\"signatures\":[]}}");

            var req = new DeterminismVerifyRequest
            {
                CosignBinPath = MakeExitZeroFakeCosign(fx.Dir), // exits 0 - isolates byte-equality
                BundlePath = fx.BundlePath,
                ReceiptPath = fx.ReceiptPath,
                TrustRootPath = WriteMinimalRoot(fx.Dir),
                WorkingDirectory = fx.Dir,
                ExpectedRid = "linux-x64",
                Identity = DeterminismVerifyIdentity.Fixture,
                CertWorkflowSha = FixtureAttestedCommit,
                Timeout = TimeSpan.FromSeconds(30),
            };

            DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

            Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
            Assert.Equal(DeterminismVerifyReason.StatementReconstructionMismatch, r.Reason); // RED driver
        }
        finally { fx.Dispose(); }
    }

    // ============ section E2 — QA-001: staleness/ancestry safety inputs are fail-closed ============
    // The staleness (INV-018/019) and ancestry (INV-012/019) inputs to DeterminismVerifyRequest default
    // to the SAFE direction. These cells drive them THROUGH the full Verify cosign-Ok policy branch
    // (a fake-exit-0 cosign + the real byte-equal POS statement + the matching cert-SHA), proving the
    // gate fires INSIDE Verify, not only in the isolated layer-1 unit. Not gated on Provisioned.

    // A request that OMITS both safety inputs inherits the fail-closed defaults (stale=true,
    // ancestry=Uncomputable) and is REJECTED, never Verified — so a future caller that forgets them
    // fails closed, not open (the QA-001 fail-open-by-default this closes). Staleness is checked first.
    [Fact]
    public void Verify_omitting_staleness_and_ancestry_fails_closed_by_default()
    {
        FixtureCopy fx = FixtureCopy.Of("pos");
        try
        {
            var req = new DeterminismVerifyRequest
            {
                CosignBinPath = MakeExitZeroFakeCosign(fx.Dir), // exits 0 -> reaches the layer-1 policy
                BundlePath = fx.BundlePath,                     // real POS bundle -> byte-equal payload
                ReceiptPath = fx.ReceiptPath,                   // real POS receipt (reconstruction source)
                TrustRootPath = WriteMinimalRoot(fx.Dir),
                WorkingDirectory = fx.Dir,
                ExpectedRid = "linux-x64",
                Identity = DeterminismVerifyIdentity.Fixture,
                CertWorkflowSha = FixtureAttestedCommit,
                Timeout = TimeSpan.FromSeconds(30),
                // ManifestStale / AttestedCommitAncestry DELIBERATELY omitted -> the safe defaults.
            };

            DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

            Assert.NotEqual(DeterminismVerifyOutcome.Verified, r.Outcome);
            Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
            Assert.Equal(DeterminismVerifyReason.StaleSubjectManifest, r.Reason);
        }
        finally { fx.Dispose(); }
    }

    // ManifestStale=true (ancestry accept) -> StaleSubjectManifest, INSIDE Verify's cosign-Ok branch.
    [Fact]
    public void Verify_rejects_stale_manifest_through_cosign_ok_policy()
    {
        FixtureCopy fx = FixtureCopy.Of("pos");
        try
        {
            DeterminismVerifyResult r = DeterminismVerifier.Verify(
                FakeOkPolicyRequest(fx, manifestStale: true, ancestry: AncestryStatus.Ancestor));
            Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
            Assert.Equal(DeterminismVerifyReason.StaleSubjectManifest, r.Reason);
        }
        finally { fx.Dispose(); }
    }

    // AttestedCommitAncestry=NotAncestor (non-stale) -> AttestedCommitNotAncestor.
    [Fact]
    public void Verify_rejects_non_ancestor_attested_commit_through_cosign_ok_policy()
    {
        FixtureCopy fx = FixtureCopy.Of("pos");
        try
        {
            DeterminismVerifyResult r = DeterminismVerifier.Verify(
                FakeOkPolicyRequest(fx, manifestStale: false, ancestry: AncestryStatus.NotAncestor));
            Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
            Assert.Equal(DeterminismVerifyReason.AttestedCommitNotAncestor, r.Reason);
        }
        finally { fx.Dispose(); }
    }

    // AttestedCommitAncestry=Uncomputable (a shallow clone / absent commit) -> AncestryUncomputable,
    // REJECTED, never unavailable (RS-013).
    [Fact]
    public void Verify_rejects_uncomputable_ancestry_through_cosign_ok_policy()
    {
        FixtureCopy fx = FixtureCopy.Of("pos");
        try
        {
            DeterminismVerifyResult r = DeterminismVerifier.Verify(
                FakeOkPolicyRequest(fx, manifestStale: false, ancestry: AncestryStatus.Uncomputable));
            Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
            Assert.Equal(DeterminismVerifyReason.AncestryUncomputable, r.Reason);
        }
        finally { fx.Dispose(); }
    }

    // Shared builder: the real byte-equal POS fixture through a fake-exit-0 cosign, so Verify reaches
    // the layer-1 claim policy with the supplied staleness/ancestry inputs (QA-001 class fix).
    private static DeterminismVerifyRequest FakeOkPolicyRequest(
        FixtureCopy fx, bool manifestStale, AncestryStatus ancestry) => new()
        {
            CosignBinPath = MakeExitZeroFakeCosign(fx.Dir),
            BundlePath = fx.BundlePath,
            ReceiptPath = fx.ReceiptPath,
            TrustRootPath = WriteMinimalRoot(fx.Dir),
            WorkingDirectory = fx.Dir,
            ExpectedRid = "linux-x64",
            Identity = DeterminismVerifyIdentity.Fixture,
            CertWorkflowSha = FixtureAttestedCommit,
            ManifestStale = manifestStale,
            AttestedCommitAncestry = ancestry,
            Timeout = TimeSpan.FromSeconds(30),
        };

    // ============ section E3 — QA-004: orchestrator input hardening (INV-014 / AP-007) ============
    // DeterminismVerifier reads the receipt + bundle directly, AHEAD of the seam's own FileInputs
    // validation. A symlinked or oversize input must be rejected as malformed BEFORE the orchestrator
    // reads it — never followed, never read unbounded (OOM). Not gated on Provisioned.

    [Fact]
    public void Verify_rejects_symlinked_receipt_before_unbounded_read()
    {
        FixtureCopy fx = FixtureCopy.Of("pos");
        try
        {
            // Replace the receipt with a SYMLINK to the real bytes: the no-symlink policy must reject
            // it as malformed-receipt BEFORE File.ReadAllBytes would follow the link.
            string realReceipt = fx.ReceiptPath + ".real";
            File.Move(fx.ReceiptPath, realReceipt);
            File.CreateSymbolicLink(fx.ReceiptPath, realReceipt);

            DeterminismVerifyResult r = DeterminismVerifier.Verify(
                FakeOkPolicyRequest(fx, manifestStale: false, ancestry: AncestryStatus.Ancestor));

            Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
            Assert.Equal(DeterminismVerifyReason.MalformedReceipt, r.Reason);
        }
        finally { fx.Dispose(); }
    }

    [Fact]
    public void Verify_rejects_oversize_bundle_before_unbounded_read()
    {
        FixtureCopy fx = FixtureCopy.Of("pos");
        try
        {
            // A bundle that is VALID JSON but exceeds the 64 MiB input cap. This is a GENUINE cap guard,
            // not a vacuous one: WITHOUT the pre-read cap the bundle PARSES (valid JSON), reaches
            // VerifyCosignOk, and fails with the DIFFERENT reason StatementReconstructionMismatch (it has
            // no dsseEnvelope). So asserting MalformedBundle proves the pre-read size cap fired, never a
            // downstream parse failure. (A sparse zero-file would be rejected by BOTH cap and parse -> the
            // reason could not distinguish them.)
            using (var w = new StreamWriter(fx.BundlePath, append: false))
            {
                w.Write("{}");
                string pad = new string(' ', 1 << 20); // 1 MiB of JSON-legal trailing whitespace
                for (int i = 0; i < 65; i++) // 65 MiB > 64 MiB cap
                {
                    w.Write(pad);
                }
            }

            DeterminismVerifyResult r = DeterminismVerifier.Verify(
                FakeOkPolicyRequest(fx, manifestStale: false, ancestry: AncestryStatus.Ancestor));

            Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
            Assert.Equal(DeterminismVerifyReason.MalformedBundle, r.Reason);
        }
        finally { fx.Dispose(); }
    }

    // MA-E: the stat-size cap trusts st_size, which a character device / FIFO reports as 0. The
    // bounded read is the real cap: an infinite special file yields more than the cap and is rejected,
    // never read unbounded into an OOM.
    [Fact]
    public void ReadRegularFileWithinCap_bounds_an_infinite_special_file()
    {
        if (!File.Exists("/dev/zero")) { return; } // Linux special file; skip elsewhere
        byte[]? bytes = CosignRunner.ReadRegularFileWithinCap("/dev/zero", out string? reason, inputCap: 4096);
        Assert.Null(bytes);       // rejected — the infinite stream exceeds the (small) cap
        Assert.NotNull(reason);   // ...never read unbounded
    }

    [Fact]
    public void ReadRegularFileWithinCap_returns_exact_bytes_for_a_regular_file()
    {
        string p = Path.Combine(Path.GetTempPath(), "mae-" + Guid.NewGuid().ToString("N"));
        byte[] payload = Encoding.UTF8.GetBytes("hello determinism");
        File.WriteAllBytes(p, payload);
        try
        {
            byte[]? bytes = CosignRunner.ReadRegularFileWithinCap(p, out string? reason, inputCap: 4096);
            Assert.Null(reason);
            Assert.NotNull(bytes);
            Assert.Equal(payload, bytes);
        }
        finally { File.Delete(p); }
    }

    // MA-E [integration]: a character-device RECEIPT (/dev/zero) reports st_size=0, so the stat-size
    // cap alone ACCEPTS it and File.ReadAllBytes would then read it forever (OOM). The bounded read
    // rejects it as malformed BEFORE the unbounded read, and before cosign is ever launched.
    [Fact]
    public void Verify_rejects_special_file_receipt_before_unbounded_read()
    {
        if (!File.Exists("/dev/zero")) { return; } // Linux special file whose stat size (0) is a lie
        FixtureCopy fx = FixtureCopy.Of("pos");
        try
        {
            var req = new DeterminismVerifyRequest
            {
                CosignBinPath = MakeExitZeroFakeCosign(fx.Dir),
                BundlePath = fx.BundlePath,
                ReceiptPath = "/dev/zero",
                TrustRootPath = WriteMinimalRoot(fx.Dir),
                WorkingDirectory = fx.Dir,
                ExpectedRid = "linux-x64",
                Identity = DeterminismVerifyIdentity.Fixture,
                CertWorkflowSha = FixtureAttestedCommit,
                ManifestStale = false,
                AttestedCommitAncestry = AncestryStatus.Ancestor,
                Timeout = TimeSpan.FromSeconds(30),
            };

            DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

            Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
            Assert.Equal(DeterminismVerifyReason.MalformedReceipt, r.Reason);
        }
        finally { fx.Dispose(); }
    }

    // ================= section F — audit-finding-3 transient faults through the REAL path =========

    // Tests INV-012 [integration] (EA-009 transient fault — chmod-000 UNREADABLE trust root →
    // Unavailable + trust-root-or-tool-unreadable): a present-but-unreadable pinned root is an I/O
    // fault, induced through the REAL cosign subprocess (not synthetic layer-1 injection). It maps to
    // the transient UNAVAILABLE severity, distinct from the digest-mismatch (rejected) fault below.
    // RED: the T3a NonZeroExit branch returns Rejected + UnclassifiedVerifierFault (not unavailable).
    [Fact]
    public void Unreadable_trust_root_is_unavailable_trust_root_or_tool_unreadable()
    {
        var rc = RealCosign.Resolve();
        using var fx = FixtureCopy.Of("pos");

        if (!rc.Provisioned)
        {
            AssertHonestUnavailableFallback(DeterminismVerifier.Verify(
                rc.FixtureRequest(fx, DeterminismVerifyIdentity.Fixture, FixtureAttestedCommit)));
            return;
        }

        // A2: under root / CAP_DAC_OVERRIDE a mode-000 file stays readable to the owner, so chmod-000
        // cannot induce the EA-009 unreadable-root fault (and a readable root legitimately verifies —
        // a `!= Verified` assertion would fail a CORRECT GREEN in a root CI container). Gate on an
        // effective-readability probe: a JUSTIFIED skip (assert the environmental fact, never a silent
        // pass), leaving a uid-independent unreadable-root fixture as a recorded residual.
        if (Mode000FileStaysReadable())
        {
            Assert.True(true, "recorded residual: uid-independent unreadable-root induction needed");
            return;
        }

        // Copy the resolved root into the temp dir and chmod it 000 (present-but-unreadable).
        string rootCopy = Path.Combine(fx.Dir, "unreadable_root.json");
        File.Copy(rc.TrustRootPath!, rootCopy);
        File.SetUnixFileMode(rootCopy, (UnixFileMode)0); // ---------- (chmod 000)
        DeterminismVerifyRequest req = rc.FixtureRequest(
            fx, DeterminismVerifyIdentity.Fixture, FixtureAttestedCommit, trustRootOverride: rootCopy);

        DeterminismVerifyResult r = DeterminismVerifier.Verify(req);
        try
        {
            Assert.Equal(DeterminismVerifyOutcome.Unavailable, r.Outcome);
            Assert.Equal(DeterminismVerifyReason.TrustRootOrToolUnreadable, r.Reason);
        }
        finally
        {
            try { File.SetUnixFileMode(rootCopy, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
        }
    }

    // A2 probe: create a fresh mode-000 file and test whether the current process can still read it
    // (true under root / CAP_DAC_OVERRIDE). Never throws; cleans up.
    private static bool Mode000FileStaysReadable()
    {
        string canary = Path.Combine(Path.GetTempPath(), "inv010-canary-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(canary, "x");
            File.SetUnixFileMode(canary, (UnixFileMode)0);
            try { _ = File.ReadAllBytes(canary); return true; }
            catch { return false; }
        }
        catch { return false; }
        finally
        {
            try { File.SetUnixFileMode(canary, UnixFileMode.UserRead | UnixFileMode.UserWrite); File.Delete(canary); }
            catch { }
        }
    }

    // Tests INV-012 [integration] (EA-009 hard fault — a digest-MISMATCHED trust root → Rejected +
    // trust-root-or-pin-mismatch): a readable-but-WRONG trust root (a different-content root whose
    // digest does not match the pinned expectation) is a hard reject, DISTINCT from the *unreadable*
    // transient fault above. Induced through the REAL path (a swapped-content root file). RED: the
    // T3a NonZeroExit branch returns UnclassifiedVerifierFault.
    [Fact]
    public void Digest_mismatched_trust_root_is_rejected_trust_root_or_pin_mismatch()
    {
        var rc = RealCosign.Resolve();
        using var fx = FixtureCopy.Of("pos");
        // A structurally-plausible but WRONG root (empty trustedroot media type shell): readable,
        // but its content/digest does not match the pinned root that verifies the fixture bundle.
        string wrongRoot = Path.Combine(fx.Dir, "wrong_root.json");
        File.WriteAllText(wrongRoot,
            "{\"mediaType\":\"application/vnd.dev.sigstore.trustedroot.v1+json\",\"tlogs\":[]}");
        DeterminismVerifyRequest req = rc.FixtureRequest(
            fx, DeterminismVerifyIdentity.Fixture, FixtureAttestedCommit, trustRootOverride: wrongRoot);

        DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        Assert.Equal(DeterminismVerifyReason.TrustRootOrPinMismatch, r.Reason);
    }

    // ================= INV-013 meta-assert — no committed fixture carries the production identity ====

    // Tests INV-013 [integration] (meta-assertion): scan test/attestations/** — EVERY committed
    // bundle's leaf certificate SAN is the FIXTURE identity, NEVER the production identity
    // (…/p3-determinism-sign.yml@refs/heads/main). The production-identity ACCEPT branch is a recorded
    // PR3 residual — never a committed fixture and never asserted (RS-006/RS-011). Hermetic: decode
    // the cert bytes from each bundle's verificationMaterial and search for the SAN token. Genuine
    // guard (holds now; would fail if a production-identity bundle were ever committed before PR3).
    [Fact]
    public void No_committed_fixture_carries_the_production_identity()
    {
        string root = TestPaths.RepoFile("test", "attestations");
        var bundles = Directory.EnumerateFiles(root, "*.sigstore.json", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(bundles); // AP-010: the scan is not vacuously over an empty set.

        const string productionSanToken = "p3-determinism-sign.yml";
        const string fixtureSanToken = "p3-fixture-sign.yml";

        foreach (string bundlePath in bundles)
        {
            byte[] certBytes = LeafCertBytes(bundlePath);
            string certAscii = Encoding.Latin1.GetString(certBytes);
            Assert.DoesNotContain(productionSanToken, certAscii, StringComparison.Ordinal);
            Assert.Contains(fixtureSanToken, certAscii, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // helpers (test-local — implementation logic is fine in a test file)
    // ---------------------------------------------------------------------------------------------

    private static string FixtureFile(string dir, string name)
        => TestPaths.RepoFile("test", "attestations", "fixtures", dir, name);

    private static byte[] DecodeDssePayload(string bundlePath)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(bundlePath));
        string b64 = doc.RootElement.GetProperty("dsseEnvelope").GetProperty("payload").GetString()!;
        return Convert.FromBase64String(b64);
    }

    private static byte[] LeafCertBytes(string bundlePath)
    {
        // Walk verificationMaterial for a base64 'rawBytes'/'certificate' string; the leaf Fulcio
        // cert carries the SAN as a UTF-8 URI in its DER, so the SAN token is findable in the bytes.
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(bundlePath));
        var found = new List<string>();
        void Walk(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty p in el.EnumerateObject())
                    {
                        if ((p.Name == "rawBytes" || p.Name == "certificate") && p.Value.ValueKind == JsonValueKind.String)
                        {
                            found.Add(p.Value.GetString()!);
                        }
                        Walk(p.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement e in el.EnumerateArray()) Walk(e);
                    break;
            }
        }
        Walk(doc.RootElement.GetProperty("verificationMaterial"));
        Assert.NotEmpty(found); // a bundle with no cert would make the SAN scan vacuous (AP-010).

        var bytes = new List<byte>();
        foreach (string b64 in found)
        {
            try { bytes.AddRange(Convert.FromBase64String(b64)); } catch (FormatException) { /* not a cert */ }
        }
        return bytes.ToArray();
    }

    // A bundle-payload tamper: flip one base64 char of .dsseEnvelope.payload so the signed payload
    // no longer matches the tlog/envelope hash (a crypto reject — NEVER a policy mutation, INV-013).
    private static string TamperFlipPayloadByte(string bundleJson)
    {
        JsonObject b = (JsonObject)JsonNode.Parse(bundleJson)!;
        var env = (JsonObject)b["dsseEnvelope"]!;
        string payload = (string)env["payload"]!;
        char c = payload[20];
        char flipped = c == 'A' ? 'B' : 'A';
        env["payload"] = payload.Substring(0, 20) + flipped + payload.Substring(21);
        return b.ToJsonString();
    }

    private static void AssertHonestUnavailableFallback(DeterminismVerifyResult r)
    {
        // RS-015 / AP-013: a genuinely degraded env records a TYPED reason, never a silent skip.
        // On a linux-x64 host with cosign absent this is verifier-unavailable; the section E
        // from-clean assertion is the forcing function that the real cosign path executes under the
        // provisioned gate command. The outcome must never be Verified (fail-closed).
        Assert.NotEqual(DeterminismVerifyOutcome.Verified, r.Outcome);
        Assert.False(r.Satisfied);
    }

    // ---- B2 seam helpers: a fake cosign that exits 0 + a minimal trust-root file ----

    private static readonly string BashAbs = ResolveBashAbsolute();

    private static string ResolveBashAbsolute()
    {
        foreach (string c in new[] { "/bin/bash", "/usr/bin/bash", "/usr/local/bin/bash" })
        {
            if (File.Exists(c)) return c;
        }
        throw new FileNotFoundException("bash not found at a known absolute path - the B2 fake cosign cannot run.");
    }

    private static string MakeExitZeroFakeCosign(string dir)
    {
        string path = Path.Combine(dir, "fake-cosign-ok");
        File.WriteAllText(path, "#!" + BashAbs + "\nexit 0\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return path;
    }

    private static string WriteMinimalRoot(string dir)
    {
        string path = Path.Combine(dir, "b2_root.json");
        File.WriteAllText(path, "{\"mediaType\":\"application/vnd.dev.sigstore.trustedroot.v1+json\"}");
        return path;
    }

    // ---- FixtureCopy: a temp-dir copy of a committed fixture (for in-place mutation) ----

    private sealed class FixtureCopy : IDisposable
    {
        internal required string Dir { get; init; }
        internal required string BundlePath { get; init; }
        internal required string ReceiptPath { get; init; }

        internal static FixtureCopy Of(
            string which,
            Func<string, string>? tamperBundle = null,
            Func<byte[], byte[]>? tamperReceipt = null,
            string? replaceBundle = null)
        {
            string dir = Path.Combine(Path.GetTempPath(), "inv010-l2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            string bundle = Path.Combine(dir, "determinism.sigstore.json");
            if (replaceBundle is not null)
            {
                File.WriteAllText(bundle, replaceBundle);
            }
            else
            {
                string src = File.ReadAllText(FixtureFile(which, "determinism.sigstore.json"));
                File.WriteAllText(bundle, tamperBundle is null ? src : tamperBundle(src));
            }

            string receipt = Path.Combine(dir, "determinism-receipt.json");
            byte[] rb = File.ReadAllBytes(FixtureFile(which, "determinism-receipt.json"));
            File.WriteAllBytes(receipt, tamperReceipt is null ? rb : tamperReceipt(rb));

            return new FixtureCopy { Dir = dir, BundlePath = bundle, ReceiptPath = receipt };
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); } catch { }
        }
    }

    // ---- RealCosign: the env-seam locator (COSIGN_BIN + TRUSTED_ROOT) ----

    private sealed class RealCosign
    {
        internal string? CosignBinPath { get; init; }
        internal string? TrustRootPath { get; init; }
        internal bool HostIsLinuxX64 { get; init; }

        internal bool Provisioned =>
            HostIsLinuxX64
            && CosignBinPath is not null && File.Exists(CosignBinPath)
            && TrustRootPath is not null && File.Exists(TrustRootPath);

        internal DeterminismVerifyRequest FixtureRequest(
            FixtureCopy fx, DeterminismVerifyIdentity identity, string certWorkflowSha,
            string? trustRootOverride = null,
            bool manifestStale = false,
            AncestryStatus attestedCommitAncestry = AncestryStatus.Ancestor) => new()
            {
                // If cosign is unresolved, point at an absolute-but-missing path so the REAL Verify
                // path fails closed with verifier-unavailable (never a skip) — the honest fallback.
                CosignBinPath = CosignBinPath ?? "/nonexistent/pinned/cosign",
                BundlePath = fx.BundlePath,
                ReceiptPath = fx.ReceiptPath,
                TrustRootPath = trustRootOverride ?? TrustRootPath ?? Path.Combine(fx.Dir, "trusted_root.json"),
                WorkingDirectory = fx.Dir,
                ExpectedRid = "linux-x64",
                Identity = identity,
                CertWorkflowSha = certWorkflowSha,
                // QA-001: the fixture positive proves accept via EXPLICIT accept-valued safety inputs,
                // never the request record's (now fail-closed) defaults.
                ManifestStale = manifestStale,
                AttestedCommitAncestry = attestedCommitAncestry,
                Timeout = TimeSpan.FromSeconds(60),
            };

        internal static RealCosign Resolve()
        {
            bool linuxX64 = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                && RuntimeInformation.OSArchitecture == Architecture.X64;

            string? bin = FirstReadable(
                Environment.GetEnvironmentVariable("COSIGN_BIN"),
                Path.Combine(Home(), ".cache", "cosign", "v3.1.2", "cosign-linux-amd64"));

            if (bin is null && linuxX64)
            {
                bin = TryProvisionCosign();
            }

            string sigstoreRoot =
                Path.Combine(Home(), ".sigstore", "root", "tuf-repo-cdn.sigstore.dev", "targets", "trusted_root.json");
            string? root = FirstReadable(Environment.GetEnvironmentVariable("TRUSTED_ROOT"), sigstoreRoot);

            // B1c: on-demand TRUST-ROOT provisioning (mirror TryProvisionCosign for the binary) — a
            // networked linux-x64 host makes the crypto cells NON-VACUOUS even before the section E
            // gate wiring lands, so Provisioned becomes true and the real path actually runs.
            if (root is null && bin is not null && linuxX64)
            {
                root = TryInitializeTrustedRoot(bin, sigstoreRoot);
            }

            return new RealCosign { CosignBinPath = bin, TrustRootPath = root, HostIsLinuxX64 = linuxX64 };
        }

        // Best-effort `cosign initialize` (a TUF fetch) to obtain the pinned trust root at the
        // well-known ~/.sigstore path. Bounded; a failure leaves the honest unavailable fallback.
        private static string? TryInitializeTrustedRoot(string cosignBin, string sigstoreRoot)
        {
            try
            {
                if (File.Exists(sigstoreRoot)) return sigstoreRoot;
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
                return File.Exists(sigstoreRoot) ? sigstoreRoot : null;
            }
            catch { return null; }
        }

        // Best-effort on-demand provision via the COMMITTED provision-cosign.sh, so the REAL path is
        // live on a linux-x64 host with network even before the section E gate wiring lands (a
        // genuine RED, not a phantom). Bounded; a failure leaves the honest unavailable fallback.
        private static string? TryProvisionCosign()
        {
            try
            {
                string script = TestPaths.RepoFile("gate", "tools", "provision-cosign.sh");
                if (!File.Exists(script)) return null;
                string dest = Path.Combine(Home(), ".cache", "cosign", "v3.1.2", "cosign-linux-amd64");
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

        private static string? FirstReadable(params string?[] candidates)
            => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c));

        private static string Home()
            => Environment.GetEnvironmentVariable("HOME")
               ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
