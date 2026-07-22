// GREEN — managed-aggregator host (INV-006/INV-009). Built mid-run by the
// bootstrap controller; VALIDATES the controller's nonce-bound receipts (it
// never claims to have performed restore/build), owns the shared probes
// (P02, P04), derives its expected report set from the committed manifest's
// route plan (never from reports found on disk — RS-010), binds every consumed
// report to the current run_id (codex F1), and emits the run-level report
// atomically. Schema-digest validation runs BEFORE any report is parsed
// (compiled-in trust anchor — codex R3-6).

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Corrected.Spike.Contracts;

var manifestPath = "";
var schemaPath = "";
var registryPath = "";
var reportsDir = "";
var runId = "";
var nonce = "";
var runRoot = "";
var restoreReceipt = "";
var buildReceipt = "";
var suiteStatusText = "unknown";
var solverPath = "";
var outPath = "";
var outCopy = "";
var aggregationReceiptPath = "";
var reports = new List<(string Route, string Path, int Exit)>();

for (var i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"missing value for {args[i]}");
    switch (args[i])
    {
        case "--manifest": manifestPath = Next(); break;
        case "--schema": schemaPath = Next(); break;
        case "--registry": registryPath = Next(); break;
        case "--reports-dir": reportsDir = Next(); break;
        case "--run-id": runId = Next(); break;
        case "--nonce": nonce = Next(); break;
        case "--run-root": runRoot = Next(); break;
        case "--restore-receipt": restoreReceipt = Next(); break;
        case "--build-receipt": buildReceipt = Next(); break;
        case "--suite-status": suiteStatusText = Next(); break;
        case "--solver": solverPath = Next(); break;
        case "--out": outPath = Next(); break;
        case "--out-copy": outCopy = Next(); break;
        case "--aggregation-receipt": aggregationReceiptPath = Next(); break;
        case "--report":
        {
            // route=path:exit — expected report paths flow directly from the
            // launches the controller actually performed (RS-010/codex F1).
            var spec = Next();
            var eq = spec.IndexOf('=');
            var colon = spec.LastIndexOf(':');
            reports.Add((spec[..eq], spec[(eq + 1)..colon], int.Parse(spec[(colon + 1)..], CultureInfo.InvariantCulture)));
            break;
        }
        default:
            Console.Error.WriteLine($"SpikeAggregator: unknown argument '{args[i]}'");
            return ExitCodes.Incomplete;
    }
}

// INV-009 (codex F11/R2-9/R3-6): the committed schema digest is validated
// against the COMPILED-IN trust anchor BEFORE parsing or projecting any report.
try
{
    EvidenceSchema.ValidateSchemaFile(schemaPath, registryPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"SpikeAggregator: schema digest validation failed BEFORE any report parse: {ex.Message}");
    return ExitCodes.Incomplete;
}

if (reportsDir.Length > 0 && reports.Count == 0)
{
    // RS-010: the aggregator never derives its input set from reports found on
    // disk — a directory scan is the natural fail-open shape and is refused.
    Console.Error.WriteLine("SpikeAggregator: --reports-dir without controller-attested --report launches is refused (RS-010): the expected report set derives from the manifest route plan and the launches actually performed, never from a disk scan.");
    return ExitCodes.Incomplete;
}

var manifest = ProbeManifest.Load(manifestPath);
if (runId.Length == 0)
{
    Console.Error.WriteLine("SpikeAggregator: --run-id is required (codex F1)");
    return ExitCodes.Incomplete;
}

