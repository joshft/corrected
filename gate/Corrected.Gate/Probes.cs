using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Corrected.Gate.Kernel;
using Corrected.Provenance.Determinism;

namespace Corrected.Gate;

/// <summary>
/// The typed fail-closed reason taxonomy (INV-006 / RS-UX-01). Distinguishes a
/// degraded env / pre-migration from a real regression.
/// </summary>
public static class ProbeReasons
{
    public const string EvidenceAbsent = "evidence-absent";
    public const string EvidenceMalformed = "evidence-malformed";
    public const string EvidenceRefutes = "evidence-refutes";
    public const string EvidenceSchemaIncomplete = "evidence-schema-incomplete";
    public const string ValidatorDeferred = "validator-deferred";
    public const string EvidenceSchemaMismatch = "evidence-schema-mismatch";
    public const string EvidenceSchemaNewerThanPinned = "evidence-schema-newer-than-pinned; bump the gate pin";

    /// <summary>
    /// P3 zero-state (INV-010/012 RS-035): the committed determinism-attestation pointer does not
    /// exist yet (pre-PR3). Rendered distinctly but classified fail-closed — P3 stays false.
    /// </summary>
    public const string P3NotYetActivated = "p3-not-yet-activated";
}

/// <summary>
/// Injectable gate context (INV-013). Production binds the repo-root pinned
/// constant via <see cref="RepoRootLocator"/>; the degraded-env test injects a
/// temp-tree copy so the real probe is driven off an alternate root WITHOUT any
/// process-global state (no Directory.GetCurrentDirectory / ambient env).
/// </summary>
public sealed class GateContext
{
    private GateContext(
        string repoRoot,
        IReadOnlyList<string>? adrRegistryOverride,
        string? cosignBinPath,
        string? trustRootPath)
    {
        RepoRoot = repoRoot;
        AdrRegistryOverride = adrRegistryOverride;
        CosignBinPath = cosignBinPath;
        TrustRootPath = trustRootPath;
    }

    public string RepoRoot { get; }

    /// <summary>
    /// Test-only injectable ADR registry override for the INV-008(a‴) supersession
    /// fixtures. Null on the production factory (which binds the compiled registry const).
    /// </summary>
    public IReadOnlyList<string>? AdrRegistryOverride { get; }

    /// <summary>
    /// Injected cosign binary path (the COSIGN_BIN seam) for the P3 present-valid verify path
    /// (INV-010/MA-B wiring). Null on the production factory — <see cref="P3Probe"/> then falls back
    /// to the <c>COSIGN_BIN</c> environment variable the gate exports.
    /// </summary>
    public string? CosignBinPath { get; }

    /// <summary>
    /// Injected trust-root path for the P3 present-valid verify path. Null on the production factory
    /// — <see cref="P3Probe"/> then falls back to the <c>TRUSTED_ROOT</c> environment variable.
    /// </summary>
    public string? TrustRootPath { get; }

    /// <summary>Test-only injectable-repo-root factory (structurally test-only, RS-271/AP-003).</summary>
    public static GateContext ForRepoRoot(string repoRoot) => new(repoRoot, null, null, null);

    /// <summary>
    /// Test-only injectable-repo-root + synthesized-ADR-registry factory for the
    /// INV-008(a‴) supersession-graph fixtures (EXT4-02/EXT4-07).
    /// </summary>
    public static GateContext ForRepoRootWithAdrRegistry(
        string repoRoot, IReadOnlyList<string> adrRegistry) => new(repoRoot, adrRegistry, null, null);

    /// <summary>
    /// Test/CI factory that also injects the cosign + trust-root seam for the P3 present-valid
    /// verify path (INV-010/MA-B). Production uses <see cref="ForRepoRoot"/> and the env fallback.
    /// </summary>
    public static GateContext ForRepoRootWithVerify(
        string repoRoot, string cosignBinPath, string trustRootPath)
        => new(repoRoot, null, cosignBinPath, trustRootPath);
}

