// INV-006 (P3-specific). The attestation builder: it turns the exact run-receipt bytes
// into the pinned in-toto object graph —
//   receipt bytes -> SHA-256 subject digest -> versioned determinism predicate
//   -> Statement/v1 (one subject) -> DSSE payload.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Corrected.Provenance.InToto;

namespace Corrected.Provenance.Determinism;

/// <summary>
/// Builds the determinism in-toto attestation graph (INV-006). Owns the SHA-256
/// subject-digest computation, the Statement assembly (pinned _type / predicate-type
/// URI / single subject), and the DSSE payload wrapping.
/// </summary>
public static class DeterminismAttestation
{
    // ---- Pinned determinism contract literals (A4 mitigation: single source of ----
    // ---- truth; the Inv006 test independently re-pins these VALUES as enforcement) ----

    /// <summary>
    /// The versioned Corrected determinism predicate-type URI — the ONLY value the
    /// built Statement's <see cref="InTotoStatement.PredicateType"/> may carry.
    /// </summary>
    public const string PredicateTypeUri = "https://correctless.org/attestations/determinism/v1";

    /// <summary>
    /// The canonical, stable in-toto subject name for the signed run receipt — the
    /// ONLY value the built subject's <see cref="Subject.Name"/> may carry.
    /// </summary>
    public const string SubjectName = "determinism-run-receipt";

    // Default STJ options escape HTML-sensitive characters; the payload here carries
    // pinned URIs (':' '/' '.') and lowercase-hex digests only — none of which the
    // default encoder escapes — so the base64 payload decodes back to the literal URIs
    // and digest the DSSE-wrap test asserts. No indentation: the payload is a byte
    // pre-image, not a human artifact.
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>
    /// SHA-256 (lowercase hex) over the EXACT receipt bytes — the in-toto subject
    /// digest (INV-006 (b)). This binds the subject to the raw receipt bytes; it is
    /// NEVER a hash of a re-serialized parsed DTO.
    /// </summary>
    public static string ComputeSubjectDigest(byte[] receiptBytes)
    {
        ArgumentNullException.ThrowIfNull(receiptBytes);
        return Convert.ToHexString(SHA256.HashData(receiptBytes)).ToLowerInvariant();
    }

    /// <summary>
    /// Assemble the in-toto <c>Statement/v1</c>: pinned <c>_type</c>, pinned
    /// predicate-type URI, EXACTLY ONE subject (canonical name + sha256 over the exact
    /// receipt bytes), and the determinism predicate (references the receipt digest,
    /// embeds the typed per-role projection facts — never the volatile raw reports).
    /// </summary>
    public static InTotoStatement BuildStatement(byte[] receiptBytes, RunReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receiptBytes);
        ArgumentNullException.ThrowIfNull(receipt);

        string subjectDigest = ComputeSubjectDigest(receiptBytes);

        // EMBED the typed per-role projection facts. Both runs carry identical per-role
        // projection digests for a deterministic run, so run1 is the canonical source for
        // the (role, kind, projection_sha256) triple. Only the digest travels — never the
        // raw report bytes (INV-006 embed-vs-reference decision).
        ProjectionFact[] facts = receipt.Run1Evidence
            .Select(e => new ProjectionFact
            {
                Role = e.Role,
                Kind = e.Kind,
                ProjectionSha256 = e.ProjectionSha256,
            })
            .ToArray();

        var predicate = new DeterminismPredicate
        {
            // REFERENCE the subject by its sha256 (not by re-embedding the receipt bytes).
            ReceiptDigest = subjectDigest,
            ProjectionFacts = facts,
        };

        return new InTotoStatement
        {
            Type = InTotoStatement.StatementTypeV1,
            PredicateType = PredicateTypeUri,
            Subjects = new[]
            {
                new Subject
                {
                    Name = SubjectName,
                    Digest = new DigestSet { Sha256 = subjectDigest },
                },
            },
            Predicate = predicate,
        };
    }

    /// <summary>
    /// The SINGLE canonical byte-source for the determinism in-toto Statement JSON
    /// (INV-006/010): <c>SerializeStatement(BuildStatement(receiptBytes, receipt))</c>.
    /// BOTH the T4 signer-emit path (the producer writes <c>determinism-statement.json</c>
    /// through this method; the signer signs THAT file) AND the future T3 INV-010 verifier
    /// (which reconstructs the Statement from the committed receipt and requires
    /// BYTE-EQUALITY with the signed payload) MUST serialize through THIS method — so the
    /// signed payload and the verifier's reconstruction are byte-identical. A bash-hand-rolled
    /// Statement (a divergent serializer) would drift by a byte and break INV-010.
    /// </summary>
    public static string SerializeStatementJson(byte[] receiptBytes, RunReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receiptBytes);
        ArgumentNullException.ThrowIfNull(receipt);

        // The single canonical byte-source: the SAME private serializer BuildDsseEnvelope wraps,
        // applied to the SAME BuildStatement graph — so the emitted determinism-statement.json and
        // the (future T3) verifier's reconstruction are byte-identical (INV-006/010). The private
        // SerializeStatement's byte shape / escaping is unchanged; this only makes it reachable.
        return SerializeStatement(BuildStatement(receiptBytes, receipt));
    }

    /// <summary>
    /// Wrap a Statement as a DSSE envelope: pinned in-toto payload media type + base64
    /// of the exact Statement JSON. Signatures are empty here — real signing is a later
    /// track (INV-007/009).
    /// </summary>
    public static DsseEnvelope BuildDsseEnvelope(InTotoStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        byte[] jsonBytes = Encoding.UTF8.GetBytes(SerializeStatement(statement));

        return new DsseEnvelope
        {
            PayloadType = DsseEnvelope.InTotoJsonPayloadType,
            Payload = Convert.ToBase64String(jsonBytes),
            Signatures = Array.Empty<DsseSignature>(),
        };
    }

    /// <summary>
    /// Serialize the Statement into its in-toto JSON wire shape (<c>_type</c>,
    /// <c>subject[]</c>, <c>predicateType</c>, <c>predicate</c>). The whole graph is
    /// carried — dropping the subject or predicate would fail the DSSE-wrap depth check.
    /// </summary>
    private static string SerializeStatement(InTotoStatement statement)
    {
        var predicate = statement.Predicate as DeterminismPredicate;

        var wire = new
        {
            _type = statement.Type,
            subject = statement.Subjects
                .Select(s => new
                {
                    name = s.Name,
                    digest = new { sha256 = s.Digest.Sha256 },
                })
                .ToArray(),
            predicateType = statement.PredicateType,
            predicate = predicate is null
                ? null
                : new
                {
                    receiptDigest = predicate.ReceiptDigest,
                    projectionFacts = predicate.ProjectionFacts
                        .Select(f => new
                        {
                            role = f.Role,
                            kind = f.Kind,
                            projectionSha256 = f.ProjectionSha256,
                        })
                        .ToArray(),
                },
        };

        return JsonSerializer.Serialize(wire, PayloadOptions);
    }
}
