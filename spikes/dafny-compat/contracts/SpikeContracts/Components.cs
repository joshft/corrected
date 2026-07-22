// GREEN-phase component implementations (dafny-compat spike).
// Every acceptance-relevant error path fails closed (AP-001): no fallback to
// the optimistic verdict, no pass-through of unenforced input.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Corrected.Spike.Contracts;

/// <summary>
/// The spec-owned probe manifest, loaded from the committed versioned file
/// (manifest/probe-manifest.json). The manifest file's SHA-256 is hard-coded as
/// a TEST-SUITE constant (RS-002) and verified against this load.
/// </summary>
public sealed class ProbeManifest
{
    public int ManifestVersion { get; init; }
    public IReadOnlyList<string> MandatoryRoutes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ManifestEntry> Entries { get; init; } = Array.Empty<ManifestEntry>();

    public static ProbeManifest Load(string manifestPath)
    {
        // Fail-closed on any structural defect: the manifest is the plan of
        // record (INV-006); a malformed manifest must never yield a shrunken plan.
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"probe manifest missing at {manifestPath} (INV-006)");
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;
        var version = root.GetProperty("manifest_version").GetInt32();
        var routes = root.GetProperty("route_plan").GetProperty("mandatory_routes")
            .EnumerateArray().Select(r => r.GetString()!).ToList();
        if (routes.Count == 0)
        {
            throw new InvalidOperationException("probe manifest declares no mandatory routes — an empty plan can never satisfy equality (PRH-001)");
        }
        var entries = new List<ManifestEntry>();
        var seen = new HashSet<(string, string)>();
        foreach (var e in root.GetProperty("entries").EnumerateArray())
        {
            var entry = new ManifestEntry(
                e.GetProperty("probe").GetString()!,
                e.GetProperty("route").GetString()!,
                e.GetProperty("owner").GetString()!);
            if (!seen.Add((entry.ProbeId, entry.Route)))
            {
                throw new InvalidOperationException($"probe manifest contains duplicate composite key ({entry.ProbeId},{entry.Route})");
            }
            entries.Add(entry);
        }
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("probe manifest has no entries — an empty plan can never satisfy equality (PRH-001)");
        }
        return new ProbeManifest { ManifestVersion = version, MandatoryRoutes = routes, Entries = entries };
    }

    public static string ComputeSha256(string manifestPath)
    {
        using var stream = File.OpenRead(manifestPath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Manifest instantiation for one route: that route's entries plus the shared probes (INV-006).</summary>
    public IReadOnlyList<ManifestEntry> InstantiationFor(string route) =>
        Entries.Where(e => e.Route == route || e.Route == "shared").ToList();
}

/// <summary>
/// INV-006: the named aggregator component. Derives its expected report set
/// from the committed manifest's route plan (never from reports found on
/// disk), owns the shared probes (P02, P04), binds every consumed report to
/// the current run_id, and computes fail-closed per-route verdicts.
/// </summary>
public static class VerdictAggregator
{
    /// <summary>
    /// INV-009 (codex R3-6): compiled-in trust anchor for the evidence schema
    /// digest, independent of the on-disk schema file. Validated against the
    /// committed schema BEFORE parsing or projecting any report.
    /// </summary>
    public const string EvidenceSchemaSha256TrustAnchor =
        "c872c710dd390ff8d8050c059077d0eb7d6ef4f2352fc7bf375403014ac18509"; // = SHA-256 of schema/evidence-schema.json (registry row v2 — QA fix round 1: solver_archive_sha256 field + suite_status_mask block; v1 row a630b1aa… frozen append-only).

    public static RouteOutcome ComputeRouteVerdict(
        ProbeManifest manifest,
        string route,
        IReadOnlyCollection<ProbeResult> completedSet,
        SuiteStatus finalSuiteStatus)
    {
        // The expected set derives from the COMMITTED manifest, never from the
        // runtime-declared completed set (RS-010). Composite keys throughout (AP-006).
        var expected = manifest.InstantiationFor(route).Select(e => new ProbeKey(e.ProbeId, e.Route)).ToList();
        if (expected.Count == 0)
        {
            return new RouteOutcome(RouteState.Incomplete, null,
                new VerdictReason.PrerequisiteFailure(IncompleteCause.PrerequisiteFailure,
                    $"route '{route}' has no manifest instantiation — unknown route can never be COMPATIBLE (PRH-001)"));
        }
        var expectedSet = expected.ToHashSet();

        // Restrict to this route's partition: this route's keys plus shared.
        // Keys of the OTHER mandatory route are not this route's concern (codex
        // R4-07 non-veto); keys claiming this route/shared but unknown to the
        // manifest deny COMPATIBLE (exact equality, not superset).
        var relevant = completedSet.Where(p => p.Key.Route == route || p.Key.Route == "shared").ToList();
        if (relevant.Count == 0)
        {
            return new RouteOutcome(RouteState.Incomplete, null,
                new VerdictReason.MissingReport(route));
        }

        var seen = new HashSet<ProbeKey>();
        foreach (var p in relevant)
        {
            if (!seen.Add(p.Key))
            {
                return new RouteOutcome(RouteState.Incomplete, null,
                    new VerdictReason.MalformedReport(route, $"duplicate composite probe key {p.Key} in completed set (AP-006)"));
            }
        }

        foreach (var key in seen)
        {
            if (!expectedSet.Contains(key))
            {
                return new RouteOutcome(RouteState.Incomplete, null,
                    new VerdictReason.MalformedReport(route, $"unknown composite probe key {key} — completed set must exactly equal the manifest instantiation (INV-006)"));
            }
        }
        foreach (var key in expected)
        {
            if (!seen.Contains(key))
            {
                return new RouteOutcome(RouteState.Incomplete, null,
                    new VerdictReason.PrerequisiteFailure(IncompleteCause.MissingReport,
                        $"completed probe set is missing manifest entry {key} — plan shrinkage is denied (PRH-001)"));
            }
        }

        // Exact equality holds; now every probe must individually pass.
        var byKey = relevant.ToDictionary(p => p.Key, p => p);
        foreach (var key in expected) // MANIFEST order, never completion order (INV-013)
        {
            if (byKey[key].Status != ProbeStatus.Pass)
            {
                return new RouteOutcome(RouteState.Incomplete, null,
                    new VerdictReason.ProbeFailure(key));
            }
        }

        // codex R4-02: final_suite_status is an INPUT to the verdict formula.
        if (finalSuiteStatus != SuiteStatus.Success)
        {
            return new RouteOutcome(RouteState.Incomplete, null,
                new VerdictReason.SuiteFailure(
                    finalSuiteStatus == SuiteStatus.Failure
                        ? "final test-suite exit was failure — an all-pass probe report accompanied by a failing dotnet test is not citable as COMPATIBLE (codex R3-5)"
                        : "final test-suite status unknown — the suite phase did not run in this run mode; COMPATIBLE requires final_suite_status=success (codex R4-02)"));
        }

        return new RouteOutcome(RouteState.Compatible, null, null);
    }

    public static RunResult Aggregate(
        ProbeManifest manifest,
        string runId,
        IReadOnlyList<RouteReport> reportsFromPerformedLaunches,
        SuiteStatus finalSuiteStatus)
    {
        if (string.IsNullOrEmpty(runId))
        {
            throw new InvalidOperationException("aggregation requires the current run_id (codex F1)");
        }
        var verdicts = new Dictionary<string, RouteOutcome>();
        foreach (var route in manifest.MandatoryRoutes)
        {
            var candidates = reportsFromPerformedLaunches.Where(r => r.Route == route).ToList();
            if (candidates.Count == 0)
            {
                // A missing mandatory-route report yields INCOMPLETE (INV-006);
                // reports found on disk but not produced by performed launches
                // never reach this method (RS-010).
                verdicts[route] = new RouteOutcome(RouteState.Incomplete, null,
                    new VerdictReason.MissingReport(route));
                continue;
            }
            if (candidates.Count > 1)
            {
                verdicts[route] = new RouteOutcome(RouteState.Incomplete, null,
                    new VerdictReason.MalformedReport(route, $"multiple reports for route {route} in one run"));
                continue;
            }
            var report = candidates[0];
            if (report.RunId != runId)
            {
                // Stale report from a crashed prior run can never be aggregated (codex F1/R3-5).
                verdicts[route] = new RouteOutcome(RouteState.Incomplete, null,
                    new VerdictReason.MalformedReport(route,
                        $"report run_id '{report.RunId}' does not match the current run '{runId}' — stale reports are rejected (codex F1)"));
                continue;
            }
            // Exit/report consistency precedes the probe-set verdict (INV-013),
            // scoped to the probes the ROUTE CHILD itself owns and reports
            // (P03, P05–P12): P01 is controller-attested and P02/P04 are
            // aggregator-owned (codex F7), so a controller/shared attestation
            // failure must surface as that probe's failure by manifest order —
            // never as a phantom exit/report mismatch against a child whose
            // exit DID match its own report (QA-022(2) per-partition attribution).
            var childOwned = report.Probes.Where(p => p.Key.Route == route && p.Key.ProbeId != "P01").ToList();
            var exitOutcome = AdjudicationStateMachine.MapExit(report.ExitCode, report.Signal, report with { Probes = childOwned });
            if (exitOutcome.Reason is VerdictReason.Crash or VerdictReason.ExitReportMismatch or VerdictReason.MalformedReport)
            {
                verdicts[route] = exitOutcome;
                continue;
            }
            verdicts[route] = ComputeRouteVerdict(manifest, route, report.Probes, finalSuiteStatus);
        }
        return new RunResult(runId, verdicts, finalSuiteStatus);
    }

    /// <summary>Rejects duplicate composite keys BEFORE dictionary conversion (route plan rule).</summary>
    public static IReadOnlyDictionary<ProbeKey, ProbeResult> ToKeyedResults(IReadOnlyList<ProbeResult> raw)
    {
        var dict = new Dictionary<ProbeKey, ProbeResult>();
        foreach (var result in raw)
        {
            if (dict.ContainsKey(result.Key))
            {
                throw new InvalidOperationException(
                    $"duplicate composite probe key {result.Key} in raw report array — rejected before dictionary conversion (INV-006/AP-006)");
            }
            dict[result.Key] = result;
        }
        return dict;
    }
}

/// <summary>INV-009: evidence schema validation and the three-way deterministic projection.</summary>
public static class EvidenceSchema
{
    /// <summary>Recomputes and validates the committed schema's digest before any report parse; rejects mismatch and version reuse.</summary>
    public static void ValidateSchemaFile(string schemaPath, string registryPath)
    {
        // The compiled-in trust anchor is independent of every on-disk file
        // (codex R3-6): a substituted schema AND a colluding registry are both
        // rejected here, before anything is parsed as a schema.
        var actual = Sha256File(schemaPath);
        if (actual != VerdictAggregator.EvidenceSchemaSha256TrustAnchor)
        {
            throw new InvalidOperationException(
                $"evidence schema digest mismatch: {schemaPath} has SHA-256 {actual}, compiled-in trust anchor is " +
                $"{VerdictAggregator.EvidenceSchemaSha256TrustAnchor} — refusing to parse or project any report (INV-009/codex F11)");
        }

        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var declaredVersion = schemaDoc.RootElement.GetProperty("evidence_schema_version").GetInt32();

        using var registry = JsonDocument.Parse(File.ReadAllText(registryPath));
        var rows = registry.RootElement.GetProperty("versions").EnumerateArray()
            .Select(v => (Version: v.GetProperty("version").GetInt32(), Sha: v.GetProperty("sha256").GetString()!))
            .ToList();
        if (rows.Select(r => r.Version).Distinct().Count() != rows.Count)
        {
            throw new InvalidOperationException("schema-version registry contains duplicate version rows — append-only violation (INV-009)");
        }
        var row = rows.SingleOrDefault(r => r.Version == declaredVersion);
        if (row == default)
        {
            throw new InvalidOperationException($"schema version {declaredVersion} has no registry row (INV-009)");
        }
        if (row.Sha != actual)
        {
            throw new InvalidOperationException(
                $"schema version {declaredVersion} digest {actual} does not match its registry row {row.Sha} — a version may never be reused with a different digest (INV-009/codex R3-6)");
        }
    }

    /// <summary>Validates a report against the committed schema (closed, additionalProperties: false recursively).</summary>
    public static void ValidateReport(string reportJson, string schemaPath)
    {
        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var schema = schemaDoc.RootElement.GetProperty("report_schema");
        JsonDocument report;
        try
        {
            report = JsonDocument.Parse(reportJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"malformed report JSON: {ex.Message} (AP-009: malformed is its own state, never pass)");
        }
        using (report)
        {
            var errors = new List<string>();
            ValidateNode(report.RootElement, schema, schemaDoc.RootElement, "$", errors);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("report fails closed-schema validation: " + string.Join("; ", errors));
            }
        }
    }

    private static void ValidateNode(JsonElement instance, JsonElement schema, JsonElement schemaRoot, string path, List<string> errors)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            schema = ResolveRef(schemaRoot, reference.GetString()!);
        }
        if (schema.TryGetProperty("type", out var typeNode))
        {
            var allowed = typeNode.ValueKind == JsonValueKind.Array
                ? typeNode.EnumerateArray().Select(t => t.GetString()!).ToList()
                : new List<string> { typeNode.GetString()! };
            if (!allowed.Any(t => TypeMatches(instance, t)))
            {
                errors.Add($"{path}: kind {instance.ValueKind} not in [{string.Join(",", allowed)}]");
                return;
            }
        }
        if (schema.TryGetProperty("const", out var constNode)
            && instance.GetRawText() != constNode.GetRawText())
        {
            errors.Add($"{path}: const mismatch");
        }
        if (schema.TryGetProperty("enum", out var enumNode)
            && !enumNode.EnumerateArray().Any(v => v.GetRawText() == instance.GetRawText()))
        {
            errors.Add($"{path}: value not in enum");
        }
        if (instance.ValueKind == JsonValueKind.String)
        {
            if (schema.TryGetProperty("pattern", out var pattern)
                && !System.Text.RegularExpressions.Regex.IsMatch(instance.GetString()!, pattern.GetString()!))
            {
                errors.Add($"{path}: pattern mismatch");
            }
            if (schema.TryGetProperty("minLength", out var minLen) && instance.GetString()!.Length < minLen.GetInt32())
            {
                errors.Add($"{path}: below minLength");
            }
        }
        if (instance.ValueKind == JsonValueKind.Object)
        {
            var props = schema.TryGetProperty("properties", out var p) ? p : default;
            var patternProps = schema.TryGetProperty("patternProperties", out var pp) ? pp : default;
            var declaresShape = props.ValueKind == JsonValueKind.Object
                                || patternProps.ValueKind == JsonValueKind.Object
                                || schema.TryGetProperty("additionalProperties", out _);
            if (!declaresShape)
            {
                return; // an empty subschema ({}) constrains nothing
            }
            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var req in required.EnumerateArray())
                {
                    if (!instance.TryGetProperty(req.GetString()!, out _))
                    {
                        errors.Add($"{path}: missing required field '{req.GetString()}'");
                    }
                }
            }
            foreach (var field in instance.EnumerateObject())
            {
                JsonElement? matched = null;
                if (props.ValueKind == JsonValueKind.Object && props.TryGetProperty(field.Name, out var fieldSchema))
                {
                    matched = fieldSchema;
                }
                else if (patternProps.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pat in patternProps.EnumerateObject())
                    {
                        if (System.Text.RegularExpressions.Regex.IsMatch(field.Name, pat.Name))
                        {
                            matched = pat.Value;
                            break;
                        }
                    }
                }
                if (matched is null)
                {
                    // Closed schema: unknown fields are rejected, never ignored (INV-009).
                    errors.Add($"{path}: additional property '{field.Name}' rejected (closed schema)");
                    continue;
                }
                ValidateNode(field.Value, matched.Value, schemaRoot, $"{path}.{field.Name}", errors);
            }
        }
        if (instance.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var i = 0;
            foreach (var item in instance.EnumerateArray())
            {
                ValidateNode(item, items, schemaRoot, $"{path}[{i++}]", errors);
            }
        }
    }

    private static bool TypeMatches(JsonElement instance, string type) => type switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
        "number" => instance.ValueKind == JsonValueKind.Number,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => instance.ValueKind == JsonValueKind.Null,
        _ => false,
    };

    private static JsonElement ResolveRef(JsonElement schemaRoot, string reference)
    {
        var current = schemaRoot;
        foreach (var segment in reference.TrimStart('#', '/').Split('/'))
        {
            current = current.GetProperty(segment);
        }
        return current;
    }

    /// <summary>
    /// Builds the class-2 deterministic projection. Fails if any instance field
    /// lacks exactly one classification; canonicalizes every run-root-dependent
    /// path to the fixed token <run-root>; the run_id value must appear NOWHERE
    /// in the projection (codex R2-1).
    /// </summary>
    public static string DeterministicProjection(string reportJson, string schemaPath)
        => DeterministicProjection(reportJson, schemaPath, applySuiteStatusMask: false);

    /// <summary>
    /// QA-006: the canonical-run committed sample compares under this masked
    /// projection, dropping ONLY the schema-declared suite-status subtree
    /// (schema file's suite_status_mask.masked_class_2_fields — never a
    /// test/projection-code list). The variance-mode sample uses the full
    /// projection. Every other class-2 field must still be equal.
    /// </summary>
    public static string DeterministicProjection(string reportJson, string schemaPath, bool applySuiteStatusMask)
    {
        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var partition = schemaDoc.RootElement.GetProperty("field_partition");
        var maskedFields = new HashSet<string>(StringComparer.Ordinal);
        if (applySuiteStatusMask)
        {
            if (!schemaDoc.RootElement.TryGetProperty("suite_status_mask", out var mask))
            {
                throw new InvalidOperationException(
                    "canonical-sample masked equality requested but the schema declares no suite_status_mask block (QA-006)");
            }
            foreach (var f in mask.GetProperty("masked_class_2_fields").EnumerateArray())
            {
                maskedFields.Add(f.GetString()!);
            }
        }
        var class1 = partition.GetProperty("class_1_binding_identity").EnumerateArray().Select(f => f.GetString()!).ToHashSet();
        var class2 = partition.GetProperty("class_2_deterministic_projection").EnumerateArray().Select(f => f.GetString()!).ToHashSet();
        var class3 = partition.GetProperty("class_3_volatile").EnumerateArray().Select(f => f.GetString()!).ToHashSet();

        using var report = JsonDocument.Parse(reportJson);
        var root = report.RootElement;

        // Every instance field must carry EXACTLY ONE classification (RS-005).
        var envelopes = new HashSet<string> { "binding_identity", "deterministic", "volatile" };
        void RequireClassified(string fieldName)
        {
            var memberships = (class1.Contains(fieldName) ? 1 : 0)
                            + (class2.Contains(fieldName) ? 1 : 0)
                            + (class3.Contains(fieldName) ? 1 : 0);
            if (memberships != 1)
            {
                throw new InvalidOperationException(
                    $"projection construction failed: instance field '{fieldName}' lacks exactly one classification " +
                    $"(found {memberships}) — every field carries exactly one class (INV-009/RS-005)");
            }
        }
        foreach (var field in root.EnumerateObject())
        {
            if (!envelopes.Contains(field.Name))
            {
                RequireClassified(field.Name);
            }
        }
        foreach (var envelope in envelopes)
        {
            if (root.TryGetProperty(envelope, out var env) && env.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in env.EnumerateObject())
                {
                    RequireClassified(field.Name);
                }
            }
        }

        // Collect class-2 members from the root level and every envelope,
        // dropping the schema-declared suite-status subtree when masking (QA-006).
        var projection = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
        {
            if (!envelopes.Contains(field.Name) && class2.Contains(field.Name) && !maskedFields.Contains(field.Name))
            {
                projection[field.Name] = field.Value;
            }
        }
        foreach (var envelope in envelopes)
        {
            if (root.TryGetProperty(envelope, out var env) && env.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in env.EnumerateObject())
                {
                    if (class2.Contains(field.Name) && !maskedFields.Contains(field.Name))
                    {
                        projection[field.Name] = field.Value;
                    }
                }
            }
        }

        var canonical = CanonicalJson.Serialize(projection);

        // Canonicalize every run-root-dependent value: the concrete run
        // directory and the run_id itself must appear NOWHERE (codex R2-1).
        var replacements = new List<string>();
        if (root.TryGetProperty("binding_identity", out var binding)
            && binding.ValueKind == JsonValueKind.Object
            && binding.TryGetProperty("run_directory", out var runDir)
            && runDir.ValueKind == JsonValueKind.String)
        {
            replacements.Add(runDir.GetString()!);
        }
        if (root.TryGetProperty("run_id", out var runIdEl) && runIdEl.ValueKind == JsonValueKind.String)
        {
            var runId = runIdEl.GetString()!;
            replacements.Add("out/" + runId);
            replacements.Add(runId);
        }
        foreach (var value in replacements.Where(v => !string.IsNullOrEmpty(v)).OrderByDescending(v => v.Length))
        {
            canonical = canonical.Replace(value, "<run-root>");
        }
        return canonical;
    }

    internal static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