/// <summary>
/// Resolves the repo root via the NAMED committed sentinel = the directory
/// containing BOTH the repo-root global.json (INV-016) AND the .correctless/
/// directory (INV-001 RS-A-04/RS-264) — never the dotnet test cwd.
/// </summary>
public static class RepoRootLocator
{
    public static string Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "global.json"))
                && Directory.Exists(Path.Combine(dir.FullName, ".correctless")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "repo-root sentinel (dir with global.json + .correctless/) not found");
    }
}

/// <summary>A single evidence probe (INV-006). Never throws/skips.</summary>
public interface IEvidenceProbe
{
    PreconditionId Id { get; }

    /// <summary>Runs the real probe over the real committed artifacts under context.RepoRoot.</summary>
    ProbeResult Evaluate(GateContext context);
}

/// <summary>
/// P1 probe — HARDENED ADR promotion + component-table (INV-008). Resolves true
/// only after the DD-003 Stage-B migration; pre-migration it returns typed
/// evidence-schema-incomplete false via the status-key-absent short-circuit.
/// </summary>
public sealed class P1Probe : IEvidenceProbe
{
    // The compiled ADR registry (R3-B4b) — every ADR file carrying an adr_lint block.
    private static readonly string[] DefaultAdrRegistry = { "ADR-0001" };

    private const string AdrRel = "docs/adr/ADR-0001-dafny-integration-boundary.md";
    private const string AdrDirRel = "docs/adr";
    private const string SampleRel = "spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json";
    private const string SchemaRel = "spikes/dafny-compat/schema/evidence-schema.json";
    private const string ManifestRel = "spikes/dafny-compat/manifest/probe-manifest.json";
    private const string RouteARel = "spikes/dafny-compat/manifest/expected-loaded/route-a.json";
    private const string ArchRel = ".correctless/ARCHITECTURE.md";

    public PreconditionId Id => PreconditionId.P1;

    public ProbeResult Evaluate(GateContext context)
    {
        string reason = EvaluateReason(context);
        bool satisfied = reason == "OK";
        var rr = ReferenceResolution.Resolved;
        return ProbeResult.TryCreate(satisfied, satisfied ? "resolved-compatible" : reason, rr)!;
    }

