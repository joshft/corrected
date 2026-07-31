using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Corrected.Provenance.Determinism;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation T4 — the SHARED canonical Statement serializer
/// <see cref="DeterminismAttestation.SerializeStatementJson"/> (INV-006 subject/predicate
/// pin + the INV-010 byte-equality precondition). This is the SINGLE byte-source the T4
/// signer-emit path writes (<c>determinism-statement.json</c>, which the signer signs) AND
/// the future T3 INV-010 verifier reconstructs-and-byte-compares. If the two sides used
/// DIFFERENT serializers (e.g. a bash-hand-rolled Statement), the signed payload and the
/// verifier's reconstruction would drift by a byte and INV-010 could never hold — which is
/// exactly why the signer must consume a Corrected-built Statement, never build its own.
///
/// PURE UNIT tests over the REAL Corrected.Provenance substrate (no mocks, no subprocess —
/// so NO [Collection("Subprocess")]) driven by the committed REAL PR1 determinism RunReceipt
/// fixture (AP-031). At RED SerializeStatementJson is STUB:TDD and returns "", so every cell
/// below fails as an ASSERTION (empty / missing frozen field / unbound digest), never a
/// compile error.
///
/// Scope: this track adds ONLY the shared serializer + these unit tests. The INV-010 verifier
/// (T3) is a DEFERRED separate track — no real signature / OIDC / Rekor / cosign here.
///
/// AP-031: the subject bytes are the committed verbatim PR1 producer receipt fixture
/// (gate/Corrected.Gate.Tests/fixtures/provenance/determinism-receipt.sample.json), the same
/// real artifact Inv006SignedSubjectTests pins — a genuine determinism-lane RunReceipt.
/// </summary>
public class DeterminismStatementCanonicalTests
{
    // ---- Pinned contract literals (the test is the source of truth in RED). ----
    // These MUST equal DeterminismAttestation.PredicateTypeUri / SubjectName; pinning the
    // literals here independently (A4) means a GREEN that silently re-freezes either value
    // fails this suite, not merely echoes itself.
    private const string PredicateTypeUri = "https://correctless.org/attestations/determinism/v1";
    private const string SubjectName = "determinism-run-receipt";
    private const string StatementTypeV1 = "https://in-toto.io/Statement/v1";

    // The per-role (kind, projection_sha256) facts embedded from run1_evidence — the SAME
    // real values Inv006SignedSubjectTests pins. control-a and control-b legitimately SHARE a
    // projection digest, so distinctness is asserted at >= 3 (never "all 5 distinct").
    // Source: gate/Corrected.Gate.Tests/fixtures/provenance/determinism-receipt.sample.json
    private static readonly (string Role, string Kind, string ProjectionSha256)[] ExpectedRoleFacts =
    {
        ("run",       "run-report",     "9838713255bf681ed6e579089c0573e730f5f00fb0ca7c253b47c306676d8c73"),
        ("route-a",   "route-report",   "0cbcf3bfb383d0263f7327c9158679eade64586e06a9d244a1779e25d03dbc15"),
        ("route-b",   "route-report",   "d34bb0bacfadee85fbea49e6b46f27b714f241c0dd778c9c301b55239cd5671f"),
        ("control-a", "control-report", "0fbba0905ceb137c7dd3f9eea23f99ba3633fde2a03459b2189fb3fdae43498d"),
        ("control-b", "control-report", "0fbba0905ceb137c7dd3f9eea23f99ba3633fde2a03459b2189fb3fdae43498d"),
    };

    private static string ReceiptPath()
        => TestPaths.Fixture("provenance", "determinism-receipt.sample.json");

    private static byte[] ReceiptBytes() => File.ReadAllBytes(ReceiptPath());

    private static RunReceipt Receipt() => RunReceipt.FromJson(ReceiptBytes());

