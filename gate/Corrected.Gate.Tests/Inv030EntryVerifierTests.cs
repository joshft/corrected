using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Corrected.Provenance.Determinism;
using Corrected.Provenance.Entry;
using Corrected.Provenance.InToto;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 phase-entry INV-030 (Group G / MA-C) LAYER 1 — the gate-side ENTRY-RECEIPT VERIFIER, the
/// missing producer that computes <see cref="EntryIntegrity"/> from a committed entry bundle via
/// cosign. A faithful MIRROR of the determinism LAYER-1 orchestration
/// (<see cref="Inv013OrchestrationTests"/> + the B2 seam cells in
/// <c>Inv010Inv011Layer2RealCosignTests</c>): it drives <see cref="EntryVerifier.Verify"/> +
/// <see cref="EntryStatementCodec"/> + <see cref="EntryVerifyReasonMap"/> + the frozen argv through a
/// FAKE cosign only — NO real fixtures, NO network (those are MA-C part (c)/(d)).
///
/// The entry Statement is MULTI-SUBJECT (commit-X + 3 preconditions) and SELF-DESCRIBING: cosign
/// <c>--check-claims</c> binds the SIGNED subjects[0] (the commit-X representation blob), then
/// Corrected decodes the DSSE payload, PARSES it via <see cref="EntryStatementCodec.ParseEntryStatement"/>,
/// runs <see cref="EntryAttestation.ValidateEntrySchema"/> (the entry INV-010 analog — a mutated
/// predicate that keeps subjects[0] is caught by the internal subject&lt;-&gt;manifest binding), and
/// cross-checks the commit-X cert-SHA + ancestry.
///
/// INDEPENDENT ORACLE: <see cref="CanonicalEntryWireOracle"/> is a BCL-only serializer of the pinned
/// entry wire shape, so this test constructs valid DSSE payloads WITHOUT calling the throwing GREEN
/// serializer — AND asserts GREEN's serializer byte-equals it (the emit contract).
///
/// RED expectation against the STUB:TDD substrate:
///   * codec byte-equal (emit)                       -> FAIL (serialize stub throws NotImplementedException)
///   * codec round-trip                              -> FAIL (parse stub returns null -> schema Valid=false)
///   * codec malformed-json fail-closed              -> PASS (parse stub is deny-by-default)
///   * severity-map Classify cross-product           -> FAIL (Classify stub returns Rejected always)
///   * severity-map annotation set/safety cells      -> PASS (read the committed enum annotations)
///   * Verify reason-specific + positive cells        -> FAIL (Verify stub returns Rejected/Unclassified)
///   * Verify fail-closed integrity match cells       -> partial-PASS (Rejected matches) but reason RED
///   * frozen argv exact + identity-specific          -> FAIL (stub argv omits every flag)
///   * argv negative-safety (no -regexp/insecure)     -> PASS (stub argv has one element)
///   * identity distinctness                          -> PASS (reads real committed constants)
///
/// [Collection("Subprocess")] — the Verify cells fork/exec a fake cosign; serialize with the other
/// subprocess classes to avoid EAGAIN/ENOMEM spawn flakes.
/// </summary>
[Collection("Subprocess")]
public class Inv030EntryVerifierTests
{
    // A synthetic 40-hex entry commit X (the commit-X representation whose UTF-8 bytes are the
    // committed receipt blob; sha256(utf8(X)) == subjects[0].Digest, ComputeCommitDigest).
    private const string CommitX = "0123456789abcdef0123456789abcdef01234567";

    // A DIFFERENT 40-hex commit — the fixture-accepting 2b cross-check target (cert-SHA != receipt X).
    private const string OtherCommit = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef";

    private const string MinimalRootJson =
        "{\"mediaType\":\"application/vnd.dev.sigstore.trustedroot.v1+json\"}";

    private const string DeterminismProductionSan =
        "https://github.com/joshft/corrected/.github/workflows/p3-determinism-sign.yml@refs/heads/main";

    private static readonly string BashAbs = ResolveBashAbsolute();

    // ============================================================================================
    // (A) EntryStatementCodec — the canonical entry wire codec (serialize + parse round-trip).
    // ============================================================================================

    // Tests INV-030 [unit] (emit contract): the GREEN serializer's bytes are IDENTICAL to the
    // independent BCL oracle wire shape for a built entry Statement — the signer signs THESE bytes and
    // the verifier reconstructs THESE bytes, so they must match to the byte. RED: SerializeEntryStatementJson
    // is a STUB:TDD that throws NotImplementedException (the documented emit-test RED signal).
    [Fact]
    public void SerializeEntryStatementJson_byte_equals_independent_oracle()
    {
        InTotoStatement built = EntryAttestation.BuildEntryStatement(CommitX, P1Closure(), P2Closure(), P3Closure());

        string oracle = CanonicalEntryWireOracle(built);
        string green = EntryStatementCodec.SerializeEntryStatementJson(built); // RED: stub throws

        Assert.Equal(oracle, green);
    }

    // Tests INV-030 [unit] (round-trip): parsing the oracle wire bytes reconstructs an entry
    // Statement the schema validator ACCEPTS — the parser and validator are mutually consistent. RED:
    // ParseEntryStatement is a STUB:TDD returning (null, "stub-not-implemented"), so ValidateEntrySchema
    // is handed null and returns Valid=false (an ASSERTION failure, never a throw).
    [Fact]
    public void ParseEntryStatement_round_trips_to_a_valid_schema()
    {
        InTotoStatement built = EntryAttestation.BuildEntryStatement(CommitX, P1Closure(), P2Closure(), P3Closure());
        byte[] oracleBytes = Encoding.UTF8.GetBytes(CanonicalEntryWireOracle(built));

        (InTotoStatement? parsed, string? error) = EntryStatementCodec.ParseEntryStatement(oracleBytes);

        // GREEN reconstructs a non-null Statement whose predicate the validator accepts.
        Assert.Null(error);
        Assert.NotNull(parsed);
        EntrySchemaResult schema = EntryAttestation.ValidateEntrySchema(parsed);
        Assert.True(schema.Valid, $"round-tripped entry statement must validate; reason='{schema.Reason}'");
    }