    private string EvaluateReason(GateContext context)
    {
        string root = context.RepoRoot;
        string Abs(string rel) => Path.Combine(root, Path.Combine(rel.Split('/')));

        // (a) Hardened ADR parse + schema-completeness short-circuit (global gate, R3-B1).
        string adrPath = Abs(AdrRel);
        if (!File.Exists(adrPath))
        {
            return ProbeReasons.EvidenceAbsent;
        }

        AdrParseResult adrResult = AdrLintBlockParser.Parse(File.ReadAllText(adrPath));
        if (adrResult.Outcome == AdrParseOutcome.EvidenceMalformed)
        {
            return ProbeReasons.EvidenceMalformed;
        }
        if (adrResult.Outcome == AdrParseOutcome.EvidenceSchemaIncomplete)
        {
            // The benign pre-migration case — short-circuit BEFORE any evidence /
            // recompute / supersession check so a stale sha const cannot dead-red Stage A.
            return ProbeReasons.EvidenceSchemaIncomplete;
        }

        AdrLintBlock adr = adrResult.Block!;

        // (a) step 3 — authoritative decision fields.
        if (adr.BoundaryDecision != "in-process-selected" || adr.SelectedRoute != "A")
        {
            return ProbeReasons.EvidenceRefutes;
        }
        AdrRoute? routeA = adr.Routes.FirstOrDefault(r => r.Route == "A");
        if (routeA is null || routeA.Verdict != "COMPATIBLE")
        {
            return ProbeReasons.EvidenceRefutes;
        }

        // prose<->machine status consistency (multi-line parenthetical token, R3-M2).
        string? proseToken = ExtractProseStatusToken(File.ReadAllText(adrPath));
        if (proseToken is null || !string.Equals(proseToken, adr.Status, StringComparison.Ordinal))
        {
            return ProbeReasons.EvidenceRefutes;
        }

        // (a′) pinned canonical evidence + compiled content anchors.
        string samplePath = Abs(SampleRel);
        string schemaPath = Abs(SchemaRel);
        string manifestPath = Abs(ManifestRel);
        string routeAPath = Abs(RouteARel);
        string archPath = Abs(ArchRel);
        if (!File.Exists(samplePath) || !File.Exists(schemaPath) || !File.Exists(manifestPath)
            || !File.Exists(routeAPath) || !File.Exists(archPath))
        {
            return ProbeReasons.EvidenceAbsent;
        }

        if (Sha256File(samplePath) != P1EvidenceAnchors.CanonicalSampleSha256)
        {
            return ProbeReasons.EvidenceMalformed;
        }
        if (Sha256File(schemaPath) != P1EvidenceAnchors.EvidenceSchemaSha256)
        {
            return ProbeReasons.EvidenceMalformed;
        }
        if (Sha256File(manifestPath) != P1EvidenceAnchors.ProbeManifestSha256)
        {
            return ProbeReasons.EvidenceMalformed;
        }

        JsonDocument sampleDoc;
        JsonDocument manifestDoc;
        try
        {
            sampleDoc = JsonDocument.Parse(File.ReadAllBytes(samplePath));
            manifestDoc = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        }
        catch (JsonException)
        {
            return ProbeReasons.EvidenceMalformed;
        }

        using (sampleDoc)
        using (manifestDoc)
        {
            JsonElement rootEl = sampleDoc.RootElement;

            // evidence-schema version + cross-field sha checks.
            if (!rootEl.TryGetProperty("evidence_schema_version", out var verEl)
                || verEl.ValueKind != JsonValueKind.Number)
            {
                return ProbeReasons.EvidenceMalformed;
            }
            int version = verEl.GetInt32();
            if (!P1EvidenceAnchors.RecognizedSchemaVersions.Contains(version))
            {
                return ProbeReasons.EvidenceSchemaMismatch;
            }
            if (GetString(rootEl, "evidence_schema_sha256") != P1EvidenceAnchors.EvidenceSchemaSha256
                || GetString(rootEl, "probe_manifest_sha256") != P1EvidenceAnchors.ProbeManifestSha256)
            {
                return ProbeReasons.EvidenceMalformed;
            }

            // (a″) sound COMPATIBLE recompute over the Route-A + shared partition.
            if (!rootEl.TryGetProperty("deterministic", out var det))
            {
                return ProbeReasons.EvidenceMalformed;
            }

            RecomputeVerdict recompute = RecomputeFromSample(det, manifestDoc.RootElement);
            if (recompute != RecomputeVerdict.Compatible)
            {
                return ProbeReasons.EvidenceMalformed;
            }

            // additional deterministic assertions.
            if (GetString(det, "final_suite_status") != "success"
                || GetString(det, "exit_report_matrix_outcome") != "consistent")
            {
                return ProbeReasons.EvidenceMalformed;
            }
        }

        // (a‴) supersession over a registry set-equality.
        IReadOnlyList<string> registry = context.AdrRegistryOverride ?? DefaultAdrRegistry;
        string supersessionReason = CheckSupersession(root, registry);
        if (supersessionReason != "OK")
        {
            return supersessionReason;
        }

        // (b) component table — propagation equality.
        if (!ComponentTableOk(routeAPath, archPath))
        {
            return ProbeReasons.EvidenceRefutes;
        }

        return "OK";
    }

    private static RecomputeVerdict RecomputeFromSample(JsonElement det, JsonElement manifestRoot)
    {
        var actual = new List<(string, string, string)>();
        if (det.TryGetProperty("per_probe_results", out var ppr) && ppr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in ppr.EnumerateArray())
            {
                string route = GetString(e, "route") ?? "";
                if (route != "A" && route != "shared")
                {
                    continue;
                }
                actual.Add((GetString(e, "probe") ?? "", route, GetString(e, "status") ?? ""));
            }
        }