/// <summary>
/// Canonical serialization per the committed rule beside the schema (EA-006):
/// UTF-8, culture-invariant, LF, lexicographic (ordinal) key ordering at every
/// object level, lowercase-hex digests.
/// </summary>
public static class CanonicalJson
{
    public static string Serialize(IReadOnlyDictionary<string, JsonElement> topLevel)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            foreach (var kv in topLevel.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(kv.Key);
                WriteCanonical(writer, kv.Value);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    WriteCanonical(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                // Numbers/strings/booleans/null re-emitted from raw JSON text:
                // no culture-sensitive formatting can occur (EA-006).
                element.WriteTo(writer);
                break;
        }
    }
}

/// <summary>INV-002/P03: pass-predicate evaluation for loaded-assembly + runtime identity.</summary>
public static class P03Evaluator
{
    public static ProbeStatus Evaluate(P03Evidence evidence, ExpectedLoadedSet expected)
    {
        // codex R4-03: the pass predicate PROVES net10-on-net10; recording alone
        // is not gating. Any leg failing → Fail (never a vacuous pass, AP-010).
        if (evidence.HarnessTargetFramework != ".NETCoreApp,Version=v10.0")
        {
            return ProbeStatus.Fail;
        }
        if (evidence.RuntimeMajorVersion != 10)
        {
            return ProbeStatus.Fail;
        }
        if (evidence.RuntimeConfigFrameworkName != "Microsoft.NETCore.App")
        {
            return ProbeStatus.Fail;
        }
        if (evidence.RuntimeConfigRollForward != "LatestPatch")
        {
            return ProbeStatus.Fail;
        }
        // Selected CoreLib/hostfxr must come from the shared-framework layout
        // of a real dotnet root — never a run-root or output-tree location.
        var corelib = evidence.CoreLibPath.Replace('\\', '/');
        if (!corelib.Contains("/shared/Microsoft.NETCore.App") || corelib.StartsWith("<run-root>", StringComparison.Ordinal))
        {
            return ProbeStatus.Fail;
        }
        var hostfxr = evidence.HostfxrPath.Replace('\\', '/');
        if (!hostfxr.Contains("hostfxr") || hostfxr.StartsWith("<run-root>", StringComparison.Ordinal))
        {
            return ProbeStatus.Fail;
        }
        if (!IsSha256(evidence.CoreLibSha256) || !IsSha256(evidence.HostfxrSha256))
        {
            return ProbeStatus.Fail;
        }

        // Loaded-set EQUALITY over (simple name, file SHA-256) — not subset
        // (INV-002/RS-008). An uncaptured expected digest can never gate.
        if (expected.Assemblies.Count == 0)
        {
            return ProbeStatus.Fail;
        }
        var expectedSet = new HashSet<(string, string)>();
        foreach (var asm in expected.Assemblies)
        {
            if (string.IsNullOrEmpty(asm.FileSha256) || !IsSha256(asm.FileSha256))
            {
                return ProbeStatus.Fail;
            }
            expectedSet.Add((asm.SimpleName, asm.FileSha256));
        }
        var actualSet = evidence.LoadedAssemblies
            .Select(a => (a.SimpleName, a.FileSha256))
            .ToHashSet();
        if (!expectedSet.SetEquals(actualSet))
        {
            return ProbeStatus.Fail;
        }
        // Mandatory route anchors observed loaded (INV-002).
        foreach (var anchor in expected.Anchors)
        {
            if (!evidence.LoadedAssemblies.Any(a => a.SimpleName == anchor))
            {
                return ProbeStatus.Fail;
            }
        }
        return ProbeStatus.Pass;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>
/// INV-013: the mechanical adjudication state machine. Terminal transitions
/// each require a schema-valid adjudication record; harness faults terminate at
/// INCOMPLETE, never any INCOMPATIBLE.
/// </summary>
public static class AdjudicationStateMachine
{
    public static RouteOutcome ClassifyEnvironmentFault(IncompleteCause cause, string detail)
    {
        // Environment/prerequisite faults are never any INCOMPATIBLE (INV-013).
        return new RouteOutcome(RouteState.Incomplete, null,
            new VerdictReason.PrerequisiteFailure(cause, detail));
    }

    public static RouteOutcome EnterInProcessFailure(UnadjudicatedInProcessFailure payload)
    {
        if (payload.IdentityEvidence is null)
        {
            throw new InvalidOperationException(
                "unadjudicated-failure records must embed the run's P01–P04 identity evidence (INV-013)");
        }
        return new RouteOutcome(RouteState.UnadjudicatedInProcessFailure, null,
            new VerdictReason.UnadjudicatedFailure(payload));
    }

    public static RouteOutcome TerminalTransition(UnadjudicatedInProcessFailure payload, AdjudicationRecord record)
    {
        if (string.IsNullOrEmpty(record.AdjudicationRecordId) || string.IsNullOrEmpty(record.Route))
        {
            throw new InvalidOperationException("terminal transitions require a schema-valid adjudication record (INV-013)");
        }
        switch (record.TerminalState)
        {
            case RouteState.Incomplete:
                // Harness fault → INCOMPLETE, never an INCOMPATIBLE of any kind.
                return new RouteOutcome(RouteState.Incomplete, null, new VerdictReason.Adjudicated(record));

            case RouteState.UpstreamDefect:
                if (string.IsNullOrEmpty(record.SourceCitation))
                {
                    throw new InvalidOperationException(
                        "UPSTREAM_DEFECT requires a source/upstream cross-check citation in the adjudication record (INV-013)");
                }
                return new RouteOutcome(RouteState.UpstreamDefect, null, new VerdictReason.Adjudicated(record));

            case RouteState.Incompatible:
                return TerminalIncompatible(payload, record);

            default:
                throw new InvalidOperationException(
                    $"'{record.TerminalState}' is not a terminal adjudication state (INV-013 transition table)");
        }
    }

    private static RouteOutcome TerminalIncompatible(UnadjudicatedInProcessFailure payload, AdjudicationRecord record)
    {
        switch (record.IncompatibleClass)
        {
            case Contracts.IncompatibleClass.OfficialApiCapabilityGap:
                // codex R4-01: reachable ONLY through the source-verified
                // terminal transition — never a direct classification.
                if (string.IsNullOrEmpty(record.SourceCitation))
                {
                    throw new InvalidOperationException(
                        "OFFICIAL_API_CAPABILITY_GAP requires a source-verified citation — a record without one is rejected (codex R4-01)");
                }
                if (string.IsNullOrEmpty(record.MinimizedReproducerDigest))
                {
                    throw new InvalidOperationException(
                        "ADR-citable transitions require a minimized official-surface reproducer (INV-013)");
                }
                return new RouteOutcome(RouteState.Incompatible, Contracts.IncompatibleClass.OfficialApiCapabilityGap,
                    new VerdictReason.Adjudicated(record));

            case Contracts.IncompatibleClass.HostRuntimeIncompatibility:
            {
                var cells = RequireThreeCell(record);
                if (cells.Net8AssetOnNet8Host != ProbeStatus.Pass
                    || cells.Net10AssetOnNet10Host == ProbeStatus.Pass
                    || cells.Net8AssetOnNet10Host != ProbeStatus.Fail)
                {
                    throw new InvalidOperationException(
                        "HOST_RUNTIME_INCOMPATIBILITY requires net8-pass + same-bits-fail-on-net10-host cell outcomes (codex R3-2)");
                }
                // codex R4-04: BOTH net10-host failure cells must share the
                // minimized reproducer AND an equivalent typed failure
                // fingerprint; unrelated failures remain unadjudicated.
                var fingerprintsMatch =
                    !string.IsNullOrEmpty(cells.Net10CellFailureFingerprint)
                    && cells.Net10CellFailureFingerprint == cells.Net8BitsOnNet10FailureFingerprint
                    && !string.IsNullOrEmpty(cells.MinimizedReproducerDigestNet10Cell)
                    && cells.MinimizedReproducerDigestNet10Cell == cells.MinimizedReproducerDigestNet8BitsCell;
                if (!fingerprintsMatch)
                {
                    return new RouteOutcome(RouteState.UnadjudicatedInProcessFailure, null,
                        new VerdictReason.UnadjudicatedFailure(payload));
                }
                RequireAdrCitable(record);
                return new RouteOutcome(RouteState.Incompatible, Contracts.IncompatibleClass.HostRuntimeIncompatibility,
                    new VerdictReason.Adjudicated(record));
            }

            case Contracts.IncompatibleClass.TargetFrameworkIncompatibility:
            {
                var cells = RequireThreeCell(record);
                // codex R3-2: net8-bits-PASS-on-net10 but net10-target-fail — a distinct class.
                if (cells.Net8AssetOnNet8Host != ProbeStatus.Pass
                    || cells.Net8AssetOnNet10Host != ProbeStatus.Pass
                    || cells.Net10AssetOnNet10Host != ProbeStatus.Fail)
                {
                    throw new InvalidOperationException(
                        "TARGET_FRAMEWORK_INCOMPATIBILITY requires net8-bits-pass-on-net10 with net10-target-fail (codex R3-2)");
                }
                RequireAdrCitable(record);
                return new RouteOutcome(RouteState.Incompatible, Contracts.IncompatibleClass.TargetFrameworkIncompatibility,
                    new VerdictReason.Adjudicated(record));
            }

            default:
                throw new InvalidOperationException("INCOMPATIBLE terminal transitions require a concrete incompatibility class (INV-013)");
        }
    }

    private static ThreeCellOutcome RequireThreeCell(AdjudicationRecord record)
    {
        return record.ThreeCell
            ?? throw new InvalidOperationException(
                "host/TFM adjudication requires the three-cell experiment outcome in the record (INV-013/codex R3-2)");
    }

    private static void RequireAdrCitable(AdjudicationRecord record)
    {
        if (string.IsNullOrEmpty(record.SourceCitation)
            || string.IsNullOrEmpty(record.MinimizedReproducerDigest)
            || string.IsNullOrEmpty(record.ControlAssetDigest))
        {
            throw new InvalidOperationException(
                "ADR-citable transitions require a minimized reproducer, a control-asset digest, and a source/upstream cross-check (INV-013)");
        }
    }

    /// <summary>Maps a child-process exit (code or signal) + report presence to the committed exit/report consistency table.</summary>
    public static RouteOutcome MapExit(int? exitCode, string? signal, RouteReport? report)
    {
        var route = report?.Route ?? "unknown";
        // Precedence (committed): signal death / unknown code → crash, never ambiguously (codex F7).
        if (!string.IsNullOrEmpty(signal))
        {
            return new RouteOutcome(RouteState.Incomplete, null, new VerdictReason.Crash(exitCode, signal));
        }
        if (exitCode is not (ExitCodes.RouteProbesPassed or ExitCodes.ProbeFailure or ExitCodes.Incomplete))
        {
            return new RouteOutcome(RouteState.Incomplete, null, new VerdictReason.Crash(exitCode, null));
        }
        if (report is null)
        {
            // Missing reports carry their OWN variant — they never collapse into
            // crash and have no "first failing probe" (codex R3-4).
            return new RouteOutcome(RouteState.Incomplete, null, new VerdictReason.MissingReport(route));
        }

        var anyFailed = report.Probes.Any(p => p.Status != ProbeStatus.Pass);
        switch (exitCode)
        {
            case ExitCodes.RouteProbesPassed when !anyFailed:
                return new RouteOutcome(RouteState.Compatible, null, null); // consistent; the probe-set verdict still applies
            case ExitCodes.RouteProbesPassed:
                return new RouteOutcome(RouteState.Incomplete, null,
                    new VerdictReason.ExitReportMismatch(exitCode.Value, "success exit with a failing probe in the report"));
            case ExitCodes.ProbeFailure when anyFailed:
            {
                var firstFailing = report.Probes
                    .Where(p => p.Status != ProbeStatus.Pass)
                    .OrderBy(p => p.Key.ProbeId, StringComparer.Ordinal)
                    .ThenBy(p => p.Key.Route, StringComparer.Ordinal)
                    .First();
                return new RouteOutcome(RouteState.Incomplete, null, new VerdictReason.ProbeFailure(firstFailing.Key));
            }
            case ExitCodes.ProbeFailure:
                // A failure exit with an all-pass report is never COMPATIBLE (codex R2-5/R3-4).
                return new RouteOutcome(RouteState.Incomplete, null,
                    new VerdictReason.ExitReportMismatch(exitCode.Value, "failure exit code with an all-pass report"));
            default:
                return new RouteOutcome(RouteState.Incomplete, null,
                    new VerdictReason.PrerequisiteFailure(IncompleteCause.PrerequisiteFailure,
                        "child exited INCOMPLETE with a typed cause in its report"));
        }
    }
}

/// <summary>
/// INV-013 (PAT-004): the ADR linter — a mechanism that runs. Operates on
/// mandatory machine-readable fields (boundary_decision, route,
/// adjudication_record_id) and validates positive selection claims as well as
/// rejection claims against schema-valid terminal adjudication records.
/// </summary>
public static class AdrLinter
{
    public static IReadOnlyList<string> Lint(string adrPath, IReadOnlyList<AdjudicationRecord> records)
    {
        var findings = new List<string>();
        if (!File.Exists(adrPath))
        {
            findings.Add($"ADR file missing at {adrPath}");
            return findings;
        }
        var block = ExtractLintBlock(File.ReadAllText(adrPath));
        if (block is null)
        {
            findings.Add("ADR carries no machine-readable adr_lint block (INV-013: closed vocabulary, not prose detection)");
            return findings;
        }
        var (decision, selectedRoute, routes) = block.Value;

        if (decision is not ("pending" or "in-process-selected" or "rejected"))
        {
            findings.Add($"boundary_decision '{decision}' is outside the closed vocabulary (pending | in-process-selected | rejected)");
        }

        var recordById = new Dictionary<string, AdjudicationRecord>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            recordById[record.AdjudicationRecordId] = record;
        }

        foreach (var route in routes)
        {
            var verdict = route.Verdict;
            if (verdict == "pending")
            {
                continue; // an undecided route claims nothing and needs no record
            }
            if (verdict.StartsWith("INCOMPATIBLE", StringComparison.Ordinal) || verdict == "UPSTREAM_DEFECT")
            {
                // Rejection claims: schema-valid terminal adjudication record mandatory (INV-013).
                if (string.IsNullOrEmpty(route.AdjudicationRecordId) || route.AdjudicationRecordId == "null")
                {
                    findings.Add($"route {route.Route}: verdict {verdict} lacks an adjudication_record_id — boundary-rejection language without a schema-valid terminal adjudication record (INV-013)");
                }
                else if (!recordById.ContainsKey(route.AdjudicationRecordId))
                {
                    findings.Add($"route {route.Route}: adjudication_record_id '{route.AdjudicationRecordId}' matches no schema-valid adjudication record");
                }
                if (string.IsNullOrEmpty(route.Evidence) || route.Evidence == "null")
                {
                    findings.Add($"route {route.Route}: verdict {verdict} cites no committed evidence path (INV-014: one citation per claim)");
                }
            }
            else if (verdict == "COMPATIBLE")
            {
                // codex R4-01/R4-02: POSITIVE claims are validated too.
                if (string.IsNullOrEmpty(route.Evidence) || route.Evidence == "null")
                {
                    findings.Add($"route {route.Route}: positive COMPATIBLE claim cites no committed evidence path (codex R4-02)");
                }
                if (string.IsNullOrEmpty(route.AdjudicationRecordId) || route.AdjudicationRecordId == "null")
                {
                    findings.Add($"route {route.Route}: positive COMPATIBLE claim lacks an adjudication_record_id anchoring it to run records (codex R4-01)");
                }
                else if (!recordById.ContainsKey(route.AdjudicationRecordId))
                {
                    findings.Add($"route {route.Route}: adjudication_record_id '{route.AdjudicationRecordId}' matches no schema-valid adjudication record");
                }
            }
            else if (verdict != "INCOMPLETE")
            {
                findings.Add($"route {route.Route}: verdict '{verdict}' is outside the committed route-outcome algebra");
            }
        }

        if (decision == "in-process-selected")
        {
            if (string.IsNullOrEmpty(selectedRoute) || selectedRoute == "null")
            {
                findings.Add("in-process-selected requires selected_route (DD-001/DD-007)");
            }
            else if (!routes.Any(r => r.Route == selectedRoute && r.Verdict == "COMPATIBLE"))
            {
                findings.Add($"selected route {selectedRoute} is not COMPATIBLE — Phase 0.1 gating requires the ADR-selected route to be fully COMPATIBLE (DD-001/DD-007)");
            }
        }
        if (decision == "rejected" && !routes.Any(r => r.Verdict.StartsWith("INCOMPATIBLE", StringComparison.Ordinal)))
        {
            findings.Add("boundary_decision rejected without any INCOMPATIBLE route verdict (INV-013)");
        }
        return findings;
    }

    private sealed record RouteClaim(string Route, string Verdict, string? AdjudicationRecordId, string? Evidence);

    private static (string Decision, string? SelectedRoute, List<RouteClaim> Routes)? ExtractLintBlock(string adrText)
    {
        var lines = adrText.Split('\n');
        var inYaml = false;
        var inBlock = false;
        string decision = "";
        string? selectedRoute = null;
        var routes = new List<RouteClaim>();
        string? curRoute = null, curVerdict = null, curRecordId = null, curEvidence = null;
        var sawBlock = false;

        void FlushRoute()
        {
            if (curRoute is not null)
            {
                routes.Add(new RouteClaim(curRoute, curVerdict ?? "pending", curRecordId, curEvidence));
            }
            curRoute = curVerdict = curRecordId = curEvidence = null;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inYaml && inBlock)
                {
                    break;
                }
                inYaml = !inYaml;
                continue;
            }
            if (!inYaml)
            {
                continue;
            }
            var stripped = StripComment(line).TrimEnd();
            if (stripped.TrimStart().Length == 0)
            {
                continue;
            }
            if (stripped.TrimStart().StartsWith("adr_lint:", StringComparison.Ordinal))
            {
                inBlock = true;
                sawBlock = true;
                continue;
            }
            if (!inBlock)
            {
                continue;
            }
            var t = stripped.TrimStart();
            if (t.StartsWith("boundary_decision:", StringComparison.Ordinal))
            {
                decision = Value(t, "boundary_decision:");
            }
            else if (t.StartsWith("selected_route:", StringComparison.Ordinal))
            {
                selectedRoute = Value(t, "selected_route:");
            }
            else if (t.StartsWith("- route:", StringComparison.Ordinal))
            {
                FlushRoute();
                curRoute = Value(t, "- route:");
            }
            else if (t.StartsWith("verdict:", StringComparison.Ordinal))
            {
                curVerdict = Value(t, "verdict:");
            }
            else if (t.StartsWith("adjudication_record_id:", StringComparison.Ordinal))
            {
                curRecordId = Value(t, "adjudication_record_id:");
            }
            else if (t.StartsWith("evidence:", StringComparison.Ordinal))
            {
                curEvidence = Value(t, "evidence:");
            }
        }
        FlushRoute();
        return sawBlock ? (decision, selectedRoute, routes) : null;
    }

    private static string StripComment(string line)
    {
        var idx = line.IndexOf(" #", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    private static string Value(string line, string key) =>
        line[key.Length..].Trim().Trim('"', '\'');
}

/// <summary>
/// INV-003/RS-003c: ONE shared, unit-tested function builds the solver-path
/// option for every probe (sentinel and real runs alike), narrowing the
/// sentinel→real-run composition gap.
/// </summary>
public static class SolverPathOptionBuilder
{
    public static IReadOnlyList<string> Build(string solverAbsolutePath)
    {
        if (string.IsNullOrWhiteSpace(solverAbsolutePath))
        {
            throw new ArgumentException("solver path must be explicit — ambient discovery is prohibited (INV-003)", nameof(solverAbsolutePath));
        }
        return new[] { "--solver-path", solverAbsolutePath };
    }
}

/// <summary>
/// PRH-004 layer 2: the managed launcher for post-build processes — route
/// harness executables, the net8 control host, the provisioned z3 identity
/// check, and the sentinel stub. The only sanctioned Process.Start site in the
/// spike (source-scan enforced). Honors LaunchRequest.KillAfterMs by delivering
/// SIGKILL externally (TA-A9).
/// </summary>
public static class ManagedLauncher
{
    public static LaunchResult Launch(LaunchRequest request)
    {
        // Structured argv only — never string-interpolated command lines and
        // never a shell layer (AP-008).
        var psi = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in request.Argv)
        {
            psi.ArgumentList.Add(arg);
        }
        // The child environment is CONSTRUCTED from the provided profile, never
        // ambient-inherited (EA-008).
        psi.Environment.Clear();
        foreach (var kv in request.EnvironmentProfile)
        {
            psi.Environment[kv.Key] = kv.Value;
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (stdout) { stdout.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (stderr) { stderr.AppendLine(e.Data); } } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        string? signal = null;
        if (request.KillAfterMs is { } killAfterMs)
        {
            // TA-A9: the LAUNCHER (not the SUT) delivers SIGKILL after the
            // delay — the child never learns it is under test.
            if (process.WaitForExit((int)killAfterMs))
            {
                // Child finished before the kill window closed.
            }
            else
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    signal = "SIGKILL";
                }
                catch (InvalidOperationException)
                {
                    // Exited between the wait and the kill.
                }
            }
        }

        if (!process.WaitForExit((int)TimeSpan.FromSeconds(request.TimeoutSeconds).TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                signal ??= "SIGKILL";
            }
            catch (InvalidOperationException)
            {
            }
        }
        process.WaitForExit(); // drain async output
        stopwatch.Stop();

        int? exitCode = null;
        try
        {
            var raw = process.ExitCode;
            // On Unix, signal termination surfaces as 128+signum; a delivered
            // SIGKILL is reported as signal death, never as a prescribed code.
            if (signal is null && raw > 128)
            {
                signal = raw switch
                {
                    137 => "SIGKILL",
                    143 => "SIGTERM",
                    _ => $"SIG{raw - 128}",
                };
            }
            else if (signal is null)
            {
                exitCode = raw;
            }
        }
        catch (InvalidOperationException)
        {
        }

        string outText, errText;
        lock (stdout) { outText = stdout.ToString(); }
        lock (stderr) { errText = stderr.ToString(); }
        return new LaunchResult(exitCode, signal, outText, errText, stopwatch.Elapsed.TotalMilliseconds);
    }
}

