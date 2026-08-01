using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Corrected.Provenance.Determinism;
using Corrected.Provenance.InToto;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-006 (P3, Group B / TB-007): "The signed subject is the run receipt; the
/// manifest is bound into it; the artifact graph is exact." The signed object is an
/// exact graph — run receipt bytes -> SHA-256 subject digest -> versioned Corrected
/// determinism predicate -> in-toto Statement/v1 (pinned _type, exactly ONE subject
/// with canonical name + sha256) -> DSSE payload -> Sigstore bundle. Three enforcement
/// clusters (spec INV-006 Enforcement):
///   (a) a Statement-schema test (pinned _type, predicate-type URI, subject
///       name/algorithm, exactly-one-subject, embed-vs-reference all pinned);
///   (b) a hash test binding the subject digest to the receipt bytes
///       (subject sha256 == SHA-256 of the exact receipt bytes);
///   (c) an assertion that the receipt carries the manifest digest + policy version.
///
/// These tests exercise the REAL Corrected.Provenance substrate types (no mocks) over
/// a REAL PR1 producer receipt (AP-031). At RED the builder/parser are STUB:TDD and
/// return zero-values, so every rule test below fails as an ASSERTION.
///
/// Scope note: this is TRACK 1 (INV-006 only). INV-001/002 (receipt schema + role/kind
/// registries), INV-007/008/009 (signing), INV-010/011/012 (verification) are OTHER
/// tracks — deliberately not tested here.
/// </summary>
public class Inv006SignedSubjectTests
{
    // ---- Pinned contract values (the test is the source of truth in RED) ----

    // in-toto Statement/v1 media identity — the fixed upstream standard URI.
    private const string StatementTypeV1 = "https://in-toto.io/Statement/v1";

    // DSSE in-toto JSON payload media type — the fixed upstream standard value.
    private const string DssePayloadType = "application/vnd.in-toto+json";

    // DECISION: the spec pins "a versioned Corrected determinism predicate (a pinned
    // predicate-type URI)" but does not give the exact literal. Pinning the concrete
    // URI here as the contract; GREEN must stamp exactly this (or the human reconciles
    // the literal during test audit). Chosen shape: corrected domain + versioned path.
    private const string PredicateTypeUri = "https://correctless.org/attestations/determinism/v1";

    // DECISION: the in-toto subject "name" is a canonical, stable identifier for the
    // signed run receipt. The spec pins "a canonical name" without the literal; pinning
    // this stable value as the contract for GREEN.
    private const string SubjectName = "determinism-run-receipt";

    // ---- Values that MUST appear in the receipt (spec INV-006 (c) etc.) ----
    // Source: spikes/dafny-compat/out/determinism-val-1/receipts/determinism-receipt.json
    // (real PR1 determinism-lane producer output). This is a RE-SERIALIZED EXCERPT of
    // that producer output, committed as the fixture
    //   gate/Corrected.Gate.Tests/fixtures/provenance/determinism-receipt.sample.json (AP-031).
    // It is NOT byte-identical to the producer file (re-serialization differs by ~1 byte:
    // producer sha256 b40aa4de…, this fixture's bytes sha256 77afcd91…). The fixture is
    // internally consistent for INV-006's hashing (its own bytes -> its own subject digest).
    // CAVEAT for downstream tracks: any track that pins the PRODUCER receipt's digest must
    // hash the real producer bytes — it must NOT reuse THIS fixture's subject digest.
    private const string ExpectedManifestDigest =
        "c872c710dd390ff8d8050c059077d0eb7d6ef4f2352fc7bf375403014ac18509";
    private const string ExpectedPolicyVersion = "1";
    private const string ExpectedAttestedCommit = "101712a313594685792b70a87cddc054c65bbc0c";
    private const string ExpectedExecutionStatus = "completed";
    private const string ExpectedComparisonStatus = "equal";

    private static readonly string[] ExpectedRoles =
    {
        "run", "route-a", "route-b", "control-a", "control-b",
    };

    // The exact per-role (kind, projection_sha256) facts from the fixture. Pinning the
    // VALUES (not just the 64-hex format) defeats a GREEN that stamps a constant digest
    // for every role. NOTE control-a and control-b legitimately SHARE a projection digest
    // (both project the control report), so distinctness is asserted at the KIND level
    // (>=3 distinct digests across roles), never "all 5 distinct".
    // Source: fixtures/provenance/determinism-receipt.sample.json (run1_evidence / run2_evidence).
    private static readonly (string Role, string Kind, string ProjectionSha256)[] ExpectedRoleFacts =
    {
        ("run",       "run-report",     "9838713255bf681ed6e579089c0573e730f5f00fb0ca7c253b47c306676d8c73"),
        ("route-a",   "route-report",   "0cbcf3bfb383d0263f7327c9158679eade64586e06a9d244a1779e25d03dbc15"),
        ("route-b",   "route-report",   "d34bb0bacfadee85fbea49e6b46f27b714f241c0dd778c9c301b55239cd5671f"),
        ("control-a", "control-report", "0fbba0905ceb137c7dd3f9eea23f99ba3633fde2a03459b2189fb3fdae43498d"),
        ("control-b", "control-report", "0fbba0905ceb137c7dd3f9eea23f99ba3633fde2a03459b2189fb3fdae43498d"),
    };