        var expected = new List<(string, string)>();
        if (manifestRoot.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in entries.EnumerateArray())
            {
                string route = GetString(e, "route") ?? "";
                if (route != "A" && route != "shared")
                {
                    continue;
                }
                expected.Add((GetString(e, "probe") ?? "", route));
            }
        }

        var routeVerdicts = new List<(string, string)>();
        if (det.TryGetProperty("route_verdicts", out var rv) && rv.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in rv.EnumerateArray())
            {
                routeVerdicts.Add((GetString(e, "route") ?? "", GetString(e, "state") ?? ""));
            }
        }

        return P1Recompute.RecomputeRouteACompatible(actual, expected, routeVerdicts);
    }

    // ---- (a‴) supersession graph over the compiled registry ----

    private string CheckSupersession(string root, IReadOnlyList<string> registry)
    {
        var discovered = DiscoverAdrLintNodes(root);

        // Registry set-equality: an unregistered on-disk adr_lint block fails closed.
        var registrySet = new HashSet<string>(registry);
        var discoveredSet = new HashSet<string>(discovered.Keys);
        if (!registrySet.SetEquals(discoveredSet))
        {
            return ProbeReasons.EvidenceMalformed;
        }

        const string rootId = "ADR-0001";
        if (!discovered.TryGetValue(rootId, out var rootBlock) || rootBlock is null)
        {
            return ProbeReasons.EvidenceMalformed;
        }

        // status vocabulary + provisional ban.
        foreach (var (_, b) in discovered)
        {
            if (b is null || b.Status is not ("accepted" or "superseded"))
            {
                return ProbeReasons.EvidenceMalformed;
            }
        }

        // edges: reciprocity + no dangling target.
        foreach (var (id, b) in discovered)
        {
            if (b!.SupersededBy is { } sb)
            {
                if (!discovered.TryGetValue(sb, out var target) || target is null
                    || target.Supersedes != id)
                {
                    return ProbeReasons.EvidenceMalformed;
                }
            }
            if (b.Supersedes is { } su)
            {
                if (!discovered.TryGetValue(su, out var src) || src is null
                    || src.SupersededBy != id)
                {
                    return ProbeReasons.EvidenceMalformed;
                }
            }
            // an accepted node MUST NOT have a non-null successor (should be superseded).
            if (b.Status == "accepted" && b.SupersededBy is not null)
            {
                return ProbeReasons.EvidenceMalformed;
            }
        }

        // exactly one accepted node TOTAL, which is the Route-A terminal.
        var accepted = discovered.Where(kv => kv.Value!.Status == "accepted").ToList();
        if (accepted.Count != 1)
        {
            return ProbeReasons.EvidenceMalformed;
        }
        AdrLintBlock terminal = accepted[0].Value!;
        if (terminal.SupersededBy is not null || terminal.SelectedRoute != "A")
        {
            return ProbeReasons.EvidenceMalformed;
        }

        // every non-terminal node MUST be superseded.
        foreach (var kv in discovered)
        {
            if (!ReferenceEquals(kv.Value, terminal) && kv.Value!.Status != "superseded")
            {
                return ProbeReasons.EvidenceMalformed;
            }
        }

        // reachability of every node from the ADR-0001 root (follow both edge directions).
        var reachable = new HashSet<string> { rootId };
        var queue = new Queue<string>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            string cur = queue.Dequeue();
            var b = discovered[cur]!;
            foreach (var next in new[] { b.SupersededBy, b.Supersedes })
            {
                if (next is not null && discovered.ContainsKey(next) && reachable.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }
        if (reachable.Count != discovered.Count)
        {
            return ProbeReasons.EvidenceMalformed;
        }

        return "OK";
    }

    private Dictionary<string, AdrLintBlock?> DiscoverAdrLintNodes(string root)
    {
        var nodes = new Dictionary<string, AdrLintBlock?>();
        string adrDir = Path.Combine(root, Path.Combine(AdrDirRel.Split('/')));
        if (!Directory.Exists(adrDir))
        {
            return nodes;
        }

        foreach (var file in Directory.EnumerateFiles(adrDir, "*.md"))
        {
            string text = File.ReadAllText(file);
            // count column-0 adr_lint fences (INV-001-D discovery).
            if (!Regex.IsMatch(text, @"(?m)^adr_lint:\s*$"))
            {
                continue;
            }
            AdrParseResult pr = AdrLintBlockParser.Parse(text);
            Match m = Regex.Match(Path.GetFileName(file), @"ADR-\d+");
            string id = m.Success ? m.Value : Path.GetFileNameWithoutExtension(file);
            nodes[id] = pr.Block;
        }

        return nodes;
    }

    // ---- (b) component-table propagation equality ----

    private static bool ComponentTableOk(string routeAPath, string archPath)
    {
        // INV-008(b): the loaded Dafny family (from route-a.json) must EXACTLY equal the
        // expected set. Detection is by the "Dafny" NAME PREFIX so a ROGUE Dafny*-named
        // assembly is swept in and BREAKS the set-equality — i.e. FAIL-CLOSED. route-a.json
        // is NOT independently sha-pinned, so this check must be self-contained fail-safe:
        // an exact family-set filter would silently IGNORE a rogue Dafny* (QA-004 asked for
        // exact membership, but that is fail-OPEN here — QA r2 F2, reverted). NB: the
        // legitimately-loaded Boogie.* verifier backend is NOT "Dafny"-prefixed and is
        // therefore correctly EXCLUDED from the family set (route-a.json ships 12 Boogie.*).
        HashSet<string> expected = new(StringComparer.Ordinal) { "DafnyCore", "DafnyDriver", "DafnyLanguageServer" };

        HashSet<string> dafnySet;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(routeAPath));
            dafnySet = doc.RootElement.GetProperty("assemblies").EnumerateArray()
                .Select(a => GetString(a, "simple_name"))
                .Where(n => n is not null && n.StartsWith("Dafny", StringComparison.Ordinal))
                .Select(n => n!)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }

        if (!dafnySet.SetEquals(expected) || dafnySet.Contains("DafnyPipeline"))
        {
            return false;
        }

        HashSet<string> archLoaded = ExtractArchDafnyLoaded(File.ReadAllText(archPath));
        return archLoaded.SetEquals(dafnySet);
    }

    private static HashSet<string> ExtractArchDafnyLoaded(string archText)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        string[] lines = archText.Replace("\r\n", "\n").Split('\n');
        bool inLoaded = false;
        foreach (var raw in lines)
        {
            string line = raw.TrimEnd();
            if (Regex.IsMatch(line, @"^\s*dafny_family_loaded:\s*$"))
            {
                inLoaded = true;
                continue;
            }
            if (inLoaded)
            {
                var m = Regex.Match(line, @"^\s*-\s*(\S+)");
                if (m.Success)
                {
                    result.Add(m.Groups[1].Value);
                }
                else if (line.Trim().Length > 0)
                {
                    inLoaded = false; // left the list
                }
            }
        }
        return result;
    }

    // ---- helpers ----

    private static string? ExtractProseStatusToken(string adrText)
    {
        Match m = Regex.Match(adrText, @"\*\*Status\*\*:\s*([A-Za-z]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Sha256File(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}

/// <summary>P2 probe — fail-closed validator-deferred unconditionally (INV-009).</summary>
public sealed class P2Probe : IEvidenceProbe
{
    public PreconditionId Id => PreconditionId.P2;

    public ProbeResult Evaluate(GateContext context)
        => ProbeResult.TryCreate(false, ProbeReasons.ValidatorDeferred, ReferenceResolution.Resolved)!;
}

/// <summary>
/// P3 probe — the REAL fail-closed determinism-attestation verifier (INV-010/012/018/019, RS-025).
/// It resolves the pinned durable pointer <see cref="ProbeOrchestrator.P3AttestationPath"/> under
/// <c>context.RepoRoot</c>: an ABSENT pointer is the expected pre-PR3 zero-state
/// (<c>p3-not-yet-activated</c>, satisfied:false); a PRESENT pointer is parsed into the closed
/// pointer schema, resolved to the committed {receipt, bundle}, and handed to
/// <see cref="DeterminismVerifier.Verify"/> with the REAL gate-side inputs — subject-manifest
/// staleness (INV-019, <see cref="SubjectManifestProducer"/> over
/// <see cref="SubjectClassificationPolicy.Pinned"/>) and attested-commit ancestry
/// (<see cref="GitAncestry"/>). A malformed/dangling pointer fails closed with a typed carrier
/// reason. The production pointer is ABSENT in the repo, so the real gate never reaches the verify
/// path — P3 stays false / readiness BLOCKED (MA-B: the present-valid branch is wired, not a stub).
/// </summary>
public sealed class P3Probe : IEvidenceProbe
{
    /// <summary>The pinned production platform RID (EA-003) the receipt's rid must equal.</summary>
    private const string ExpectedRid = "linux-x64";

    public PreconditionId Id => PreconditionId.P3;

    public ProbeResult Evaluate(GateContext context)
    {
        // Resolve the pinned pointer under the injected repo root (never the dotnet test cwd).
        string pointer = Path.Combine(
            context.RepoRoot, Path.Combine(ProbeOrchestrator.P3AttestationPath.Split('/')));

        // ABSENT pointer = the expected pre-PR3 zero-state. Fail closed (satisfied:false) with the
        // distinct p3-not-yet-activated reason — NOT the old validator-deferred stub.
        if (!File.Exists(pointer))
        {
            return ProbeResult.TryCreate(
                false, ProbeReasons.P3NotYetActivated, ReferenceResolution.Resolved)!;
        }

        // PRESENT pointer -> the real verify path. A probe never throws (IEvidenceProbe contract):
        // any internal fault fails closed with a typed carrier reason.
        try
        {
            return EvaluatePresentPointer(context, pointer);
        }
        catch (Exception)
        {
            return Reject(DeterminismVerifyReason.UnclassifiedVerifierFault);
        }
    }

    private static ProbeResult EvaluatePresentPointer(GateContext context, string pointerPath)
    {
        string repoRoot = context.RepoRoot;

        // 1) Parse the minimal pointer JSON. A malformed pointer fails closed (malformed-bundle).
        (PointerSchema.PointerDocument? doc, _) =
            PointerSchema.ParsePointerJson(File.ReadAllBytes(pointerPath));
        if (doc is null)
        {
            return Reject(DeterminismVerifyReason.MalformedBundle);
        }

        PointerFamily? family = PointerSchema.FamilyFromWire(doc.Family);
        if (family is null)
        {
            return Reject(DeterminismVerifyReason.MalformedBundle);
        }

        // 2) Build the closed-schema descriptor + validate against the committed-path set.
        string root = PointerSchema.FixedRoot(family.Value)!;
        string onDiskSegment = FirstSegmentUnderRoot(doc.Receipt, root);
        string receiptAbs = Path.Combine(repoRoot, Path.Combine(doc.Receipt.Split('/')));
        string bundleAbs = Path.Combine(repoRoot, Path.Combine(doc.Bundle.Split('/')));
        bool symlinked = IsSymlink(receiptAbs) || IsSymlink(bundleAbs);

        var descriptor = new PointerDescriptor(
            family.Value, new[] { doc.Receipt }, new[] { doc.Bundle },
            doc.AttestedCommit, onDiskSegment, symlinked);

        var committed = new HashSet<string>(
            SubjectManifestProducer.EnumerateRepoFiles(repoRoot), StringComparer.Ordinal);
        PointerValidation validation = PointerSchema.ValidatePointer(descriptor, committed);
        if (!validation.Valid)
        {
            // A dangling pointer (named target not committed) is evidence-absent; every other
            // closed-schema violation (bad path / symlink / cardinality / commit-dir) is malformed.
            return validation.Reason.StartsWith("dangling", StringComparison.Ordinal)
                ? Reject(DeterminismVerifyReason.EvidenceAbsent)
                : Reject(DeterminismVerifyReason.MalformedBundle);
        }

        // 3) Resolve the cosign + trust-root seam (injected, else the gate-exported env). A missing
        // seam is a fail-closed verifier-unavailable (retryable), never a silent accept.
        string? cosignBin = context.CosignBinPath ?? Environment.GetEnvironmentVariable("COSIGN_BIN");
        string? trustRoot = context.TrustRootPath ?? Environment.GetEnvironmentVariable("TRUSTED_ROOT");
        if (string.IsNullOrEmpty(cosignBin) || string.IsNullOrEmpty(trustRoot))
        {
            return DeterminismVerifier.ToProbeResult(new DeterminismVerifyResult(
                DeterminismVerifyOutcome.Unavailable, DeterminismVerifyReason.VerifierUnavailable));
        }

        // 4) Read the receipt for the gate-side inputs (staleness + ancestry). A malformed receipt
        // fails closed.
        RunReceipt receipt;
        try
        {
            receipt = RunReceipt.FromJson(File.ReadAllBytes(receiptAbs));
        }
        catch (Exception)
        {
            return Reject(DeterminismVerifyReason.MalformedReceipt);
        }

        bool manifestStale = SubjectManifestProducer.IsStale(
            receipt.SubjectManifestDigest, SubjectClassificationPolicy.Pinned, repoRoot);
        AncestryStatus ancestry = GitAncestry.Classify(repoRoot, receipt.AttestedCommit);

        // 5) Run the real verifier with the resolved inputs; bridge to a typed carrier ProbeResult.
        var request = new DeterminismVerifyRequest
        {
            CosignBinPath = cosignBin,
            BundlePath = bundleAbs,
            ReceiptPath = receiptAbs,
            TrustRootPath = trustRoot,
            WorkingDirectory = repoRoot,
            ExpectedRid = ExpectedRid,
            Identity = DeterminismVerifyIdentity.Production,
            CertWorkflowSha = null,
            AttestedCommitAncestry = ancestry,
            ManifestStale = manifestStale,
        };

        return DeterminismVerifier.ToProbeResult(DeterminismVerifier.Verify(request));
    }

    private static ProbeResult Reject(DeterminismVerifyReason reason)
        => DeterminismVerifier.ToProbeResult(new DeterminismVerifyResult(
            DeterminismVerifyOutcome.Rejected, reason));

    /// <summary>The first path segment after <paramref name="root"/> (the &lt;commit&gt; dir), or "" if not under the root.</summary>
    private static string FirstSegmentUnderRoot(string repoRelativePath, string root)
    {
        if (!repoRelativePath.StartsWith(root, StringComparison.Ordinal))
        {
            return string.Empty;
        }
        string rest = repoRelativePath.Substring(root.Length);
        int slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest.Substring(0, slash);
    }

    private static bool IsSymlink(string absPath)
    {
        try
        {
            var info = new FileInfo(absPath);
            return info.Exists && info.LinkTarget is not null;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Runs the REAL P1/P2/P3 probes on the real committed artifacts (nothing mocked;
/// INV-006) and produces the ProbeResult map + referenceResolution for the kernel.
/// </summary>
public static class ProbeOrchestrator
{
    public static IReadOnlyDictionary<PreconditionId, ProbeResult> RunAll(GateContext context)
    {
        var probes = new IEvidenceProbe[] { new P1Probe(), new P2Probe(), new P3Probe() };
        var map = new Dictionary<PreconditionId, ProbeResult>();
        foreach (var probe in probes)
        {
            map[probe.Id] = probe.Evaluate(context);
        }
        return map;
    }

    /// <summary>The pinned canonical P1 evidence sample path (DD-002).</summary>
    public const string CanonicalSamplePath =
        "spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json";

    /// <summary>The pinned P2 completion-manifest path (DD-002).</summary>
    public const string P2ManifestPath = "test/manifests/phase-0.0-completion.json";

    /// <summary>The pinned P3 determinism-attestation path (DD-002).</summary>
    public const string P3AttestationPath = "test/attestations/inv010-determinism.json";

    /// <summary>
    /// The pinned durable ENTRY-ACTIVATION pointer path (INV-030 / Group G, MA-C part e). ABSENT in
    /// PR2 (Group G dormant, the committed readiness block is v1) so <see cref="EntryIntegrityProbe"/>
    /// resolves the pre-entry zero-state <see cref="EntryIntegrity.Absent"/> — the src/ ban stays
    /// active and readiness stays BLOCKED. A P2 activation would emit this pointer at the entry commit.
    /// </summary>
    public const string EntryActivationPointerPath = "test/attestations/entry-activation.json";
}
