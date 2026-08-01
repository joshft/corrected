// INV-030 (P3 phase-entry, Group G / MA-C). The SINGLE canonical byte-source for the entry in-toto
// Statement JSON — the entry analog of DeterminismAttestation.SerializeStatementJson. BOTH the entry
// signer-emit path (part b: the producer writes entry-statement.json through this serializer; the
// signer signs THAT file) AND the gate-side EntryVerifier (which decodes the SIGNED DSSE payload and
// PARSES it back into the typed graph to run EntryAttestation.ValidateEntrySchema) go through THIS
// codec — so the signed payload and the verifier's parse are the same wire shape. A bash-hand-rolled
// entry Statement (a divergent serializer) would drift and break the round-trip.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Corrected.Provenance.InToto;

namespace Corrected.Provenance.Entry;

/// <summary>
/// The canonical entry in-toto Statement codec (INV-030): serialize a built entry
/// <see cref="InTotoStatement"/> to its pinned wire JSON, and parse that wire JSON back into the
/// typed graph (a Statement whose <c>Predicate</c> is an <see cref="EntryPredicate"/>). The two are
/// a matched pair; the round-trip <c>ValidateEntrySchema(Parse(Serialize(built)))</c> must hold.
/// </summary>
public static class EntryStatementCodec
{
    // Defensive parse cap (MA-C self-audit #3): a post-cosign-Ok payload is bounded to the read cap
    // (~48 MiB decoded), but a compromised/fake cosign — the very threat the verifier's subject re-bind
    // guards — could still hand a payload with a huge array to amplify the object graph. Cap each array
    // enumeration far above any real entry statement (4 subjects, 3 preconditions, tiny closures); a
    // payload that exceeds the cap is TRUNCATED, after which ValidateEntrySchema rejects on cardinality
    // (fail-closed) — never an accept. Bounds the transient graph without rejecting any legitimate input.
    private const int MaxArrayElements = 100_000;

    // Default STJ options escape HTML-sensitive characters; the entry payload carries pinned URIs
    // (':' '/' '.') + repo-relative paths + lowercase-hex digests only — none of which the default
    // encoder escapes — so the wire round-trips to the literal values. No indentation: a byte
    // pre-image, not a human artifact. Kept byte-identical to the test's independent oracle.
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>
    /// Serialize the entry Statement to its canonical wire JSON (UTF-8, no BOM, no indentation).
    /// Wire shape: <c>_type</c>, <c>predicateType</c>, <c>subject[]</c> (name + digest.sha256, in the
    /// built order), <c>predicate</c> (<c>commitX</c> + <c>preconditions[]</c>, each <c>precondition</c>
    /// + <c>manifest[]</c> of path/sha256).
    /// </summary>
    public static string SerializeEntryStatementJson(InTotoStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var predicate = statement.Predicate as EntryPredicate;

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
            predicate = predicate is null
                ? null
                : new
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

        return JsonSerializer.Serialize(wire, WireOptions);
    }

    /// <summary>
    /// Parse the canonical entry Statement wire JSON bytes into the typed graph, fail-closed. Returns
    /// the reconstructed <see cref="InTotoStatement"/> (with an <see cref="EntryPredicate"/>) on
    /// success, else <c>(null, error)</c> for malformed JSON, a non-object root, or a structurally-
    /// missing field. NEVER throws (catches every exception). The reconstruction is intentionally
    /// PERMISSIVE about content — <see cref="EntryAttestation.ValidateEntrySchema"/> is the authority
    /// on shape/binding — so a well-formed-but-invalid statement parses (non-null) and is REJECTED by
    /// the validator, never silently dropped here.
    /// </summary>
    public static (InTotoStatement? Statement, string? Error) ParseEntryStatement(byte[] payloadJson)
    {
        if (payloadJson is null)
        {
            return (null, "null payload bytes");
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, "entry statement root is not a JSON object");
            }

            var statement = new InTotoStatement
            {
                Type = GetString(root, "_type"),
                PredicateType = GetString(root, "predicateType"),
                Subjects = ParseSubjects(root),
                Predicate = ParsePredicate(root),
            };
            return (statement, null);
        }
        catch (JsonException ex)
        {
            return (null, "entry statement is not valid JSON: " + ex.GetType().Name);
        }
        catch (Exception ex)
        {
            return (null, "entry statement parse failed: " + ex.GetType().Name);
        }
    }

    private static IReadOnlyList<Subject> ParseSubjects(JsonElement root)
    {
        if (!root.TryGetProperty("subject", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Subject>();
        }

        var list = new List<Subject>();
        foreach (JsonElement s in arr.EnumerateArray())
        {
            if (list.Count >= MaxArrayElements)
            {
                break; // truncate a hostile oversize array; ValidateEntrySchema then rejects on cardinality.
            }
            if (s.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string sha256 = string.Empty;
            if (s.TryGetProperty("digest", out JsonElement d) && d.ValueKind == JsonValueKind.Object)
            {
                sha256 = GetString(d, "sha256");
            }

            list.Add(new Subject
            {
                Name = GetString(s, "name"),
                Digest = new DigestSet { Sha256 = sha256 },
            });
        }

        return list;
    }

    private static EntryPredicate? ParsePredicate(JsonElement root)
    {
        if (!root.TryGetProperty("predicate", out JsonElement p) || p.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var preconditions = new List<PreconditionClosure>();
        if (p.TryGetProperty("preconditions", out JsonElement pre) && pre.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement pc in pre.EnumerateArray())
            {
                if (preconditions.Count >= MaxArrayElements)
                {
                    break; // truncate a hostile oversize array; ValidateEntrySchema then rejects on cardinality.
                }
                if (pc.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                preconditions.Add(new PreconditionClosure
                {
                    Precondition = GetString(pc, "precondition"),
                    Manifest = ParseManifest(pc),
                });
            }
        }

        return new EntryPredicate
        {
            CommitX = GetString(p, "commitX"),
            Preconditions = preconditions,
        };
    }

    private static IReadOnlyList<ClosureDigest> ParseManifest(JsonElement precondition)
    {
        if (!precondition.TryGetProperty("manifest", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ClosureDigest>();
        }

        var list = new List<ClosureDigest>();
        foreach (JsonElement m in arr.EnumerateArray())
        {
            if (list.Count >= MaxArrayElements)
            {
                break; // truncate a hostile oversize array; ValidateEntrySchema then rejects on cardinality.
            }
            if (m.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            list.Add(new ClosureDigest
            {
                Path = GetString(m, "path"),
                Sha256 = GetString(m, "sha256"),
            });
        }

        return list;
    }

    /// <summary>Read a string property, or "" when absent / non-string (defensive; the validator gates content).</summary>
    private static string GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;
}
