// INV-006 (P3-specific). The RunReceipt is the signed in-toto SUBJECT: a pinned-schema
// record that carries execution_status, comparison_status, the per-role/kind evidence,
// the platform identity, attested_commit, AND (INV-006 (c)) the determinism-subject-
// manifest digest + policy version. No attestation / verification / probe status lives
// inside it (that is INV-001/012, outside the subject). This shape mirrors the real PR1
// producer receipt (determinism-receipt.json).
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Corrected.Provenance.Determinism;

/// <summary>
/// The determinism run receipt — the exact bytes of this record are the SHA-256
/// pre-image for the in-toto subject digest (INV-006 (b)). GREEN implements
/// <see cref="FromJson"/>; the RED stub returns an empty receipt.
/// </summary>
public sealed class RunReceipt
{
    public string ExecutionStatus { get; init; } = "";

    public string ComparisonStatus { get; init; } = "";

    public string AttestedCommit { get; init; } = "";

    /// <summary>INV-006 (c): the determinism-subject-manifest digest bound INTO the signed receipt.</summary>
    public string SubjectManifestDigest { get; init; } = "";

    /// <summary>INV-006 (c): the projection/subject policy version bound INTO the signed receipt.</summary>
    public string PolicyVersion { get; init; } = "";

    public PlatformIdentity Platform { get; init; } = new();

    public IReadOnlyList<RoleEvidence> Run1Evidence { get; init; } = Array.Empty<RoleEvidence>();

    public IReadOnlyList<RoleEvidence> Run2Evidence { get; init; } = Array.Empty<RoleEvidence>();

    /// <summary>
    /// Parse the receipt JSON bytes into the typed DTO. The pre-image bytes handed here
    /// are the SAME bytes hashed for the subject digest (INV-006 (b)); this parse is a
    /// READ-ONLY projection of them into typed fields — it never re-serializes the receipt
    /// nor feeds the parsed DTO back into the subject digest.
    /// </summary>
    /// <remarks>
    /// Snake_case receipt keys are mapped explicitly (not via a naming policy) so the
    /// mapping is auditable and robust to numeric-segment edge cases (e.g. raw_sha256).
    /// Missing/typed-mismatched fields fall back to the DTO's zero-values; malformed JSON
    /// throws (a determinism receipt that will not parse is not a valid signed subject).
    /// </remarks>
    public static RunReceipt FromJson(byte[] receiptBytes)
    {
        ArgumentNullException.ThrowIfNull(receiptBytes);

        using JsonDocument doc = JsonDocument.Parse(receiptBytes);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("determinism receipt root must be a JSON object");
        }

        return new RunReceipt
        {
            ExecutionStatus = GetString(root, "execution_status"),
            ComparisonStatus = GetString(root, "comparison_status"),
            AttestedCommit = GetString(root, "attested_commit"),
            SubjectManifestDigest = GetString(root, "subject_manifest_digest"),
            PolicyVersion = GetString(root, "policy_version"),
            Platform = ParsePlatform(root),
            Run1Evidence = ParseEvidence(root, "run1_evidence"),
            Run2Evidence = ParseEvidence(root, "run2_evidence"),
        };
    }

    private static PlatformIdentity ParsePlatform(JsonElement root)
    {
        if (!root.TryGetProperty("platform", out JsonElement p) || p.ValueKind != JsonValueKind.Object)
        {
            return new PlatformIdentity();
        }

        return new PlatformIdentity
        {
            ProcessorCount = GetInt(p, "processor_count"),
            Rid = GetString(p, "rid"),
            OsLabel = GetString(p, "os_label"),
            RunnerImage = GetString(p, "runner_image"),
            Kernel = GetString(p, "kernel"),
            Architecture = GetString(p, "architecture"),
            ResolvedSdk = GetString(p, "resolved_sdk"),
        };
    }

    private static IReadOnlyList<RoleEvidence> ParseEvidence(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RoleEvidence>();
        }

        var list = new List<RoleEvidence>(arr.GetArrayLength());
        foreach (JsonElement e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            list.Add(new RoleEvidence
            {
                Role = GetString(e, "role"),
                Kind = GetString(e, "kind"),
                RepoRelativeName = GetString(e, "repo_relative_name"),
                RawSha256 = GetString(e, "raw_sha256"),
                ProjectionSha256 = GetString(e, "projection_sha256"),
                ProjectionSchemaId = GetString(e, "projection_schema_id"),
                ProjectionSchemaVersion = GetInt(e, "projection_schema_version"),
                ProjectionSchemaDigest = GetString(e, "projection_schema_digest"),
                CanonicalizationVersion = GetString(e, "canonicalization_version"),
                PerRoleVerdict = GetString(e, "per_role_verdict"),
                ProjectionImplDigest = GetString(e, "projection_impl_digest"),
            });
        }

        return list;
    }

    /// <summary>
    /// Read a scalar as a string. A JSON string yields its value; a JSON number yields
    /// its raw text (policy_version is authored as "1" but tolerating a bare 1 is safe);
    /// anything else (or absent) yields "".
    /// </summary>
    private static string GetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement el))
        {
            return "";
        }

        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Number => el.GetRawText(),
            _ => "",
        };
    }

    private static int GetInt(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out JsonElement el)
            && el.ValueKind == JsonValueKind.Number
            && el.TryGetInt32(out int value))
        {
            return value;
        }

        return 0;
    }
}

/// <summary>The recorded platform identity (INV-005) carried in the receipt.</summary>
public sealed class PlatformIdentity
{
    public int ProcessorCount { get; init; }

    public string Rid { get; init; } = "";

    public string OsLabel { get; init; } = "";

    public string RunnerImage { get; init; } = "";

    public string Kernel { get; init; } = "";

    public string Architecture { get; init; } = "";

    public string ResolvedSdk { get; init; } = "";
}

/// <summary>Per-run × per-role evidence row carried in the receipt.</summary>
public sealed class RoleEvidence
{
    public string Role { get; init; } = "";

    public string Kind { get; init; } = "";

    public string RepoRelativeName { get; init; } = "";

    public string RawSha256 { get; init; } = "";

    public string ProjectionSha256 { get; init; } = "";

    public string ProjectionSchemaId { get; init; } = "";

    public int ProjectionSchemaVersion { get; init; }

    public string ProjectionSchemaDigest { get; init; } = "";

    public string CanonicalizationVersion { get; init; } = "";

    public string PerRoleVerdict { get; init; } = "";

    public string ProjectionImplDigest { get; init; } = "";
}