/// <summary>
/// INV-013/BND-004 (codex R2-2, TA-B12): control-equivalence proof — digest
/// equality of normalized source, resolved package graph, options, fixtures,
/// and compiler inputs between the main asset and its control counterpart.
/// </summary>
public static class ControlEquivalence
{
    /// <summary>Returns normally when every keyed digest matches; throws a typed mismatch (never a silent bool) naming the first divergent key otherwise.</summary>
    public static void Prove(
        IReadOnlyDictionary<string, string> mainInputDigests,
        IReadOnlyDictionary<string, string> controlInputDigests)
    {
        if (mainInputDigests.Count == 0)
        {
            throw new InvalidOperationException("control equivalence over an empty input set proves nothing (AP-010)");
        }
        // Cardinality/key-set equality asserted before value comparison (AP-006).
        foreach (var key in mainInputDigests.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!controlInputDigests.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"control-equivalence mismatch: control input set is missing key '{key}' (codex R2-2)");
            }
        }
        foreach (var key in controlInputDigests.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!mainInputDigests.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"control-equivalence mismatch: unexpected control input key '{key}' (codex R2-2)");
            }
        }
        foreach (var key in mainInputDigests.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (mainInputDigests[key] != controlInputDigests[key])
            {
                throw new InvalidOperationException(
                    $"control-equivalence mismatch at key '{key}': main {mainInputDigests[key]} != control {controlInputDigests[key]} (codex R2-2/TA-B12)");
            }
        }
    }
}