    private static string ExpectedKindOf(string role)
        => ExpectedRoleFacts.Single(f => f.Role == role).Kind;

    private static string ExpectedProjectionOf(string role)
        => ExpectedRoleFacts.Single(f => f.Role == role).ProjectionSha256;

    private static string ReceiptPath()
        => TestPaths.Fixture("provenance", "determinism-receipt.sample.json");

    private static byte[] ReceiptBytes() => File.ReadAllBytes(ReceiptPath());

    // Infra assertion (NOT an INV-006 rule test): the real-producer fixture must be
    // present so the rule tests below fail for the intended reason, not a missing file.
    [Fact]
    public void Receipt_fixture_is_present_for_live_coverage()
    {
        Assert.True(File.Exists(ReceiptPath()),
            "AP-031: the verbatim PR1 determinism-receipt fixture must be committed + copied to output");
    }

    // =====================================================================
    // (a) Statement-schema tests — every schema pin (INV-006 Enforcement (a))
    // =====================================================================

    // Tests INV-006 (a) [unit]: the Statement _type is pinned to in-toto Statement/v1.
    // RED: the stub builder returns an empty Statement (Type == "") -> assertion fails.
    [Fact]
    public void Statement_type_is_pinned_intoto_v1()
    {
        InTotoStatement stmt = DeterminismAttestation.BuildStatement(ReceiptBytes(), Receipt());
        Assert.Equal(StatementTypeV1, stmt.Type);
    }

    // Tests INV-006 (a) [unit]: the predicate-type URI is pinned (versioned Corrected
    // determinism predicate). RED: stub returns "" -> fails.
    [Fact]
    public void Predicate_type_uri_is_pinned()
    {
        InTotoStatement stmt = DeterminismAttestation.BuildStatement(ReceiptBytes(), Receipt());
        Assert.Equal(PredicateTypeUri, stmt.PredicateType);
    }

    // Tests INV-006 (a) [unit]: EXACTLY ONE subject (an in-toto Statement/v1 with a
    // multi-subject or zero-subject graph is schema-invalid for this predicate).
    // RED: stub returns zero subjects -> Assert.Single fails.
    [Fact]
    public void Statement_has_exactly_one_subject()
    {
        InTotoStatement stmt = DeterminismAttestation.BuildStatement(ReceiptBytes(), Receipt());
        Assert.Single(stmt.Subjects);
    }

    // Tests INV-006 (a) [unit]: the single subject's canonical NAME is pinned.
    // RED: no subject / empty name -> fails.
    [Fact]
    public void Subject_name_is_pinned_canonical()
    {
        InTotoStatement stmt = DeterminismAttestation.BuildStatement(ReceiptBytes(), Receipt());
        Subject subject = Assert.Single(stmt.Subjects);
        Assert.Equal(SubjectName, subject.Name);
    }

    // Tests INV-006 (a) [unit]: the subject digest ALGORITHM is sha256 (64 lowercase
    // hex chars) — the only digest key the subject carries. RED: "" -> fails.
    [Fact]
    public void Subject_digest_algorithm_is_sha256_lowercase_hex()
    {
        InTotoStatement stmt = DeterminismAttestation.BuildStatement(ReceiptBytes(), Receipt());
        Subject subject = Assert.Single(stmt.Subjects);
        Assert.Matches("^[0-9a-f]{64}$", subject.Digest.Sha256);
    }