    private static string ExpectedSubjectDigest()
        => Convert.ToHexString(SHA256.HashData(ReceiptBytes())).ToLowerInvariant();

    private static string Serialize()
        => DeterminismAttestation.SerializeStatementJson(ReceiptBytes(), Receipt());

    // =====================================================================
    // (1) DETERMINISTIC — the canonical serializer is a byte-stable pre-image.
    // =====================================================================

    // Tests INV-006/INV-010 [unit]: two independent serializations of the SAME receipt bytes
    // return a byte-IDENTICAL, NON-EMPTY JSON string. The verifier reconstructs from the same
    // bytes and requires byte-equality with the signed payload (INV-010) — so any non-
    // determinism (map ordering, whitespace, culture) here makes INV-010 unattainable. Each
    // call re-parses the receipt (fresh RunReceipt object identity) to prove the output does
    // NOT depend on object identity or member ordering. RED: stub returns "" -> NotEmpty fails.
    [Fact]
    public void Serialization_is_deterministic_byte_identical_across_calls()
    {
        string a = DeterminismAttestation.SerializeStatementJson(ReceiptBytes(), RunReceipt.FromJson(ReceiptBytes()));
        string b = DeterminismAttestation.SerializeStatementJson(ReceiptBytes(), RunReceipt.FromJson(ReceiptBytes()));

        // NON-EMPTY guard first: without it, "" == "" would be a vacuous GREEN on the stub.
        Assert.False(string.IsNullOrEmpty(a),
            "INV-010: the canonical Statement serializer must produce a non-empty byte pre-image.");
        Assert.Equal(a, b);
    }

    // =====================================================================
    // (2) FROZEN — the JSON carries the pinned predicate-type URI + subject name.
    // =====================================================================

    // Tests INV-006 [unit]: the serialized Statement is a valid in-toto Statement/v1 whose
    // `predicateType` is the frozen Corrected determinism URI and whose single subject `name`
    // is the canonical `determinism-run-receipt`. Parsed (not substring-matched) so structure
    // is asserted, not mere presence. RED: "" is not parseable JSON -> throws/fails.
    [Fact]
    public void Serialized_statement_carries_the_frozen_predicate_type_and_subject_name()
    {
        using JsonDocument doc = JsonDocument.Parse(Serialize());
        JsonElement root = doc.RootElement;

        Assert.Equal(StatementTypeV1, root.GetProperty("_type").GetString());
        Assert.Equal(PredicateTypeUri, root.GetProperty("predicateType").GetString());

        JsonElement subjects = root.GetProperty("subject");
        Assert.Equal(JsonValueKind.Array, subjects.ValueKind);
        Assert.Equal(1, subjects.GetArrayLength()); // exactly ONE subject (INV-006)
        Assert.Equal(SubjectName, subjects[0].GetProperty("name").GetString());

        // The pinned literals in this test MUST match the production single-source constants.
        Assert.Equal(DeterminismAttestation.PredicateTypeUri, PredicateTypeUri);
        Assert.Equal(DeterminismAttestation.SubjectName, SubjectName);
    }

    // =====================================================================
    // (3) BINDING — the subject sha256 is SHA-256 of the EXACT receipt bytes.
    // =====================================================================

    // Tests INV-006 (b)/INV-010 [unit]: the serialized subject digest is
    // Convert.ToHexString(SHA256(receiptBytes)).ToLowerInvariant() — the exact pre-image the
    // T3 verifier recomputes from the committed receipt bytes. An independent BCL SHA-256 is
    // the oracle. RED: "" not parseable -> fails.
    [Fact]
    public void Serialized_subject_sha256_binds_to_the_exact_receipt_bytes()
    {
        using JsonDocument doc = JsonDocument.Parse(Serialize());
        string subjectSha = doc.RootElement
            .GetProperty("subject")[0]
            .GetProperty("digest")
            .GetProperty("sha256")
            .GetString()!;

        Assert.Matches("^[0-9a-f]{64}$", subjectSha);
        Assert.Equal(ExpectedSubjectDigest(), subjectSha);
    }

