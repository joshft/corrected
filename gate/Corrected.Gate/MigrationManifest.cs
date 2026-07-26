using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Corrected.Gate.Kernel;

namespace Corrected.Gate;

/// <summary>The single repo-wide migration stage, derived MECHANICALLY from committed P1.satisfied (DD-003/EXT5-01).</summary>
public enum MigrationStage
{
    /// <summary>P1.satisfied == false (evidence null): every span must equal its stage_before_sha256.</summary>
    StageA,

    /// <summary>P1.satisfied == true (evidence a registered id): every span must equal its stage_after_sha256.</summary>
    StageB,
}

/// <summary>The closed discriminated union on `kind` (DD-003/EXT9-01).</summary>
public enum MigrationRowKind
{
    /// <summary>{id, file, kind:"digest", stage_before_sha256, stage_after_sha256}.</summary>
    Digest,

    /// <summary>{id, file, kind:"structural", stage_predicate} — B1's GREEN-assigned evidence id.</summary>
    Structural,
}

/// <summary>One manifest row (DD-003 discriminated union).</summary>
public sealed class MigrationRow
{
    private MigrationRow(
        string id, string file, MigrationRowKind kind,
        string? stageBeforeSha256, string? stageAfterSha256, string? stagePredicate)
    {
        Id = id;
        File = file;
        Kind = kind;
        StageBeforeSha256 = stageBeforeSha256;
        StageAfterSha256 = stageAfterSha256;
        StagePredicate = stagePredicate;
    }

    public string Id { get; }
    public string File { get; }
    public MigrationRowKind Kind { get; }
    public string? StageBeforeSha256 { get; }
    public string? StageAfterSha256 { get; }
    public string? StagePredicate { get; }

    internal static MigrationRow Create(
        string id, string file, MigrationRowKind kind,
        string? before, string? after, string? predicate)
        => new(id, file, kind, before, after, predicate);
}

/// <summary>Result of the DD-003 consistency gate (a per-site disagreement report).</summary>
public sealed class ConsistencyResult
{
    private ConsistencyResult(bool passed, MigrationStage stage, IReadOnlyList<string> disagreeing)
    {
        Passed = passed;
        ResolvedStage = stage;
        DisagreeingSites = disagreeing;
    }

    public bool Passed { get; }
    public MigrationStage ResolvedStage { get; }

    /// <summary>Naming each disagreeing site (file:line, expected vs found) on a mixed set (RS-UX-08).</summary>
    public IReadOnlyList<string> DisagreeingSites { get; }

    internal static ConsistencyResult Create(bool passed, MigrationStage stage, IReadOnlyList<string> disagreeing)
        => new(passed, stage, disagreeing);
}

/// <summary>
/// DD-003 migration manifest + consistency gate. The manifest is the SOLE digest
/// authority; the stage is P1-derived and applied UNIFORMLY; a mixed before/after
/// set FAILS CLOSED (atomic accepted TREE STATE, EXT9-06). Also runs the finite
/// stale-literal scan.
/// </summary>
public static class MigrationManifest
{
    /// <summary>The pinned manifest path (DD-003).</summary>
    public const string ManifestPath =
        "gate/Corrected.Gate.Tests/manifests/readiness-migration-manifest.json";

    /// <summary>The pinned after-span fixture directory (DD-003 site A5, EXT8-04).</summary>
    public const string AfterSpanFixtureDir =
        "gate/Corrected.Gate.Tests/manifests/after-spans/";

    /// <summary>
    /// The finite stale-literal set (DD-003/EXT5-03), PARTITIONED by the migration stage
    /// at which each literal must become ABSENT. Stage-A-removed literals are corrected
    /// when the carrier lands (sites A3/A4/A6) so they must be absent at BOTH stages;
    /// Stage-B-removed literals change only at the P1 flip so they are legitimately
    /// present at Stage A (e.g. inside the B5 before-span) and absent only at Stage B.
    /// </summary>
    public static readonly IReadOnlyList<string> StageARemovedLiterals = new[]
    {
        "specified but unhomed",
        "rm -rf out",
        "no entrypoint YAML exists yet",
        "entrypoint YAML TBD",
        "Flagged for the ARCHITECTURE.md component table",
    };

    /// <summary>Literals corrected only at the Stage-B P1 flip (present at Stage A is OK).</summary>
    public static readonly IReadOnlyList<string> StageBRemovedLiterals = new[]
    {
        "EvaluateReadiness(blockText)",
        "BLOCKED-all-false",
        "pending DF-002",
    };

    /// <summary>The full enumerated stale-literal set (union of both stage partitions).</summary>
    public static readonly IReadOnlyList<string> KnownStaleLiterals =
        StageARemovedLiterals.Concat(StageBRemovedLiterals).ToArray();