// Validate the controller's nonce-bound receipts (the aggregator validates,
// it never performs — codex F7/R2-4/R3-5).
var probeResults = new List<ProbeResult>();
string? restoreArgvJson = null;
try
{
    using var restore = JsonDocument.Parse(File.ReadAllText(restoreReceipt));
    var receiptRunId = restore.RootElement.GetProperty("run_id").GetString();
    var receiptNonce = restore.RootElement.GetProperty("nonce").GetString();
    if (receiptRunId != runId || (nonce.Length > 0 && receiptNonce != nonce))
    {
        throw new InvalidOperationException($"restore receipt run_id/nonce mismatch (receipt {receiptRunId}) — stale receipts are rejected (codex F1)");
    }
    var exit = restore.RootElement.GetProperty("exit").GetInt32();
    restoreArgvJson = restore.RootElement.GetProperty("argv").GetRawText();
    var status = exit == 0 ? ProbeStatus.Pass : ProbeStatus.Fail;
    probeResults.Add(new ProbeResult(new ProbeKey("P01", "A"), status, exit == 0 ? null : $"locked restore exit {exit}"));
    probeResults.Add(new ProbeResult(new ProbeKey("P01", "B"), status, exit == 0 ? null : $"locked restore exit {exit}"));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"SpikeAggregator: restore receipt invalid: {ex.Message}");
    probeResults.Add(new ProbeResult(new ProbeKey("P01", "A"), ProbeStatus.Incomplete, "restore receipt missing/invalid"));
    probeResults.Add(new ProbeResult(new ProbeKey("P01", "B"), ProbeStatus.Incomplete, "restore receipt missing/invalid"));
}

try
{
    using var build = JsonDocument.Parse(File.ReadAllText(buildReceipt));
    var receiptRunId = build.RootElement.GetProperty("run_id").GetString();
    var receiptNonce = build.RootElement.GetProperty("nonce").GetString();
    if (receiptRunId != runId || (nonce.Length > 0 && receiptNonce != nonce))
    {
        throw new InvalidOperationException("build receipt run_id/nonce mismatch — stale receipts are rejected (codex F1)");
    }
    var exit = build.RootElement.GetProperty("exit").GetInt32();
    var sdk = build.RootElement.GetProperty("sdk_version").GetString();
    var pinned = ReadSdkPin();
    var ok = exit == 0 && sdk == pinned;
    probeResults.Add(new ProbeResult(new ProbeKey("P02", "shared"), ok ? ProbeStatus.Pass : ProbeStatus.Fail,
        ok ? null : $"build exit {exit}; SDK '{sdk}' vs pinned '{pinned}' (EA-001)"));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"SpikeAggregator: build receipt invalid: {ex.Message}");
    probeResults.Add(new ProbeResult(new ProbeKey("P02", "shared"), ProbeStatus.Incomplete, "build receipt missing/invalid"));
}

// P04 — solver identity (owned here): provisioned z3 exists at the
// non-discoverable install location, the retained archive digest matches the
// pin, and the banner reports 4.12.1 (launched via the managed launcher).
string? executedSolverSha = null;
{
    var detail = new List<string>();
    var solver = solverPath.Length > 0 ? solverPath : Path.Combine(runRoot, SolverLayout.SolverRelativePath.Replace('/', Path.DirectorySeparatorChar));
    var pass = true;
    if (!File.Exists(solver))
    {
        pass = false;
        detail.Add("solver-unavailable: provisioned z3 missing — run provisioning first: scripts/provision-z3.sh");
    }
    else
    {
        // Evidence binds to the PINNED RELEASE ASSET retained in the run root
        // (its digest IS the BND-002 pin); the extracted binary is its
        // provisioning derivative. Execution identity is proven behaviorally
        // (INV-003), never presumed from a digest.
        var archive = Path.Combine(runRoot, "z3-4.12.1-x64-glibc-2.35.zip");
        if (!File.Exists(archive) || Sha256File(archive) != SolverLayout.Z3PinnedSha256)
        {
            pass = false;
            detail.Add("retained release-asset digest does not match the pin (BND-002)");
            executedSolverSha = Sha256File(solver);
        }
        else
        {
            executedSolverSha = Sha256File(archive);
        }
        var banner = ManagedLauncher.Launch(new LaunchRequest(
            solver, new[] { "-version" }, runRoot, SolverIdentityEnv(runRoot), 60));
        if (banner.ExitCode != 0 || !banner.StdOut.Contains("Z3 version 4.12.1", StringComparison.Ordinal))
        {
            pass = false;
            detail.Add($"banner did not report 4.12.1 (exit {banner.ExitCode}: {banner.StdOut.Trim()})");
        }
    }
    probeResults.Add(new ProbeResult(new ProbeKey("P04", "shared"), pass ? ProbeStatus.Pass : ProbeStatus.Incomplete,
        pass ? null : string.Join("; ", detail)));
}