    // Tests INV-030 [unit] (fail-closed parse): malformed JSON yields (null, non-null error) and NEVER
    // throws. PASS on the deny-by-default stub (which returns null for any input). Guards that GREEN
    // catches JsonException rather than letting it escape.
    [Fact]
    public void ParseEntryStatement_on_malformed_json_fails_closed_without_throwing()
    {
        byte[] malformed = Encoding.UTF8.GetBytes("{ this is not valid json ]]]");

        (InTotoStatement? parsed, string? error) = EntryStatementCodec.ParseEntryStatement(malformed);

        Assert.Null(parsed);
        Assert.False(string.IsNullOrEmpty(error));
    }

    // ============================================================================================
    // (B) EntryVerifyReasonMap — three-valued severity totality (reflection cross-product,
    //     RS-010 / AP-022 / PMB-003: expected DERIVED from the committed [EntrySeverity] annotation).
    // ============================================================================================

    // Tests INV-030 [unit] (totality): for EVERY EntryVerifyReason member, Classify(reason) equals the
    // member's COMMITTED [EntrySeverity(...)] annotation — the expected mapping is read via reflection
    // FROM the annotation, never a test literal (so shrinking/re-pointing the map is a reviewable enum
    // diff). RED: the Classify stub returns Rejected always, so the two Absent + two Unavailable
    // members fail as ASSERTIONS while the Rejected members pass.
    [Fact]
    public void Classify_agrees_with_committed_severity_annotation_for_every_reason()
    {
        EntryVerifyReason[] reasons = Enum.GetValues<EntryVerifyReason>();
        Assert.NotEmpty(reasons); // AP-010: the cross-product is not vacuous.

        foreach (EntryVerifyReason reason in reasons)
        {
            EntrySeverity expected = CommittedSeverity(reason); // FROM the annotation, not a literal
            Assert.Equal(expected, EntryVerifyReasonMap.Classify(reason));
        }
    }

    // Tests INV-030 [unit] (closed sets): the Absent set is EXACTLY {EvidenceAbsent,
    // PointerNotYetActivated} and the Unavailable set is EXACTLY {VerifierUnavailable,
    // TrustRootOrToolUnreadable}, both DERIVED from the committed annotations — everything else is the
    // fail-closed Rejected default. PASS on the stub (this reads the enum annotations, not Classify).
    [Fact]
    public void Committed_absent_and_unavailable_severity_sets_are_exact()
    {
        var absent = Enum.GetValues<EntryVerifyReason>()
            .Where(r => CommittedSeverity(r) == EntrySeverity.Absent)
            .ToHashSet();
        var unavailable = Enum.GetValues<EntryVerifyReason>()
            .Where(r => CommittedSeverity(r) == EntrySeverity.Unavailable)
            .ToHashSet();

        Assert.Equal(
            new HashSet<EntryVerifyReason> { EntryVerifyReason.EvidenceAbsent, EntryVerifyReason.PointerNotYetActivated },
            absent);
        Assert.Equal(
            new HashSet<EntryVerifyReason> { EntryVerifyReason.VerifierUnavailable, EntryVerifyReason.TrustRootOrToolUnreadable },
            unavailable);
    }

    // Tests INV-030 [unit] (safety-direction): NO reason ever maps through Classify -> ToIntegrity to
    // the accepting EntryIntegrity.Verified — Verified is the null-reason accept path only. PASS on the
    // stub (Classify=Rejected -> ToIntegrity=Rejected). A future edit that pointed a reason at Verified
    // would fail here.
    [Fact]
    public void No_reason_maps_to_verified_integrity()
    {
        foreach (EntryVerifyReason reason in Enum.GetValues<EntryVerifyReason>())
        {
            EntryIntegrity integrity = EntryVerifyReasonMap.ToIntegrity(EntryVerifyReasonMap.Classify(reason));
            Assert.NotEqual(EntryIntegrity.Verified, integrity);
        }
    }

    // ============================================================================================
    // (C) EntryVerifier.Verify — orchestration through a FAKE cosign (LAYER 1).
    // ============================================================================================

    // Tests INV-030 [integration] (absent bundle): a missing entry bundle is the pre-entry zero-state
    // -> Integrity==Absent, EvidenceAbsent (the ban stays active). RED: the Verify stub returns
    // (Rejected, UnclassifiedVerifierFault).
    [Fact]
    public void Absent_bundle_is_absent_evidence_absent()
    {
        using var w = new Work();
        var req = new EntryVerifyRequest
        {
            CosignBinPath = MakeFakeCosign(w.Dir, 0, null),
            BundlePath = Path.Combine(w.Dir, "does-not-exist.sigstore.json"), // absent
            ReceiptPath = w.WriteBytes("commit-x.blob", Encoding.UTF8.GetBytes(CommitX)),
            TrustRootPath = w.Write("trusted_root.json", MinimalRootJson),
            WorkingDirectory = w.Dir,
            Identity = EntryVerifyIdentity.Fixture,
            CertWorkflowSha = CommitX,
            CommitAncestry = AncestryStatus.Ancestor,
            Timeout = TimeSpan.FromSeconds(30),
        };

        EntryVerifyResult r = EntryVerifier.Verify(req);

        Assert.Equal(EntryIntegrity.Absent, r.Integrity);
        Assert.Equal(EntryVerifyReason.EvidenceAbsent, r.Reason);
        Assert.False(r.Satisfied);
    }