    /// <summary>
    /// The non-normative appendix boundary — the stale-literal scan stops here so
    /// historical changelog literals under "Notes for review (not invariants)" (which
    /// legitimately record the OLD wording, e.g. a past `rm -rf out`) are not falsely
    /// flagged. The scan enforces LIVE normative prose, not the review-notes history.
    /// </summary>
    public const string NotesForReviewMarker = "## Notes for review";

    private const string ParentSpecRel = ".correctless/specs/phase-0-1-worker.md";

    /// <summary>Parse + schema-validate the manifest (closed discriminated union on kind).</summary>
    public static IReadOnlyList<MigrationRow> LoadAndValidate(string manifestJsonText)
    {
        var rows = new List<MigrationRow>();
        using var doc = JsonDocument.Parse(manifestJsonText);
        if (!doc.RootElement.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("migration manifest: missing rows array");
        }

        foreach (var r in rowsEl.EnumerateArray())
        {
            string id = RequireString(r, "id");
            string file = RequireString(r, "file");
            string kindStr = RequireString(r, "kind");

            switch (kindStr)
            {
                case "digest":
                    if (!r.TryGetProperty("stage_before_sha256", out var before)
                        || !r.TryGetProperty("stage_after_sha256", out var after))
                    {
                        throw new FormatException($"migration manifest: digest row {id} missing a sha");
                    }
                    if (r.TryGetProperty("stage_predicate", out _))
                    {
                        throw new FormatException($"migration manifest: digest row {id} carries a stage_predicate");
                    }
                    rows.Add(MigrationRow.Create(
                        id, file, MigrationRowKind.Digest, before.GetString(), after.GetString(), null));
                    break;
                case "structural":
                    if (!r.TryGetProperty("stage_predicate", out var pred))
                    {
                        throw new FormatException($"migration manifest: structural row {id} missing stage_predicate");
                    }
                    if (r.TryGetProperty("stage_after_sha256", out _))
                    {
                        throw new FormatException($"migration manifest: structural row {id} carries an after-digest");
                    }
                    rows.Add(MigrationRow.Create(
                        id, file, MigrationRowKind.Structural, null, null, pred.GetString()));
                    break;
                default:
                    throw new FormatException($"migration manifest: unknown kind '{kindStr}' for row {id}");
            }
        }

        return rows;
    }

    /// <summary>
    /// Derive the stage from the committed block's P1.satisfied and assert every
    /// anchored span matches the corresponding digest under that single stage; a
    /// mixed set fails closed, naming each disagreeing site.
    /// </summary>
    public static ConsistencyResult CheckConsistency(string repoRoot)
    {
        var disagreeing = new List<string>();
        string parentPath = Path.Combine(repoRoot, Path.Combine(ParentSpecRel.Split('/')));

        // Deny-by-default: a missing parent means the migration cannot be verified.
        if (!File.Exists(parentPath))
        {
            disagreeing.Add($"{ParentSpecRel}: parent spec missing — cannot verify migration consistency (fail-closed)");
            return ConsistencyResult.Create(false, MigrationStage.StageA, disagreeing);
        }

        string parentText = File.ReadAllText(parentPath);
        ReadinessBlock block = ReadinessBlockParser.Parse(parentText);
        bool p1Satisfied = block.Preconditions.Any(pc => pc.Id == PreconditionId.P1 && pc.Satisfied);
        MigrationStage stage = p1Satisfied ? MigrationStage.StageB : MigrationStage.StageA;

        // The manifest is the SOLE digest authority — a missing or invalid manifest
        // fails closed (never a vacuous pass).
        string manifestPath = Path.Combine(repoRoot, Path.Combine(ManifestPath.Split('/')));
        if (!File.Exists(manifestPath))
        {
            disagreeing.Add($"{ManifestPath}: migration manifest missing — fail-closed");
            return ConsistencyResult.Create(false, stage, disagreeing);
        }

        IReadOnlyList<MigrationRow> rows;
        try
        {
            rows = LoadAndValidate(File.ReadAllText(manifestPath));
        }
        catch (FormatException ex)
        {
            disagreeing.Add($"{ManifestPath}: migration manifest invalid ({ex.Message}) — fail-closed");
            return ConsistencyResult.Create(false, stage, disagreeing);
        }

        // (i) anchored-span digest check under the single P1-derived stage. A digest
        // row whose current-state anchor is ABSENT fails closed — the gate must never
        // silently "pass" a migration whose anchor it could not locate (non-vacuity).
        var anchors = DiscoverAnchors(parentText, out var duplicateAnchorIds);
        foreach (var dup in duplicateAnchorIds)
        {
            // A repeated anchor id is last-writer-wins in extraction; a decoy second pair
            // could satisfy the digest while the real (earlier) span drifts — fail closed.
            disagreeing.Add($"{ParentSpecRel}#{dup}: duplicate current-state anchor id — fail-closed (tamper)");
        }
        foreach (var row in rows)
        {
            if (row.Kind != MigrationRowKind.Digest)
            {
                continue; // structural (B1) is validated by the INV-005 cross-check, not a span
            }
            if (!anchors.TryGetValue(row.Id, out string? spanBytes))
            {
                disagreeing.Add($"{row.File}#{row.Id}: current-state anchor MISSING — fail-closed");
                continue;
            }
            string spanDigest = Sha256Utf8(spanBytes);
            string? expected = stage == MigrationStage.StageA ? row.StageBeforeSha256 : row.StageAfterSha256;
            if (!string.Equals(spanDigest, expected, StringComparison.OrdinalIgnoreCase))
            {
                disagreeing.Add($"{row.File}#{row.Id}: expected {expected}, found {spanDigest}");
            }
        }

        // (ii) finite stale-literal scan over the NORMATIVE body (excludes the
        // non-normative "Notes for review" appendix). Stage-partitioned: at Stage A only
        // the Stage-A-removed literals must be absent (the Stage-B-removed literals are
        // legitimately still present); at Stage B every known stale literal must be gone.
        // A tampered/injected SECOND appendix marker could move the NormativeBody boundary
        // up and hide stale literals below it — >1 marker fails closed (the real parent has
        // exactly one "## Notes for review" heading).
        if (AppendixMarkerCount(parentText) > 1)
        {
            disagreeing.Add($"{ParentSpecRel}: multiple '{NotesForReviewMarker}' appendix markers — fail-closed (tamper)");
        }

        string body = NormativeBody(parentText);
        IEnumerable<string> mustBeAbsent = stage == MigrationStage.StageA
            ? StageARemovedLiterals
            : StageARemovedLiterals.Concat(StageBRemovedLiterals);
        foreach (var lit in mustBeAbsent)
        {
            if (body.Contains(lit, StringComparison.Ordinal))
            {
                disagreeing.Add($"{ParentSpecRel}: stale literal present at {stage} — \"{lit}\"");
            }
        }

        return ConsistencyResult.Create(disagreeing.Count == 0, stage, disagreeing);
    }