// Route reports: only from performed launches; run_id bound.
var routeReports = new List<RouteReport>();
var consumedPaths = new List<string>();
foreach (var (route, path, exit) in reports)
{
    if (!File.Exists(path))
    {
        routeReports.Add(new RouteReport(runId, route, Array.Empty<ProbeResult>(), exit, null, path));
        continue;
    }
    try
    {
        var text = File.ReadAllText(path);
        EvidenceSchema.ValidateReport(text, schemaPath);
        using var doc = JsonDocument.Parse(text);
        var reportRunId = doc.RootElement.GetProperty("run_id").GetString() ?? "";
        var probes = new List<ProbeResult>();
        foreach (var p in doc.RootElement.GetProperty("deterministic").GetProperty("per_probe_results").EnumerateArray())
        {
            probes.Add(new ProbeResult(
                new ProbeKey(p.GetProperty("probe").GetString()!, p.GetProperty("route").GetString()!),
                Enum.Parse<ProbeStatus>(p.GetProperty("status").GetString()!, ignoreCase: true),
                p.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null));
        }
        // The route child reports only its route probes; the aggregator owns
        // the shared probes and the controller attests P01 (codex F7).
        probes.AddRange(probeResults.Where(r => r.Key.Route == "shared" || r.Key.Route == route));
        routeReports.Add(new RouteReport(reportRunId, route, probes, exit, null, path));
        consumedPaths.Add(path);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"SpikeAggregator: malformed route report {path}: {ex.Message}");
        routeReports.Add(new RouteReport(runId, route, Array.Empty<ProbeResult>(), exit, null, path));
    }
}

var suiteStatus = suiteStatusText switch
{
    "success" => SuiteStatus.Success,
    "failure" => SuiteStatus.Failure,
    _ => SuiteStatus.Unknown,
};

var result = VerdictAggregator.Aggregate(manifest, runId, routeReports, suiteStatus);

// Combined 22-entry per-probe view (manifest order).
var byKey = new Dictionary<ProbeKey, ProbeResult>();
foreach (var r in probeResults)
{
    byKey[r.Key] = r;
}
foreach (var report in routeReports)
{
    foreach (var p in report.Probes)
    {
        byKey.TryAdd(p.Key, p);
    }
}
var perProbe = new List<Dictionary<string, object?>>();
foreach (var entry in manifest.Entries)
{
    var key = new ProbeKey(entry.ProbeId, entry.Route);
    if (byKey.TryGetValue(key, out var r))
    {
        perProbe.Add(new Dictionary<string, object?>
        {
            ["probe"] = key.ProbeId,
            ["route"] = key.Route,
            ["status"] = r.Status.ToString().ToLowerInvariant(),
            ["detail"] = r.Detail,
        });
    }
    else
    {
        perProbe.Add(new Dictionary<string, object?>
        {
            ["probe"] = key.ProbeId,
            ["route"] = key.Route,
            ["status"] = "incomplete",
            ["detail"] = "no report from a performed launch covers this manifest entry (INV-006)",
        });
    }
}