    // Tests INV-006 (a) [integration]: the embed-vs-reference decision is pinned — the
    // predicate REFERENCES the receipt by digest (ReceiptDigest == subject sha256) and
    // EMBEDS the typed per-role projection facts (one per role), NEVER the volatile raw
    // reports. RED: stub predicate is null -> Assert.IsType fails.
    [Fact]
    public void Predicate_references_receipt_digest_and_embeds_projection_facts()
    {
        InTotoStatement stmt = DeterminismAttestation.BuildStatement(ReceiptBytes(), Receipt());
        Subject subject = Assert.Single(stmt.Subjects);

        DeterminismPredicate predicate = Assert.IsType<DeterminismPredicate>(stmt.Predicate);

        // REFERENCE: the predicate points at the subject by its sha256, not by re-embedding bytes.
        Assert.False(string.IsNullOrEmpty(predicate.ReceiptDigest));
        Assert.Equal(subject.Digest.Sha256, predicate.ReceiptDigest);

        // EMBED: the typed per-role projection facts (5 roles), each with a projection digest.
        Assert.Equal(ExpectedRoles.Length, predicate.ProjectionFacts.Count);
        Assert.Equal(
            ExpectedRoles.OrderBy(r => r).ToArray(),
            predicate.ProjectionFacts.Select(f => f.Role).OrderBy(r => r).ToArray());

        // Every fact carries its role's CORRECT kind (role->kind map) and the EXACT
        // per-role projection digest VALUE from the receipt — a constant-stamped digest
        // or a wrong kind fails.
        foreach (ProjectionFact fact in predicate.ProjectionFacts)
        {
            Assert.Equal(ExpectedKindOf(fact.Role), fact.Kind);
            Assert.Equal(ExpectedProjectionOf(fact.Role), fact.ProjectionSha256);
        }

        // Distinctness at the KIND level (run/route/control differ; control-a==control-b):
        // a single constant stamped across all roles yields 1 distinct value and fails.
        int distinctDigests = predicate.ProjectionFacts.Select(f => f.ProjectionSha256).Distinct().Count();
        Assert.True(distinctDigests >= 3,
            $"INV-006: per-role projection digests must not be constant-stamped (got {distinctDigests} distinct)");
    }