    /// <summary>
    /// The normative body = parent text up to the "## Notes for review (not invariants)"
    /// appendix header (which records historical changelog wording and is not subject to
    /// the stale-literal scan). LF-normalized.
    /// </summary>
    /// <summary>
    /// The appendix heading, matched as a WHOLE column-0 line (a `## Notes for review…`
    /// heading, NOT a substring — so "## Notes for reviewer" does not match). A future edit
    /// injecting a SECOND such heading earlier would move the boundary; CheckConsistency
    /// fails closed on more than one occurrence (AppendixMarkerCount).
    /// </summary>
    private static readonly Regex NotesForReviewLine =
        new(@"^##\s+Notes for review\b", RegexOptions.Multiline);

    private static int AppendixMarkerCount(string parentText)
        => NotesForReviewLine.Matches(parentText.Replace("\r\n", "\n").Replace("\r", "\n")).Count;

    private static string NormativeBody(string parentText)
    {
        string norm = parentText.Replace("\r\n", "\n").Replace("\r", "\n");
        Match m = NotesForReviewLine.Match(norm);
        return m.Success ? norm.Substring(0, m.Index) : norm;
    }

    /// <summary>
    /// Discover paired start/end anchor markers and return the LF-normalized between-bytes
    /// per id. A repeated id (a second start/end pair for the same id) is recorded in
    /// <paramref name="duplicateIds"/> so the caller can fail closed (last-writer-wins
    /// extraction would otherwise let a decoy pair mask a drifted span).
    /// </summary>
    private static Dictionary<string, string> DiscoverAnchors(string text, out HashSet<string> duplicateIds)
    {
        var result = new Dictionary<string, string>();
        duplicateIds = new HashSet<string>(StringComparer.Ordinal);
        string norm = text.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] lines = norm.Split('\n');
        var startRx = new Regex(@"correctless:readiness-current-state:start\s+id=""([^""]+)""");
        var endRx = new Regex(@"correctless:readiness-current-state:end\s+id=""([^""]+)""");

        for (int i = 0; i < lines.Length; i++)
        {
            var ms = startRx.Match(lines[i]);
            if (!ms.Success)
            {
                continue;
            }
            string id = ms.Groups[1].Value;
            var between = new List<string>();
            for (int j = i + 1; j < lines.Length; j++)
            {
                var me = endRx.Match(lines[j]);
                if (me.Success && me.Groups[1].Value == id)
                {
                    if (!result.TryAdd(id, string.Join("\n", between)))
                    {
                        duplicateIds.Add(id); // a second pair for the same id — tamper
                    }
                    break;
                }
                between.Add(lines[j]);
            }
        }

        return result;
    }

    private static string Sha256Utf8(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static string RequireString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"migration manifest: missing string '{name}'");
        }
        return v.GetString()!;
    }
}