string ReasonVariant(VerdictReason? reason) => reason switch
{
    null => "none",
    VerdictReason.ProbeFailure => "probe-failure",
    VerdictReason.PrerequisiteFailure => "prerequisite-failure",
    VerdictReason.MissingReport => "missing-report",
    VerdictReason.MalformedReport => "malformed-report",
    VerdictReason.Crash => "crash",
    VerdictReason.ExitReportMismatch => "exit-report-mismatch",
    VerdictReason.SuiteFailure => "suite-failure",
    VerdictReason.UnadjudicatedFailure => "unadjudicated-failure",
    VerdictReason.Adjudicated => "adjudicated",
    _ => "unknown",
};

string StateText(RouteOutcome outcome) => outcome.State switch
{
    RouteState.Compatible => "COMPATIBLE",
    RouteState.Incomplete => "INCOMPLETE",
    RouteState.Incompatible => $"INCOMPATIBLE({outcome.Class})",
    RouteState.UpstreamDefect => "UPSTREAM_DEFECT",
    _ => "UNADJUDICATED_IN_PROCESS_FAILURE",
};

var routeVerdicts = new List<Dictionary<string, object?>>();
var verdictReasons = new List<Dictionary<string, object?>>();
foreach (var route in manifest.MandatoryRoutes)
{
    var outcome = result.RouteVerdicts[route];
    var reasonMap = new Dictionary<string, object?> { ["variant"] = ReasonVariant(outcome.Reason) };
    switch (outcome.Reason)
    {
        case VerdictReason.ProbeFailure pf:
            reasonMap["first_failing_probe_by_manifest_order"] = pf.FirstFailingByManifestOrder.ToString();
            break;
        case VerdictReason.PrerequisiteFailure prereq:
            reasonMap["cause"] = prereq.Cause.ToString();
            reasonMap["detail"] = prereq.Detail;
            break;
        case VerdictReason.MissingReport mr:
            reasonMap["route"] = mr.Route;
            break;
        case VerdictReason.MalformedReport mal:
            reasonMap["route"] = mal.Route;
            reasonMap["detail"] = mal.Detail;
            break;
        case VerdictReason.Crash crash:
            reasonMap["exit_code_or_signal"] = crash.Signal ?? crash.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
            break;
        case VerdictReason.ExitReportMismatch mm:
            reasonMap["exit_code"] = mm.ExitCode;
            reasonMap["report_summary"] = mm.ReportSummary;
            break;
        case VerdictReason.SuiteFailure sf:
            reasonMap["detail"] = sf.Detail;
            break;
    }
    routeVerdicts.Add(new Dictionary<string, object?>
    {
        ["route"] = route,
        ["state"] = StateText(outcome),
        ["verdict_reason"] = outcome.Reason is null ? null : reasonMap,
    });
    if (outcome.Reason is not null)
    {
        var vr = new Dictionary<string, object?>(reasonMap) { ["route"] = route };
        verdictReasons.Add(vr);
    }
}

// Deterministic oracle/input digests (class 2).
var spikeRoot = FindSpikeRoot();
var fixtureDigests = new SortedDictionary<string, object?>(StringComparer.Ordinal);
foreach (var fixture in Directory.EnumerateFiles(Path.Combine(spikeRoot, "fixtures"), "*.dfy"))
{
    fixtureDigests["fixtures/" + Path.GetFileName(fixture)] = Sha256File(fixture);
}
var sidecarDigests = new SortedDictionary<string, object?>(StringComparer.Ordinal);
foreach (var sidecar in Directory.EnumerateFiles(Path.Combine(spikeRoot, "fixtures", "expected"), "*.json"))
{
    sidecarDigests["fixtures/expected/" + Path.GetFileName(sidecar)] = Sha256File(sidecar);
}
var lockDigests = new SortedDictionary<string, object?>(StringComparer.Ordinal);
foreach (var (name, rel) in SpikeProjects())
{
    var lockFile = Path.Combine(spikeRoot, Path.GetDirectoryName(rel.Replace('/', Path.DirectorySeparatorChar))!, "packages.lock.json");
    if (File.Exists(lockFile))
    {
        lockDigests[name] = Sha256File(lockFile);
    }
}