    // Tests INV-006 (a) [integration]: the Statement is base64-wrapped into a DSSE
    // payload under the pinned in-toto media type, and the payload decodes back to the
    // Statement JSON (the "Statement -> DSSE payload" graph edge; signing is INV-009).
    // RED: stub envelope has "" payload type + empty payload -> fails.
    [Fact]
    public void Dsse_payload_type_is_pinned_and_wraps_the_statement()
    {
        byte[] bytes = ReceiptBytes();
        string subjectSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        InTotoStatement stmt = DeterminismAttestation.BuildStatement(bytes, Receipt());
        DsseEnvelope env = DeterminismAttestation.BuildDsseEnvelope(stmt);

        Assert.Equal(DssePayloadType, env.PayloadType);
        Assert.False(string.IsNullOrEmpty(env.Payload));

        // Full wrap depth: the decoded payload must carry the WHOLE Statement graph, not a
        // minimal {"_type":"…Statement/v1"} shell — assert the subject sha256 AND the
        // predicate-type URI are present too (dropping subject or predicate fails).
        string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(env.Payload));
        Assert.Contains(StatementTypeV1, decoded);
        Assert.Contains(subjectSha256, decoded);
        Assert.Contains(PredicateTypeUri, decoded);
    }

    // =====================================================================
    // (b) Hash test — subject digest is SHA-256 of the EXACT receipt bytes
    // =====================================================================

    // Tests INV-006 (b) [integration]: the subject digest is SHA-256 over the EXACT
    // receipt bytes. Independent BCL SHA-256 is the oracle; the builder's digest must
    // equal it. RED: stub returns "" -> fails.
    [Fact]
    public void Subject_digest_is_sha256_of_exact_receipt_bytes()
    {
        byte[] bytes = ReceiptBytes();
        string expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.Equal(expected, DeterminismAttestation.ComputeSubjectDigest(bytes));
    }

    // Tests INV-006 (b) [integration]: the BUILT Statement's single subject binds to the
    // receipt bytes end-to-end (subject.sha256 == SHA-256 of the exact receipt bytes) —
    // not to some other input hash. RED: no subject / empty digest -> fails.
    [Fact]
    public void Built_statement_subject_sha256_binds_to_receipt_bytes()
    {
        byte[] bytes = ReceiptBytes();
        string expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        InTotoStatement stmt = DeterminismAttestation.BuildStatement(bytes, Receipt());
        Subject subject = Assert.Single(stmt.Subjects);
        Assert.Equal(expected, subject.Digest.Sha256);
    }

    // Tests INV-006 (b) [integration]: the digest is genuinely OVER the bytes — flipping
    // a single receipt byte changes the subject digest (a constant/empty stub cannot
    // distinguish the two). RED: both stub digests are "" (equal) -> Assert.NotEqual fails.
    [Fact]
    public void Subject_digest_changes_when_one_receipt_byte_flips()
    {
        byte[] original = ReceiptBytes();
        byte[] tampered = (byte[])original.Clone();
        int mid = tampered.Length / 2;
        tampered[mid] = (byte)(tampered[mid] ^ 0xFF);

        string digestOriginal = DeterminismAttestation.ComputeSubjectDigest(original);
        string digestTampered = DeterminismAttestation.ComputeSubjectDigest(tampered);

        Assert.NotEqual(digestOriginal, digestTampered);
    }

    // =====================================================================
    // (c) The receipt carries the manifest digest + policy version (+ the
    //     other subject-bound fields the exact graph requires)
    // =====================================================================

    // Tests INV-006 (c) [integration]: the (signed) receipt carries the determinism-
    // subject-manifest digest bound INTO it. RED: FromJson stub -> "" -> fails.
    [Fact]
    public void Receipt_carries_subject_manifest_digest()
    {
        RunReceipt receipt = Receipt();
        Assert.Equal(ExpectedManifestDigest, receipt.SubjectManifestDigest);
    }

    // Tests INV-006 (c) [integration]: the receipt carries the policy version.
    // RED: "" -> fails.
    [Fact]
    public void Receipt_carries_policy_version()
    {
        RunReceipt receipt = Receipt();
        Assert.Equal(ExpectedPolicyVersion, receipt.PolicyVersion);
    }

    // Tests INV-006 [integration]: the signed receipt carries execution_status +
    // comparison_status (the graph's status pair lives in the subject). RED: "" -> fails.
    [Fact]
    public void Receipt_carries_execution_and_comparison_status()
    {
        RunReceipt receipt = Receipt();
        Assert.Equal(ExpectedExecutionStatus, receipt.ExecutionStatus);
        Assert.Equal(ExpectedComparisonStatus, receipt.ComparisonStatus);
    }

    // Tests INV-006 [integration]: the receipt carries attested_commit (bound into the
    // subject; the verifier cross-checks it in INV-011). RED: "" -> fails.
    [Fact]
    public void Receipt_carries_attested_commit()
    {
        RunReceipt receipt = Receipt();
        Assert.Equal(ExpectedAttestedCommit, receipt.AttestedCommit);
    }

    // Tests INV-006 [integration]: the receipt carries the recorded platform identity.
    // RED: default PlatformIdentity (Rid "", ProcessorCount 0) -> fails.
    [Fact]
    public void Receipt_carries_platform_identity()
    {
        RunReceipt receipt = Receipt();
        Assert.Equal("linux-x64", receipt.Platform.Rid);
        Assert.Equal(24, receipt.Platform.ProcessorCount);
    }

    // Tests INV-006 [integration]: the receipt carries the per-role/kind evidence for
    // both runs — 5 roles each, with the role->kind pairing and a per-role projection
    // digest. RED: empty evidence lists -> fails. (INV-002 owns the registry set-equality;
    // this only asserts the subject CARRIES the per-role/kind evidence.)
    [Fact]
    public void Receipt_carries_per_role_kind_evidence()
    {
        RunReceipt receipt = Receipt();

        Assert.Equal(ExpectedRoles.Length, receipt.Run1Evidence.Count);
        Assert.Equal(ExpectedRoles.Length, receipt.Run2Evidence.Count);

        // Both runs carry the SAME role set (not merely the same count).
        string[] expectedRoleSet = ExpectedRoles.OrderBy(r => r).ToArray();
        Assert.Equal(expectedRoleSet, receipt.Run1Evidence.Select(e => e.Role).OrderBy(r => r).ToArray());
        Assert.Equal(expectedRoleSet, receipt.Run2Evidence.Select(e => e.Role).OrderBy(r => r).ToArray());

        // Symmetric per-role assertions across ALL 5 roles for BOTH runs: correct kind
        // (role->kind map), valid 64-hex format, and the EXACT per-role projection digest.
        foreach (IReadOnlyList<RoleEvidence> run in new[] { receipt.Run1Evidence, receipt.Run2Evidence })
        {
            foreach (string role in ExpectedRoles)
            {
                RoleEvidence e = run.Single(x => x.Role == role);
                Assert.False(string.IsNullOrEmpty(e.Kind));
                Assert.Equal(ExpectedKindOf(role), e.Kind);
                Assert.Matches("^[0-9a-f]{64}$", e.ProjectionSha256);
                Assert.Equal(ExpectedProjectionOf(role), e.ProjectionSha256);
            }

            // Distinctness: run/route/control projections differ (constant-stamp fails).
            int distinct = run.Select(x => x.ProjectionSha256).Distinct().Count();
            Assert.True(distinct >= 3,
                $"INV-006: per-role projection digests must not be constant-stamped (got {distinct} distinct)");
        }
    }

    private static RunReceipt Receipt() => RunReceipt.FromJson(ReceiptBytes());
}