    // =====================================================================
    // (4) PREDICATE — references the receipt digest + embeds the run1 projection facts.
    // =====================================================================

    // Tests INV-006 [unit]: the predicate REFERENCES the subject by its sha256
    // (receiptDigest == subject sha256) and EMBEDS the typed per-role projection facts from
    // run1_evidence (one per role, correct role->kind, EXACT per-role projection digest, never
    // the volatile raw reports). A constant-stamped digest yields < 3 distinct and fails. RED:
    // "" not parseable -> fails.
    [Fact]
    public void Predicate_references_the_receipt_digest_and_embeds_the_projection_facts()
    {
        using JsonDocument doc = JsonDocument.Parse(Serialize());
        JsonElement root = doc.RootElement;

        string subjectSha = root.GetProperty("subject")[0].GetProperty("digest").GetProperty("sha256").GetString()!;
        JsonElement predicate = root.GetProperty("predicate");

        // REFERENCE: the predicate points at the subject by its sha256.
        Assert.Equal(subjectSha, predicate.GetProperty("receiptDigest").GetString());

        // EMBED: the five per-role projection facts.
        JsonElement facts = predicate.GetProperty("projectionFacts");
        Assert.Equal(JsonValueKind.Array, facts.ValueKind);

        var byRole = facts.EnumerateArray()
            .ToDictionary(
                f => f.GetProperty("role").GetString()!,
                f => (Kind: f.GetProperty("kind").GetString()!, Proj: f.GetProperty("projectionSha256").GetString()!));

        Assert.Equal(
            ExpectedRoleFacts.Select(f => f.Role).OrderBy(r => r).ToArray(),
            byRole.Keys.OrderBy(r => r).ToArray());

        foreach ((string role, string kind, string proj) in ExpectedRoleFacts)
        {
            Assert.True(byRole.ContainsKey(role), $"INV-006: projection fact for role '{role}' missing.");
            Assert.Equal(kind, byRole[role].Kind);
            Assert.Equal(proj, byRole[role].Proj);
        }

        // Distinctness at the KIND level (constant-stamp across all roles yields 1 -> fails).
        int distinct = byRole.Values.Select(v => v.Proj).Distinct().Count();
        Assert.True(distinct >= 3,
            $"INV-006: per-role projection digests must not be constant-stamped (got {distinct} distinct).");
    }

    // =====================================================================
    // (5) ESCAPING — the pinned URI's ':' '/' '.' survive un-escaped (no HTML/unicode drift).
    // =====================================================================

    // Tests INV-006/INV-010 [unit]: the raw serialized bytes carry the predicate-type URI
    // VERBATIM — its ':' '/' '.' are NOT HTML/unicode-escaped (no `:`, `/`, `.`).
    // A GREEN that switched to a stricter encoder (or emitted the URI escaped) would drift the
    // bytes the verifier byte-compares (INV-010), so this pins the exact wire form. RED: "" ->
    // does not contain the URI -> fails.
    [Fact]
    public void Pinned_uri_survives_unescaped_no_html_or_unicode_drift()
    {
        string json = Serialize();

        // The literal URI is present un-escaped ...
        Assert.Contains(PredicateTypeUri, json);
        // ... and NONE of its structural chars leaked out as unicode escapes.
        Assert.DoesNotContain("\\u003a", json); // ':'
        Assert.DoesNotContain("\\u003A", json);
        Assert.DoesNotContain("\\u002f", json); // '/'
        Assert.DoesNotContain("\\u002F", json);
        Assert.DoesNotContain("\\u002e", json); // '.'
        Assert.DoesNotContain("\\u002E", json);
        // Not human-indented: the payload is a byte pre-image, so no newline-separated members.
        Assert.DoesNotContain("\n  ", json);
    }
}