var exitReportConsistent = reports.All(r =>
{
    var report = routeReports.FirstOrDefault(rr => rr.Route == r.Route);
    if (report is null || report.Probes.Count == 0)
    {
        return false;
    }
    var mapped = AdjudicationStateMachine.MapExit(r.Exit, null, report);
    return mapped.Reason is null or not (VerdictReason.ExitReportMismatch or VerdictReason.Crash);
});

var runDirectory = Path.GetRelativePath(spikeRoot, Path.GetFullPath(runRoot)).Replace('\\', '/');
var (gitCommit, gitDirty) = ReadGitState(runRoot);

var reportObject = new Dictionary<string, object?>
{
    ["evidence_schema_version"] = 1,
    ["evidence_schema_sha256"] = Sha256File(schemaPath),
    ["probe_manifest_sha256"] = ProbeManifest.ComputeSha256(manifestPath),
    ["run_id"] = runId,
    ["kind"] = "run-report",
    ["binding_identity"] = new Dictionary<string, object?>
    {
        ["run_directory"] = runDirectory,
        ["git_commit_id"] = gitCommit,
        ["git_dirty_flag"] = gitDirty,
        ["host_rid"] = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
        ["sdk_version"] = ReadSdkPin(),
        ["actual_runtime_version"] = Environment.Version.ToString(),
        ["restore_argv_concrete"] = restoreArgvJson is null ? new List<string>() : JsonSerializer.Deserialize<List<string>>(restoreArgvJson),
        ["solver_path_concrete"] = "z3-4.12.1-x64-glibc-2.35.zip",
    },
    ["deterministic"] = new Dictionary<string, object?>
    {
        ["route_verdicts"] = routeVerdicts,
        ["verdict_reasons"] = verdictReasons.Count == 0 ? null : verdictReasons.Cast<object?>().ToList(),
        ["per_probe_results"] = perProbe,
        ["final_suite_status"] = suiteStatusText is "success" or "failure" ? suiteStatusText : "unknown",
        ["exit_report_matrix_outcome"] = exitReportConsistent ? "consistent" : "inconsistent",
        ["fixture_digests"] = fixtureDigests,
        ["sidecar_digests"] = sidecarDigests,
        ["lock_file_digests"] = lockDigests,
        ["nuget_config_digest"] = Sha256File(Path.Combine(spikeRoot, "NuGet.Config")),
        ["oracle_file_digests"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["option_manifest"] = Sha256File(Path.Combine(spikeRoot, "manifest", "option-manifest.json")),
            ["probe_manifest"] = ProbeManifest.ComputeSha256(manifestPath),
            ["expected_loaded_route_a"] = Sha256File(Path.Combine(spikeRoot, "manifest", "expected-loaded", "route-a.json")),
            ["expected_loaded_route_b"] = Sha256File(Path.Combine(spikeRoot, "manifest", "expected-loaded", "route-b.json")),
        },
        ["executed_solver_sha256"] = executedSolverSha,
        ["adjudication_records"] = null,
    },
    ["volatile"] = new Dictionary<string, object?>
    {
        ["timestamps"] = new Dictionary<string, object?> { ["aggregated_utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
    },
};

var json = JsonSerializer.Serialize(reportObject, new JsonSerializerOptions { WriteIndented = true });
EvidenceSchema.ValidateReport(json, schemaPath);
AtomicWrite(outPath, json);
if (outCopy.Length > 0)
{
    AtomicWrite(outCopy, json);
}

if (aggregationReceiptPath.Length > 0)
{
    var receipt = JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["run_id"] = runId,
        ["consumed_report_paths"] = consumedPaths,
        ["note"] = "expected report set derives from the committed manifest route plan and the launches actually performed — never from reports found on disk (RS-010)",
    }, new JsonSerializerOptions { WriteIndented = true });
    AtomicWrite(aggregationReceiptPath, receipt);
}