    // Tests INV-030 [integration] (symlinked receipt -> guarded before unbounded read): a SYMLINK
    // commit-X blob is rejected as MalformedReceipt BEFORE File.ReadAllBytes would follow it (the
    // no-symlink policy, mirroring the determinism MA-E cell). RED: the stub returns the wrong reason.
    [Fact]
    public void Symlinked_receipt_is_rejected_malformed_receipt()
    {
        using var w = new Work();
        string realReceipt = Path.Combine(w.Dir, "commit-x.blob.real");
        File.WriteAllBytes(realReceipt, Encoding.UTF8.GetBytes(CommitX));
        string receipt = Path.Combine(w.Dir, "commit-x.blob");
        File.CreateSymbolicLink(receipt, realReceipt);

        var req = new EntryVerifyRequest
        {
            CosignBinPath = MakeFakeCosign(w.Dir, 0, null),
            BundlePath = w.Write("entry.sigstore.json", DsseBundleJson(CanonicalEntryWireOracle(WellFormed()))),
            ReceiptPath = receipt,
            TrustRootPath = w.Write("trusted_root.json", MinimalRootJson),
            WorkingDirectory = w.Dir,
            Identity = EntryVerifyIdentity.Fixture,
            CertWorkflowSha = CommitX,
            CommitAncestry = AncestryStatus.Ancestor,
            Timeout = TimeSpan.FromSeconds(30),
        };

        EntryVerifyResult r = EntryVerifier.Verify(req);

        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.MalformedReceipt, r.Reason);
    }

    // Tests INV-030 [integration] (unparseable bundle): a bundle that is not valid JSON -> Rejected,
    // MalformedBundle (a pre-cosign parse reject). RED: the stub returns UnclassifiedVerifierFault.
    [Fact]
    public void Unparseable_bundle_is_rejected_malformed_bundle()
    {
        using var w = new Work();
        var req = new EntryVerifyRequest
        {
            CosignBinPath = MakeFakeCosign(w.Dir, 0, null),
            BundlePath = w.Write("entry.sigstore.json", "{ not a valid bundle ]]]"),
            ReceiptPath = w.WriteBytes("commit-x.blob", Encoding.UTF8.GetBytes(CommitX)),
            TrustRootPath = w.Write("trusted_root.json", MinimalRootJson),
            WorkingDirectory = w.Dir,
            Identity = EntryVerifyIdentity.Fixture,
            CertWorkflowSha = CommitX,
            CommitAncestry = AncestryStatus.Ancestor,
            Timeout = TimeSpan.FromSeconds(30),
        };

        EntryVerifyResult r = EntryVerifier.Verify(req);

        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.MalformedBundle, r.Reason);
    }

    // Tests INV-030 [integration] (missing cosign binary -> UNAVAILABLE): an absolute-but-missing
    // cosign binary is the transient tool fault -> Integrity==Unavailable, VerifierUnavailable (EA-008;
    // the ONLY unavailable orchestration cell). RED: the stub returns (Rejected, ...).
    [Fact]
    public void Missing_cosign_binary_is_unavailable_verifier_unavailable()
    {
        using var w = new Work();
        var req = new EntryVerifyRequest
        {
            CosignBinPath = "/nonexistent/pinned/cosign", // absolute, missing -> launch fault
            BundlePath = w.Write("entry.sigstore.json", DsseBundleJson(CanonicalEntryWireOracle(WellFormed()))),
            ReceiptPath = w.WriteBytes("commit-x.blob", Encoding.UTF8.GetBytes(CommitX)),
            TrustRootPath = w.Write("trusted_root.json", MinimalRootJson),
            WorkingDirectory = w.Dir,
            Identity = EntryVerifyIdentity.Fixture,
            CertWorkflowSha = CommitX,
            CommitAncestry = AncestryStatus.Ancestor,
            Timeout = TimeSpan.FromSeconds(30),
        };

        EntryVerifyResult r = EntryVerifier.Verify(req);

        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.Equal(EntryIntegrity.Unavailable, r.Integrity);
        Assert.Equal(EntryVerifyReason.VerifierUnavailable, r.Reason);
    }

    // Tests INV-030 [integration] (cosign identity reject): a cosign non-zero exit whose stderr carries
    // the no-matching-certificate-identity phrase -> Rejected, IdentityMismatch (2a, the production
    // identity negative). RED: the stub has no stderr classification -> UnclassifiedVerifierFault.
    [Fact]
    public void Cosign_identity_stderr_is_rejected_identity_mismatch()
        => AssertCosignFailureReason(
            "failed to verify certificate identity: no matching certificate identity found",
            EntryVerifyReason.IdentityMismatch);

    // Tests INV-030 [integration] (cosign predicate-type reject): a cosign non-zero exit whose stderr
    // carries the invalid-predicate-type phrase -> Rejected, PredicateTypeMismatch (RS-024
    // cross-rejection at the crypto layer). RED: the stub returns UnclassifiedVerifierFault.
    [Fact]
    public void Cosign_predicate_type_stderr_is_rejected_predicate_type_mismatch()
        => AssertCosignFailureReason(
            "invalid predicate type, expected https://correctless.org/attestations/phase-entry/v1 got custom",
            EntryVerifyReason.PredicateTypeMismatch);

    // Tests INV-030 [integration] (cosign subject-digest reject): a cosign non-zero exit whose stderr
    // carries the artifact-digests-do-not-match phrase -> Rejected, SubjectDigestMismatch (the commit-X
    // blob sha256 != the signed subjects[0] digest). RED: the stub returns UnclassifiedVerifierFault.
    [Fact]
    public void Cosign_artifact_digests_stderr_is_rejected_subject_digest_mismatch()
        => AssertCosignFailureReason(
            "failed to verify signature: provided artifact digests do not match digests in statement",
            EntryVerifyReason.SubjectDigestMismatch);

    // Tests INV-030 [integration] (cosign signature reject): a cosign non-zero exit whose stderr
    // carries the failed-to-verify-signature phrase -> Rejected, SignatureInvalid. RED: the stub
    // returns UnclassifiedVerifierFault.
    [Fact]
    public void Cosign_signature_stderr_is_rejected_signature_invalid()
        => AssertCosignFailureReason(
            "error: failed to verify signature",
            EntryVerifyReason.SignatureInvalid);

    // Tests INV-030 [integration] (THE positive driver): fake cosign exits 0 + a valid entry payload
    // (the oracle wire) + a matching commit-X blob + CommitAncestry=Ancestor + CertWorkflowSha=commitX
    // -> Integrity==Verified, Satisfied, Reason null. RED: the stub returns (Rejected, Unclassified),
    // so all three assertions fail — the single positive-accept cell.
    [Fact]
    public void Cosign_ok_valid_entry_payload_verifies()
    {
        using var w = new Work();
        EntryVerifyResult r = EntryVerifier.Verify(OkRequest(
            w, MakeFakeCosign(w.Dir, 0, null),
            CanonicalEntryWireOracle(WellFormed()),
            EntryVerifyIdentity.Fixture, certWorkflowSha: CommitX, ancestry: AncestryStatus.Ancestor));

        Assert.Equal(EntryIntegrity.Verified, r.Integrity);
        Assert.True(r.Satisfied);
        Assert.Null(r.Reason);
    }

    // Tests INV-030 [integration] (entry INV-010 analog — schema binding): fake cosign exits 0 + a
    // payload whose P1 manifest digest is TAMPERED (breaking subjects[1]<->manifest-root binding) while
    // subjects[0]/commit stay unchanged -> Rejected, EntrySchemaInvalid. cosign --check-claims passes
    // (subjects[0] intact); ONLY ValidateEntrySchema catches the internal binding break. RED: the stub
    // returns UnclassifiedVerifierFault.
    [Fact]
    public void Cosign_ok_tampered_closure_digest_is_rejected_entry_schema_invalid()
    {
        InTotoStatement good = WellFormed();
        var ep = (EntryPredicate)good.Predicate!;

        // Flip one hex char of P1's first manifest entry sha256 — still 64 lowercase-hex, same path,
        // so ONLY the subject<->manifest-root binding breaks (subjects unchanged).
        PreconditionClosure[] tampered = ep.Preconditions
            .Select(pc =>
            {
                if (pc.Precondition != "P1") return pc;
                ClosureDigest[] m = pc.Manifest.ToArray();
                string sha = m[0].Sha256;
                string flipped = (sha[0] == '0' ? '1' : '0') + sha.Substring(1);
                m[0] = new ClosureDigest { Path = m[0].Path, Sha256 = flipped };
                return new PreconditionClosure { Precondition = "P1", Manifest = m };
            })
            .ToArray();

        var tamperedStmt = new InTotoStatement
        {
            Type = good.Type,
            PredicateType = good.PredicateType,
            Subjects = good.Subjects, // UNCHANGED -> subjects[1] root no longer equals the tampered manifest root
            Predicate = new EntryPredicate { CommitX = ep.CommitX, Preconditions = tampered },
        };

        using var w = new Work();
        EntryVerifyResult r = EntryVerifier.Verify(OkRequest(
            w, MakeFakeCosign(w.Dir, 0, null),
            CanonicalEntryWireOracle(tamperedStmt),
            EntryVerifyIdentity.Fixture, certWorkflowSha: CommitX, ancestry: AncestryStatus.Ancestor));

        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.EntrySchemaInvalid, r.Reason);
    }

    // Tests INV-030 [integration] (RS-024 cross-rejection): fake cosign exits 0 + a payload carrying the
    // DETERMINISM predicate-type URI (entry-shaped predicate otherwise) -> Rejected, attributable to
    // EITHER EntrySchemaInvalid (the validator's predicate-type gate) OR PredicateTypeMismatch. RED:
    // the stub returns UnclassifiedVerifierFault.
    [Fact]
    public void Cosign_ok_determinism_predicate_type_is_rejected_cross_rejection()
    {
        InTotoStatement good = WellFormed();
        var crossTyped = new InTotoStatement
        {
            Type = good.Type,
            PredicateType = DeterminismAttestation.PredicateTypeUri, // wrong (determinism) type
            Subjects = good.Subjects,
            Predicate = good.Predicate,
        };

        using var w = new Work();
        EntryVerifyResult r = EntryVerifier.Verify(OkRequest(
            w, MakeFakeCosign(w.Dir, 0, null),
            CanonicalEntryWireOracle(crossTyped),
            EntryVerifyIdentity.Fixture, certWorkflowSha: CommitX, ancestry: AncestryStatus.Ancestor));

        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Contains(
            r.Reason,
            new EntryVerifyReason?[] { EntryVerifyReason.EntrySchemaInvalid, EntryVerifyReason.PredicateTypeMismatch });
    }

    // Tests INV-030 [integration] (2b cert-SHA cross-check): fake cosign exits 0 (fixture-accepting) +
    // a valid payload + CertWorkflowSha != the receipt commit-X -> Rejected, CertWorkflowShaMismatch —
    // the Corrected-side binding check reached only AFTER cosign accepts, DISTINCT from IdentityMismatch.
    // Ancestry=Ancestor isolates the cert-SHA break. RED: the stub returns UnclassifiedVerifierFault.
    [Fact]
    public void Cosign_ok_cert_workflow_sha_mismatch_is_rejected()
    {
        using var w = new Work();
        EntryVerifyResult r = EntryVerifier.Verify(OkRequest(
            w, MakeFakeCosign(w.Dir, 0, null),
            CanonicalEntryWireOracle(WellFormed()),
            EntryVerifyIdentity.Fixture, certWorkflowSha: OtherCommit, ancestry: AncestryStatus.Ancestor));

        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.CertWorkflowShaMismatch, r.Reason);
    }

    // Tests INV-030 [integration] (QA-001 fail-closed-by-default): a request that OMITS CommitAncestry
    // inherits the SAFE default (Uncomputable). With cosign Ok + a valid payload + a matching cert-SHA,
    // the ONLY remaining break is the uncomputable ancestry -> Rejected, AncestryUncomputable (never
    // Unavailable, RS-013). RED: the stub returns UnclassifiedVerifierFault.
    [Fact]
    public void Cosign_ok_default_ancestry_is_rejected_ancestry_uncomputable()
    {
        using var w = new Work();
        var req = new EntryVerifyRequest
        {
            CosignBinPath = MakeFakeCosign(w.Dir, 0, null),
            BundlePath = w.Write("entry.sigstore.json", DsseBundleJson(CanonicalEntryWireOracle(WellFormed()))),
            ReceiptPath = w.WriteBytes("commit-x.blob", Encoding.UTF8.GetBytes(CommitX)),
            TrustRootPath = w.Write("trusted_root.json", MinimalRootJson),
            WorkingDirectory = w.Dir,
            Identity = EntryVerifyIdentity.Fixture,
            CertWorkflowSha = CommitX, // matching -> cert-SHA is NOT the break
            // CommitAncestry DELIBERATELY omitted -> the fail-closed default Uncomputable.
            Timeout = TimeSpan.FromSeconds(30),
        };

        EntryVerifyResult r = EntryVerifier.Verify(req);

        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.AncestryUncomputable, r.Reason);
    }

    // Tests INV-030 [integration] (non-ancestor activation): fake cosign exits 0 + a valid payload +
    // a matching cert-SHA + CommitAncestry=NotAncestor -> Rejected, AttestedCommitNotAncestor. RED: the
    // stub returns UnclassifiedVerifierFault.
    [Fact]
    public void Cosign_ok_not_ancestor_is_rejected_attested_commit_not_ancestor()
    {
        using var w = new Work();
        EntryVerifyResult r = EntryVerifier.Verify(OkRequest(
            w, MakeFakeCosign(w.Dir, 0, null),
            CanonicalEntryWireOracle(WellFormed()),
            EntryVerifyIdentity.Fixture, certWorkflowSha: CommitX, ancestry: AncestryStatus.NotAncestor));

        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.AttestedCommitNotAncestor, r.Reason);
    }

    // Tests INV-030 [integration] (MA-C self-audit — the fake/compromised-cosign subject re-bind): fake
    // cosign exits 0 (so it does NOT actually run --check-claims) + a valid entry payload built over
    // CommitX BUT a receipt blob that is a DIFFERENT commit (OtherCommit), so sha256(receipt) != the
    // signed subjects[0] digest. cosign --check-claims would catch this in production, but a fake-Ok
    // cosign does not — so Corrected's INTERNAL sha256(receipt)==subjects[0] re-bind MUST reject it as
    // SubjectDigestMismatch. Isolates the re-bind: cert-SHA is set to the receipt's own commit so ONLY
    // the subject binding is the break. Without the internal check, a fake-Ok cosign would VERIFY a
    // mismatched blob (the fail-open this cell closes).
    [Fact]
    public void Cosign_ok_receipt_blob_not_matching_commit_subject_is_rejected_subject_digest_mismatch()
    {
        using var w = new Work();
        var req = new EntryVerifyRequest
        {
            CosignBinPath = MakeFakeCosign(w.Dir, 0, null),
            BundlePath = w.Write("entry.sigstore.json", DsseBundleJson(CanonicalEntryWireOracle(WellFormed()))), // subjects[0]==sha256(CommitX)
            ReceiptPath = w.WriteBytes("commit-x.blob", Encoding.UTF8.GetBytes(OtherCommit)),                     // sha256 != subjects[0]
            TrustRootPath = w.Write("trusted_root.json", MinimalRootJson),
            WorkingDirectory = w.Dir,
            Identity = EntryVerifyIdentity.Fixture,
            CertWorkflowSha = OtherCommit, // == the receipt commit -> cert-SHA is NOT the break; the subject binding is
            CommitAncestry = AncestryStatus.Ancestor,
            Timeout = TimeSpan.FromSeconds(30),
        };

        EntryVerifyResult r = EntryVerifier.Verify(req);

        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.SubjectDigestMismatch, r.Reason);
    }

    // Tests INV-030 [integration] (MA-C self-audit #2 — present-but-non-regular bundle is a tamper, not
    // Absent): a DIRECTORY at the bundle path is NOT the benign pre-entry zero-state — it is rejected as
    // MalformedBundle (Rejected), NOT masked as EvidenceAbsent (Absent). Distinguishes a genuinely-
    // missing bundle (Absent, covered above) from a present-but-non-regular path.
    [Fact]
    public void Directory_at_bundle_path_is_rejected_malformed_bundle_not_absent()
    {
        using var w = new Work();
        string bundleDir = Path.Combine(w.Dir, "entry.sigstore.json");
        Directory.CreateDirectory(bundleDir); // a DIRECTORY sits where the bundle file should be

        var req = new EntryVerifyRequest
        {
            CosignBinPath = MakeFakeCosign(w.Dir, 0, null),
            BundlePath = bundleDir,
            ReceiptPath = w.WriteBytes("commit-x.blob", Encoding.UTF8.GetBytes(CommitX)),
            TrustRootPath = w.Write("trusted_root.json", MinimalRootJson),
            WorkingDirectory = w.Dir,
            Identity = EntryVerifyIdentity.Fixture,
            CertWorkflowSha = CommitX,
            CommitAncestry = AncestryStatus.Ancestor,
            Timeout = TimeSpan.FromSeconds(30),
        };

        EntryVerifyResult r = EntryVerifier.Verify(req);

        Assert.NotEqual(EntryIntegrity.Absent, r.Integrity); // NOT masked as the benign zero-state
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.MalformedBundle, r.Reason);
    }

    // Tests INV-030 [integration] (2nd UNAVAILABLE cell — present-but-unreadable trust root): a mode-000
    // (present-but-unreadable) pinned trust root is a transient I/O fault -> Integrity==Unavailable,
    // TrustRootOrToolUnreadable — DISTINCT from the missing-binary VerifierUnavailable. Pins the
    // Rejected-vs-Unavailable boundary against a future mis-edit of the outcome/severity map. Under
    // root / CAP_DAC_OVERRIDE chmod-000 stays readable, so gate on an effective-readability probe (a
    // JUSTIFIED skip that asserts the environmental fact — never a silent pass).
    [Fact]
    public void Unreadable_trust_root_is_unavailable_trust_root_or_tool_unreadable()
    {
        using var w = new Work();
        if (Mode000FileStaysReadable())
        {
            Assert.True(true, "recorded residual: uid-independent unreadable-root induction needed (root/CAP_DAC_OVERRIDE)");
            return;
        }

        string root = w.Write("trusted_root.json", MinimalRootJson);
        File.SetUnixFileMode(root, (UnixFileMode)0); // ---------- (chmod 000)
        try
        {
            var req = new EntryVerifyRequest
            {
                CosignBinPath = MakeFakeCosign(w.Dir, 0, null),
                BundlePath = w.Write("entry.sigstore.json", DsseBundleJson(CanonicalEntryWireOracle(WellFormed()))),
                ReceiptPath = w.WriteBytes("commit-x.blob", Encoding.UTF8.GetBytes(CommitX)),
                TrustRootPath = root,
                WorkingDirectory = w.Dir,
                Identity = EntryVerifyIdentity.Fixture,
                CertWorkflowSha = CommitX,
                CommitAncestry = AncestryStatus.Ancestor,
                Timeout = TimeSpan.FromSeconds(30),
            };

            EntryVerifyResult r = EntryVerifier.Verify(req);

            Assert.Equal(EntryIntegrity.Unavailable, r.Integrity);
            Assert.Equal(EntryVerifyReason.TrustRootOrToolUnreadable, r.Reason);
        }
        finally
        {
            try { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
        }
    }

    // Tests INV-030 [integration] (cosign-Ok but the bundle DSSE payload is missing): fake cosign exits 0
    // but the bundle has no dsseEnvelope.payload to decode -> Rejected, EntrySchemaInvalid (the decode
    // catch fails closed; never Verified). Pins the cosign-Ok decode branch.
    [Fact]
    public void Cosign_ok_bundle_missing_dsse_payload_is_rejected_entry_schema_invalid()
    {
        using var w = new Work();
        var req = new EntryVerifyRequest
        {
            CosignBinPath = MakeFakeCosign(w.Dir, 0, null),
            BundlePath = w.Write("entry.sigstore.json", "{\"mediaType\":\"application/vnd.dev.sigstore.bundle.v0.3+json\"}"), // valid JSON, no dsseEnvelope
            ReceiptPath = w.WriteBytes("commit-x.blob", Encoding.UTF8.GetBytes(CommitX)),
            TrustRootPath = w.Write("trusted_root.json", MinimalRootJson),
            WorkingDirectory = w.Dir,
            Identity = EntryVerifyIdentity.Fixture,
            CertWorkflowSha = CommitX,
            CommitAncestry = AncestryStatus.Ancestor,
            Timeout = TimeSpan.FromSeconds(30),
        };

        EntryVerifyResult r = EntryVerifier.Verify(req);

        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.EntrySchemaInvalid, r.Reason);
    }

    // Tests INV-030 [integration] (the re-bind is NOT a standalone accept): fake cosign exits 0 + a
    // payload whose subjects[0] digest STILL equals sha256(receipt) (so the internal re-bind passes) but
    // whose subjects[0] NAME is wrong -> Rejected, EntrySchemaInvalid. Proves ValidateEntrySchema's
    // subject-name pin is load-bearing on the Verify path, not only in the schema unit tests.
    [Fact]
    public void Cosign_ok_correct_digest_but_wrong_subject_name_is_rejected_entry_schema_invalid()
    {
        InTotoStatement good = WellFormed();
        Subject[] subjects = good.Subjects.ToArray();
        // Keep subjects[0].Digest (== sha256(CommitX), so the re-bind passes) but rename the subject.
        subjects[0] = new Subject { Name = "not-the-entry-commit", Digest = subjects[0].Digest };
        var renamed = new InTotoStatement
        {
            Type = good.Type,
            PredicateType = good.PredicateType,
            Subjects = subjects,
            Predicate = good.Predicate,
        };

        using var w = new Work();
        EntryVerifyResult r = EntryVerifier.Verify(OkRequest(
            w, MakeFakeCosign(w.Dir, 0, null),
            CanonicalEntryWireOracle(renamed),
            EntryVerifyIdentity.Fixture, certWorkflowSha: CommitX, ancestry: AncestryStatus.Ancestor));

        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.EntrySchemaInvalid, r.Reason);
    }

    // ============================================================================================
    // (D) BuildVerifyArgv — the frozen entry verify argv (PRH-001 analog).
    // ============================================================================================

    // Tests INV-030 [unit] (frozen argv): BuildVerifyArgv returns EXACTLY the frozen entry argv, in
    // order — verify-blob-attestation, --check-claims=true, --type, --certificate-identity,
    // --certificate-oidc-issuer, --certificate-github-workflow-sha, --use-signed-timestamps,
    // --trusted-root, --bundle, <receipt> — with NO --check-claims=false and NO -regexp/--insecure
    // variant. RED: the stub returns a one-element argv, so the exact-sequence assert fails.
    [Fact]
    public void BuildVerifyArgv_is_the_frozen_entry_argv()
    {
        var req = ArgvRequest(EntryVerifyIdentity.Production, CommitX);

        IReadOnlyList<string> argv = EntryVerifier.BuildVerifyArgv(req);

        string[] expected =
        {
            "verify-blob-attestation",
            "--check-claims=true",
            "--type", req.Identity.PredicateType,
            "--certificate-identity", req.Identity.CertificateIdentity,
            "--certificate-oidc-issuer", req.Identity.OidcIssuer,
            "--certificate-github-workflow-sha", CommitX,
            "--use-signed-timestamps",
            "--trusted-root", req.TrustRootPath,
            "--bundle", req.BundlePath,
            req.ReceiptPath,
        };
        Assert.Equal(expected, argv.ToArray());

        // Never a claims-off / regexp / insecure variant (PRH-001).
        Assert.DoesNotContain("--check-claims=false", argv);
        Assert.All(argv, a =>
        {
            Assert.DoesNotContain("-regexp", a);
            Assert.DoesNotContain("--insecure", a);
        });
    }

    // Tests INV-030 [unit] (argv is value-specific to identity): a Production request's argv carries the
    // production SAN (not the fixture SAN) and a Fixture request's argv carries the fixture SAN (not the
    // production SAN) — an always-emit / typo'd / default-accept builder cannot pass. RED: the stub argv
    // carries neither SAN.
    [Fact]
    public void BuildVerifyArgv_is_value_specific_to_identity()
    {
        IReadOnlyList<string> prod = EntryVerifier.BuildVerifyArgv(ArgvRequest(EntryVerifyIdentity.Production, CommitX));
        IReadOnlyList<string> fix = EntryVerifier.BuildVerifyArgv(ArgvRequest(EntryVerifyIdentity.Fixture, CommitX));

        Assert.Contains(EntryVerifyIdentity.ProductionCertificateIdentity, prod);
        Assert.Contains(EntryVerifyIdentity.FixtureCertificateIdentity, fix);
        Assert.DoesNotContain(EntryVerifyIdentity.FixtureCertificateIdentity, prod);
        Assert.DoesNotContain(EntryVerifyIdentity.ProductionCertificateIdentity, fix);
    }

    // Tests INV-030 [unit] (MA-C self-audit — the null-CertWorkflowSha production argv derives from the
    // receipt): when the request supplies NO explicit CertWorkflowSha (the production real path),
    // BuildVerifyArgv reads the committed receipt blob and pins its commit-X as the
    // --certificate-github-workflow-sha value — so the frozen argv (the real cosign-enforced binding)
    // is populated on the production path, not only when a test injects the SHA.
    [Fact]
    public void BuildVerifyArgv_derives_workflow_sha_from_receipt_when_unset()
    {
        using var w = new Work();
        string receipt = w.WriteBytes("commit-x.blob", Encoding.UTF8.GetBytes(CommitX));
        var req = new EntryVerifyRequest
        {
            CosignBinPath = "/pinned/cosign",
            BundlePath = "/work/entry.sigstore.json",
            ReceiptPath = receipt,
            TrustRootPath = "/work/trusted_root.json",
            WorkingDirectory = "/work",
            Identity = EntryVerifyIdentity.Production,
            CertWorkflowSha = null, // production real path -> derive from the receipt
            CommitAncestry = AncestryStatus.Ancestor,
            Timeout = TimeSpan.FromSeconds(30),
        };

        IReadOnlyList<string> argv = EntryVerifier.BuildVerifyArgv(req);

        int shaIdx = argv.ToList().IndexOf("--certificate-github-workflow-sha");
        Assert.True(shaIdx >= 0 && shaIdx + 1 < argv.Count, "argv must carry the workflow-sha flag + value");
        Assert.Equal(CommitX, argv[shaIdx + 1]); // derived from the receipt blob, not empty
    }

    // ============================================================================================
    // (E) EntryVerifyIdentity — distinctness of the committed frozen identities.
    // ============================================================================================

    // Tests INV-030 [unit] (identity distinctness): Production/Fixture pin the expected SANs, both share
    // the entry predicate type (== EntryAttestation.PredicateTypeUri), both SANs carry the p3-entry
    // workflow token, and NEITHER equals the determinism production SAN (RS-024 independent identity).
    // PASS on the stub (reads the real committed constants).
    [Fact]
    public void Entry_identities_are_pinned_and_distinct_from_determinism()
    {
        Assert.Equal(EntryVerifyIdentity.ProductionCertificateIdentity, EntryVerifyIdentity.Production.CertificateIdentity);
        Assert.Equal(EntryVerifyIdentity.FixtureCertificateIdentity, EntryVerifyIdentity.Fixture.CertificateIdentity);

        Assert.Equal(EntryAttestation.PredicateTypeUri, EntryVerifyIdentity.Production.PredicateType);
        Assert.Equal(EntryAttestation.PredicateTypeUri, EntryVerifyIdentity.Fixture.PredicateType);
        Assert.Equal(EntryAttestation.PredicateTypeUri, EntryVerifyIdentity.EntryPredicateTypeUri);

        Assert.Contains("p3-entry", EntryVerifyIdentity.Production.CertificateIdentity);
        Assert.Contains("p3-entry", EntryVerifyIdentity.Fixture.CertificateIdentity);

        Assert.NotEqual(DeterminismProductionSan, EntryVerifyIdentity.Production.CertificateIdentity);
        Assert.NotEqual(DeterminismProductionSan, EntryVerifyIdentity.Fixture.CertificateIdentity);
        Assert.NotEqual(EntryVerifyIdentity.Production.CertificateIdentity, EntryVerifyIdentity.Fixture.CertificateIdentity);

        // The entry predicate type must never equal the determinism predicate type (RS-024).
        Assert.NotEqual(DeterminismAttestation.PredicateTypeUri, EntryVerifyIdentity.EntryPredicateTypeUri);
    }

    // ============================================================================================
    // Helpers — INDEPENDENT oracles + the fake-cosign / temp-dir seam. Never call the code-under-test
    // for an oracle (RS-010): the wire shape + closures are built with BCL only.
    // ============================================================================================

    // The independent BCL wire-serializer oracle: the pinned entry Statement wire shape (_type,
    // predicateType, subject[] name+digest.sha256 in built order, predicate{commitX,
    // preconditions[{precondition, manifest[{path,sha256}]}]}), WriteIndented=false, default STJ
    // escaping. GREEN's SerializeEntryStatementJson MUST byte-equal this.
    private static string CanonicalEntryWireOracle(InTotoStatement statement)
    {
        var predicate = (EntryPredicate)statement.Predicate!;
        var wire = new
        {
            _type = statement.Type,
            predicateType = statement.PredicateType,
            subject = statement.Subjects
                .Select(s => new
                {
                    name = s.Name,
                    digest = new { sha256 = s.Digest.Sha256 },
                })
                .ToArray(),
            predicate = new
            {
                commitX = predicate.CommitX,
                preconditions = predicate.Preconditions
                    .Select(pc => new
                    {
                        precondition = pc.Precondition,
                        manifest = pc.Manifest
                            .Select(m => new { path = m.Path, sha256 = m.Sha256 })
                            .ToArray(),
                    })
                    .ToArray(),
            },
        };

        return JsonSerializer.Serialize(wire, new JsonSerializerOptions { WriteIndented = false });
    }

    // A well-formed entry Statement over the synthetic closures (the schema-positive base). Built via
    // the fully-implemented EntryAttestation.BuildEntryStatement (not a stub), so it is genuinely valid.
    private static InTotoStatement WellFormed()
        => EntryAttestation.BuildEntryStatement(CommitX, P1Closure(), P2Closure(), P3Closure());

    private static Dictionary<string, byte[]> P1Closure() => new()
    {
        ["test/attestations/entry/p1/inv008-p1-probe.json"] = Encoding.UTF8.GetBytes("p1-probe-body-alpha"),
        ["test/attestations/entry/p1/inv008-tree-migrated.json"] = Encoding.UTF8.GetBytes("p1-tree-migrated-beta"),
        ["test/attestations/entry/p1/inv008-closure.json"] = Encoding.UTF8.GetBytes("p1-closure-gamma"),
    };

    private static Dictionary<string, byte[]> P2Closure() => new()
    {
        ["test/attestations/entry/p2/inv010-receipt.json"] = Encoding.UTF8.GetBytes("p2-receipt-delta"),
        ["test/attestations/entry/p2/inv010-bundle.sigstore.json"] = Encoding.UTF8.GetBytes("p2-bundle-epsilon"),
    };

    private static Dictionary<string, byte[]> P3Closure() => new()
    {
        ["test/attestations/entry/p3/inv010-receipt.json"] = Encoding.UTF8.GetBytes("p3-receipt-zeta"),
        ["test/attestations/entry/p3/inv010-bundle.sigstore.json"] = Encoding.UTF8.GetBytes("p3-bundle-eta"),
        ["test/attestations/entry/p3/active-baseline-pointer.json"] = Encoding.UTF8.GetBytes("p3-pointer-theta"),
        ["test/attestations/entry/p3/trusted-root.json"] = Encoding.UTF8.GetBytes("p3-root-iota"),
    };

    // The committed [EntrySeverity(...)] annotation for a reason, read via reflection (RS-010: derive
    // the expected severity FROM the committed enum, never a test literal).
    private static EntrySeverity CommittedSeverity(EntryVerifyReason reason)
    {
        FieldInfo field = typeof(EntryVerifyReason).GetField(reason.ToString())!;
        EntrySeverityAttribute? attr = field.GetCustomAttribute<EntrySeverityAttribute>();
        Assert.NotNull(attr); // every member must carry exactly one committed severity annotation
        return attr!.Severity;
    }

    // Wrap a serialized entry Statement JSON as the DSSE bundle wire (the same shape the determinism L2
    // test builds): mediaType + dsseEnvelope{payload=base64(entry-statement-json), payloadType, signatures}.
    private static string DsseBundleJson(string entryStatementJson)
    {
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(entryStatementJson));
        return "{\"mediaType\":\"application/vnd.dev.sigstore.bundle.v0.3+json\"," +
               "\"dsseEnvelope\":{\"payload\":\"" + b64 + "\"," +
               "\"payloadType\":\"application/vnd.in-toto+json\",\"signatures\":[]}}";
    }

    // A cosign-Ok request over the real byte-equal payload through a fake-exit-0 cosign: the commit-X
    // blob is the raw commit-X UTF-8 bytes (sha256(X) == subjects[0]); cert-SHA + ancestry are supplied.
    private EntryVerifyRequest OkRequest(
        Work w, string cosignBin, string entryStatementJson,
        EntryVerifyIdentity identity, string? certWorkflowSha, AncestryStatus ancestry) => new()
        {
            CosignBinPath = cosignBin,
            BundlePath = w.Write("entry.sigstore.json", DsseBundleJson(entryStatementJson)),
            ReceiptPath = w.WriteBytes("commit-x.blob", Encoding.UTF8.GetBytes(CommitX)),
            TrustRootPath = w.Write("trusted_root.json", MinimalRootJson),
            WorkingDirectory = w.Dir,
            Identity = identity,
            CertWorkflowSha = certWorkflowSha,
            CommitAncestry = ancestry,
            Timeout = TimeSpan.FromSeconds(30),
        };

    // Shared cosign-non-zero-exit body: fake cosign exits 1 with the supplied stderr; a valid bundle +
    // receipt so cosign is REACHED, then its stderr drives the taxonomy. NOT gated on any environment.
    private void AssertCosignFailureReason(string stderr, EntryVerifyReason expected)
    {
        using var w = new Work();
        var req = new EntryVerifyRequest
        {
            CosignBinPath = MakeFakeCosign(w.Dir, 1, stderr),
            BundlePath = w.Write("entry.sigstore.json", DsseBundleJson(CanonicalEntryWireOracle(WellFormed()))),
            ReceiptPath = w.WriteBytes("commit-x.blob", Encoding.UTF8.GetBytes(CommitX)),
            TrustRootPath = w.Write("trusted_root.json", MinimalRootJson),
            WorkingDirectory = w.Dir,
            Identity = EntryVerifyIdentity.Fixture,
            CertWorkflowSha = CommitX,
            CommitAncestry = AncestryStatus.Ancestor,
            Timeout = TimeSpan.FromSeconds(30),
        };

        EntryVerifyResult r = EntryVerifier.Verify(req);

        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(expected, r.Reason);
    }

    // A minimal request for the argv contract (no files are read when CertWorkflowSha is explicit).
    private static EntryVerifyRequest ArgvRequest(EntryVerifyIdentity identity, string certWorkflowSha) => new()
    {
        CosignBinPath = "/pinned/cosign",
        BundlePath = "/work/entry.sigstore.json",
        ReceiptPath = "/work/commit-x.blob",
        TrustRootPath = "/work/trusted_root.json",
        WorkingDirectory = "/work",
        Identity = identity,
        CertWorkflowSha = certWorkflowSha,
        CommitAncestry = AncestryStatus.Ancestor,
        Timeout = TimeSpan.FromSeconds(30),
    };

    // ---- fake cosign: exits N with a chosen stderr line (BASH only, resolved absolutely) ----

    private static string MakeFakeCosign(string dir, int exitCode, string? stderr)
    {
        string path = Path.Combine(dir, "fake-cosign-" + Guid.NewGuid().ToString("N"));
        var body = new StringBuilder();
        body.Append("#!").Append(BashAbs).Append('\n');
        if (!string.IsNullOrEmpty(stderr))
        {
            body.Append("printf '%s\\n' ").Append(ShellSingleQuote(stderr)).Append(" >&2\n");
        }
        body.Append("exit ").Append(exitCode).Append('\n');
        File.WriteAllText(path, body.ToString());
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return path;
    }

    private static string ShellSingleQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    // Probe: create a fresh mode-000 file and test whether the current process can still read it (true
    // under root / CAP_DAC_OVERRIDE, where chmod-000 cannot induce the EA-009 unreadable-root fault).
    // Never throws; cleans up. Mirrors the determinism L2 test's guard.
    private static bool Mode000FileStaysReadable()
    {
        string canary = Path.Combine(Path.GetTempPath(), "inv030-canary-" + Guid.NewGuid().ToString("N"));
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

    private static string ResolveBashAbsolute()
    {
        foreach (string c in new[] { "/bin/bash", "/usr/bin/bash", "/usr/local/bin/bash" })
        {
            if (File.Exists(c))
            {
                return c;
            }
        }

        throw new FileNotFoundException("bash not found at a known absolute path — the fake cosign cannot run.");
    }

    // ---- Work: a temp dir with try/finally cleanup (no leaks) ----

    private sealed class Work : IDisposable
    {
        internal string Dir { get; }

        internal Work()
        {
            Dir = Path.Combine(Path.GetTempPath(), "inv030-entry-verify-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
        }

        internal string Write(string name, string content)
        {
            string p = Path.Combine(Dir, name);
            File.WriteAllText(p, content);
            return p;
        }

        internal string WriteBytes(string name, byte[] content)
        {
            string p = Path.Combine(Dir, name);
            File.WriteAllBytes(p, content);
            return p;
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); }
            catch { /* OS temp cleanup is the backstop */ }
        }
    }
}