// Terminal per-route verdict summary (INV-014) — human entry point, no JSON parsing.
foreach (var route in manifest.MandatoryRoutes)
{
    var outcome = result.RouteVerdicts[route];
    var failing = perProbe.Where(p => (string?)p["route"] == route && (string?)p["status"] != "pass")
        .Select(p => $"{p["probe"]} ({p["detail"] ?? "no detail"})").ToList();
    Console.WriteLine($"route {route} verdict: {StateText(outcome)}; failed probes: {(failing.Count == 0 ? "-" : string.Join(", ", failing))}; report: {outPath}");
}

return result.RouteVerdicts.Values.All(v => v.State == RouteState.Compatible)
    ? ExitCodes.RouteProbesPassed
    : ExitCodes.Incomplete;

static string Sha256File(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static IReadOnlyDictionary<string, string> SolverIdentityEnv(string runRoot)
{
    // Constructed from the committed launch-class profile (EA-008).
    var profilePath = Path.Combine(FindSpikeRoot(), "config", "env-profiles.json");
    using var doc = JsonDocument.Parse(File.ReadAllText(profilePath));
    var profile = doc.RootElement.GetProperty("launch_classes").GetProperty("solver-identity");
    var env = new Dictionary<string, string>();
    foreach (var kv in profile.EnumerateObject())
    {
        var template = kv.Value.GetString() ?? "";
        var value = template switch
        {
            "<constructed:decoy-first>" => Path.Combine(runRoot, "decoys") + ":/usr/bin:/bin",
            "<passthrough>" => Environment.GetEnvironmentVariable(kv.Name) ?? "",
            _ => template.Replace("<run-root>", runRoot),
        };
        if (value.Length > 0)
        {
            env[kv.Name] = value;
        }
    }
    return env;
}

static string FindSpikeRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "DafnyCompatSpike.sln")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }
    dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "DafnyCompatSpike.sln")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }
    throw new InvalidOperationException("cannot locate the spike root");
}

static string ReadSdkPin()
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindSpikeRoot(), "global.json")));
    return doc.RootElement.GetProperty("sdk").GetProperty("version").GetString() ?? "";
}

static (string Commit, bool Dirty) ReadGitState(string runRoot)
{
    var marker = Path.Combine(runRoot, "git-state.json");
    if (File.Exists(marker))
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(marker));
        return (doc.RootElement.GetProperty("commit").GetString() ?? "unknown",
            doc.RootElement.GetProperty("dirty").GetBoolean());
    }
    return ("unknown", false);
}

static IEnumerable<(string Name, string Rel)> SpikeProjects()
{
    yield return ("SpikeContracts", "contracts/SpikeContracts/SpikeContracts.csproj");
    yield return ("SpikeDafnyAdapter.RouteA", "adapters/SpikeDafnyAdapter.RouteA/SpikeDafnyAdapter.RouteA.csproj");
    yield return ("SpikeDafnyAdapter.RouteB", "adapters/SpikeDafnyAdapter.RouteB/SpikeDafnyAdapter.RouteB.csproj");
    yield return ("RouteAHarness", "harness/RouteAHarness/RouteAHarness.csproj");
    yield return ("RouteBHarness", "harness/RouteBHarness/RouteBHarness.csproj");
    yield return ("SpikeAggregator", "aggregator/SpikeAggregator/SpikeAggregator.csproj");
    yield return ("RouteAControl", "control/RouteAControl/RouteAControl.csproj");
    yield return ("RouteBControl", "control/RouteBControl/RouteBControl.csproj");
    yield return ("SpikeTests", "tests/SpikeTests/SpikeTests.csproj");
}

static void AtomicWrite(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    var temp = path + ".tmp-" + Environment.ProcessId;
    File.WriteAllText(temp, content);
    File.Move(temp, path, overwrite: true);
}
