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
var printSuiteStatusPath = "";
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
        case "--print-suite-status": printSuiteStatusPath = Next(); break;
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

if (printSuiteStatusPath.Length > 0)
{
    // QA-024: schema-validating read-out mode for scripts (regen-sample.sh).
    // Scripts that gate committable evidence must not parse JSON with line
    // greps; this routes the read through the schema-validating contracts code
    // (the trust-anchor check above already ran). Prints exactly:
    //   final_suite_status=<value>
    //   route_verdict route=<r> state=<s> variant=<verdict_reason variant|none>
    try
    {
        var text = File.ReadAllText(printSuiteStatusPath);
        EvidenceSchema.ValidateReport(text, schemaPath);
        using var doc = JsonDocument.Parse(text);
        var det = doc.RootElement.GetProperty("deterministic");
        Console.WriteLine($"final_suite_status={det.GetProperty("final_suite_status").GetString()}");
        if (det.TryGetProperty("route_verdicts", out var rvs) && rvs.ValueKind == JsonValueKind.Array)
        {
            foreach (var rv in rvs.EnumerateArray())
            {
                var variant = rv.TryGetProperty("verdict_reason", out var vr)
                              && vr.ValueKind == JsonValueKind.Object
                              && vr.TryGetProperty("variant", out var v)
                    ? v.GetString() ?? "none"
                    : "none";
                Console.WriteLine($"route_verdict route={rv.GetProperty("route").GetString()} state={rv.GetProperty("state").GetString()} variant={variant}");
            }
        }
        return ExitCodes.RouteProbesPassed;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"SpikeAggregator: --print-suite-status failed closed: {ex.Message} (AP-009: a malformed report is its own state)");
        return ExitCodes.Incomplete;
    }
}

if (reportsDir.Length > 0 && reports.Count == 0)
{
    // RS-010: the aggregator never derives its input set from reports found on
    // disk — a directory scan is the natural fail-open shape and is refused.
    Console.Error.WriteLine("SpikeAggregator: --reports-dir without controller-attested --report launches is refused (RS-010): the expected report set derives from the manifest route plan and the launches actually performed, never from a disk scan.");
    return ExitCodes.Incomplete;
}

// PR-003/PR-007 startup anchors: the probe manifest digest and the committed
// pin files are validated BEFORE any receipt or report is consumed.
try
{
    VerdictAggregator.ValidateProbeManifestFile(manifestPath);
    var aggSpikeRoot = FindSpikeRoot();
    PinFiles.ValidateZ3Pin(aggSpikeRoot);
    PinFiles.ValidateNet8ControlPin(aggSpikeRoot);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"SpikeAggregator: startup trust anchor rejected: {ex.Message}");
    return ExitCodes.Incomplete;
}

var manifest = ProbeManifest.Load(manifestPath);
if (runId.Length == 0)
{
    Console.Error.WriteLine("SpikeAggregator: --run-id is required (codex F1)");
    return ExitCodes.Incomplete;
}

// MA-VI-6: final_suite_status is DERIVED from the controller's nonce-bound
// suite receipt (RunLayout.SuiteReceiptRelativePath — see the coordination
// contract there); --suite-status is at most a cross-check. With a receipt:
// run_id/nonce must bind and a mismatching --suite-status claim is REFUSED.
// Without a receipt: a bare argv claim of "success" is never trusted — it is
// downgraded to "unknown" (fail-closed: verdicts stay INCOMPLETE), so an
// out-of-band re-invocation with a forged --suite-status can never mint a
// COMPATIBLE run report.
var suiteReceiptPath = runRoot.Length > 0
    ? Path.Combine(runRoot, RunLayout.SuiteReceiptRelativePath.Replace('/', Path.DirectorySeparatorChar))
    : "";
string? receiptDerivedStatus = null;
if (suiteReceiptPath.Length > 0 && File.Exists(suiteReceiptPath))
{
    try
    {
        using var suiteReceipt = JsonDocument.Parse(File.ReadAllText(suiteReceiptPath));
        var receiptRunId = suiteReceipt.RootElement.GetProperty("run_id").GetString();
        var receiptNonce = suiteReceipt.RootElement.GetProperty("nonce").GetString();
        if (receiptRunId != runId || (nonce.Length > 0 && receiptNonce != nonce))
        {
            throw new InvalidOperationException(
                $"suite receipt run_id/nonce mismatch (receipt {receiptRunId}) — stale/forged suite receipts are rejected (MA-VI-6)");
        }
        var suiteExit = suiteReceipt.RootElement.GetProperty("suite_exit").GetInt32();
        receiptDerivedStatus = suiteExit == 0 ? "success" : "failure";
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"SpikeAggregator: suite receipt invalid: {ex.Message} — refusing (MA-VI-6 fail-closed)");
        return ExitCodes.Incomplete;
    }
    if (suiteStatusText is "success" or "failure" && suiteStatusText != receiptDerivedStatus)
    {
        Console.Error.WriteLine(
            $"SpikeAggregator: --suite-status '{suiteStatusText}' contradicts the nonce-bound suite receipt ('{receiptDerivedStatus}') — a forged suite-status claim is refused (MA-VI-6)");
        return ExitCodes.Incomplete;
    }
    suiteStatusText = receiptDerivedStatus;
}
else if (suiteStatusText == "success")
{
    Console.Error.WriteLine(
        "SpikeAggregator: --suite-status success carries no nonce-bound suite receipt "
        + $"({RunLayout.SuiteReceiptRelativePath}) — the unvalidated claim is downgraded to 'unknown' (MA-VI-6 fail-closed; the controller emits the receipt after the test phase)");
    suiteStatusText = "unknown";
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
    restoreArgvJson = restore.RootElement.GetProperty("argv").GetRawText();
    // QA-009: P01 status is derived from EACH partition's own recorded exit —
    // a Route-B-only lock fault attributes only to P01(B) (R4-07 non-veto).
    var partitions = restore.RootElement.GetProperty("p01_partitions");
    foreach (var route in new[] { "A", "B" })
    {
        var exit = partitions.GetProperty(route).GetProperty("exit").GetInt32();
        probeResults.Add(new ProbeResult(new ProbeKey("P01", route),
            exit == 0 ? ProbeStatus.Pass : ProbeStatus.Fail,
            exit == 0 ? null : $"route {route} locked restore exit {exit}"));
    }
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
// non-discoverable install location, the INSTALLED BINARY's digest matches the
// digest provisioning recorded from the pin-verified archive (QA-002), the
// retained release asset matches the BND-002 pin, and the banner reports
// 4.12.1 (launched via the managed launcher).
string? executedSolverSha = null;
string? solverArchiveSha = null;
{
    var detail = new List<string>();
    var solver = solverPath.Length > 0 ? solverPath : Path.Combine(runRoot, SolverLayout.SolverRelativePath.Replace('/', Path.DirectorySeparatorChar));
    var pass = true;
    var provisioningCrossCheckRan = false;
    if (!File.Exists(solver))
    {
        pass = false;
        detail.Add("solver-unavailable: provisioned z3 missing — run provisioning first: scripts/provision-z3.sh");
    }
    else
    {
        // QA-002: executed_solver_sha256 is ALWAYS the recomputed digest of
        // the solver binary at the option-manifest path; the release-asset pin
        // is a separate field. Post-provisioning substitution is caught by
        // comparing the installed binary against provisioning's own record.
        executedSolverSha = Sha256File(solver);
        var provisioningRecord = Path.Combine(runRoot, "solver", "z3-4.12.1", "binary.sha256");
        if (solverPath.Length == 0 || solver.EndsWith(SolverLayout.SolverRelativePath.Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal))
        {
            if (!File.Exists(provisioningRecord))
            {
                pass = false;
                detail.Add("provisioning's binary.sha256 record is missing — the installed binary cannot be tied to the pin-verified archive (QA-002)");
            }
            else
            {
                provisioningCrossCheckRan = true;
                var recorded = File.ReadAllText(provisioningRecord).Trim();
                if (recorded != executedSolverSha)
                {
                    pass = false;
                    detail.Add($"installed binary digest {executedSolverSha} does not match provisioning's record {recorded} — post-provisioning substitution (INV-003 fail-closed)");
                }
            }
        }
        var archive = Path.Combine(runRoot, "z3-4.12.1-x64-glibc-2.35.zip");
        if (File.Exists(archive))
        {
            solverArchiveSha = Sha256File(archive);
        }
        if (solverArchiveSha != SolverLayout.Z3PinnedSha256)
        {
            pass = false;
            detail.Add("retained release-asset digest does not match the pin (BND-002)");
        }
        var banner = ManagedLauncher.Launch(new LaunchRequest(
            solver, new[] { "-version" }, runRoot, SolverIdentityEnv(runRoot), 60));
        if (banner.ExitCode != 0 || !banner.StdOut.Contains("Z3 version 4.12.1", StringComparison.Ordinal))
        {
            pass = false;
            detail.Add($"banner did not report 4.12.1 (exit {banner.ExitCode}: {banner.StdOut.Trim()})");
        }
    }
    // MA-VI-7: a --solver override outside the standard layout silently skips
    // the installed-binary-vs-provisioning cross-check — an unqualified P04
    // pass is then unreachable: the probe records Incomplete with the skipped
    // sub-check named, never a wrong-reason pass (AP-010).
    if (pass && !provisioningCrossCheckRan)
    {
        probeResults.Add(new ProbeResult(new ProbeKey("P04", "shared"), ProbeStatus.Incomplete,
            "override path: provisioning cross-check not applicable — banner/archive checks ran, but P04 pass requires the binary.sha256 comparison (MA-VI-7)"));
    }
    else
    {
        probeResults.Add(new ProbeResult(new ProbeKey("P04", "shared"), pass ? ProbeStatus.Pass : ProbeStatus.Incomplete,
            pass ? null : string.Join("; ", detail)));
    }
}

// Route reports: only from performed launches; run_id bound. Malformed
// reports carry their OWN variant (QA-005 — they never collapse into
// missing-report or crash), and route-level adjudication records propagate to
// the run level with the unadjudicated-failure verdict variant.
var routeReports = new List<RouteReport>();
var consumedPaths = new List<string>();
var malformedRoutes = new Dictionary<string, string>(StringComparer.Ordinal);
var routeAdjudicationRecords = new List<(string Route, JsonElement Record)>();
var routeRecordDocuments = new List<JsonDocument>();
var childProbesByRoute = new Dictionary<string, IReadOnlyDictionary<ProbeKey, ProbeResult>>(StringComparer.Ordinal);
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
        var doc = JsonDocument.Parse(text);
        routeRecordDocuments.Add(doc);
        var reportRunId = doc.RootElement.GetProperty("run_id").GetString() ?? "";
        var probes = new List<ProbeResult>();
        foreach (var p in doc.RootElement.GetProperty("deterministic").GetProperty("per_probe_results").EnumerateArray())
        {
            probes.Add(new ProbeResult(
                new ProbeKey(p.GetProperty("probe").GetString()!, p.GetProperty("route").GetString()!),
                Enum.Parse<ProbeStatus>(p.GetProperty("status").GetString()!, ignoreCase: true),
                p.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null));
        }
        // MA-VI-1: the spec-named duplicate-key rejection runs ON THE
        // PRODUCTION INGESTION PATH — a duplicate composite key in the raw
        // report array makes the whole report malformed in BOTH the verdict
        // and the per_probe_results view (never a first-wins TryAdd).
        var keyed = VerdictAggregator.ToKeyedResults(probes);
        // MA-VI-R2-1: the schema does NOT constrain per_probe_results[].probe to
        // the manifest probe-id set, so a schema-clean report can carry an
        // unknown/superset composite key (e.g. P99(A)). ComputeRouteVerdict
        // already fails such a report closed (INCOMPLETE malformed-report via
        // its exact-equality unknown-key check), but the per_probe_results view
        // would still show all-pass and exit_report_matrix=consistent — the
        // three surfaces would disagree. Detect unknown/superset keys vs the
        // manifest instantiation HERE (the same catch path duplicates take), so
        // the verdict, the per-probe view, and the exit/report matrix all read
        // malformed. (Mirrors ComputeRouteVerdict's `!expectedSet.Contains(key)`
        // leg at ingestion — INV-006/AP-006 exact equality, never superset.)
        var instantiationKeys = manifest.InstantiationFor(route)
            .Select(e => new ProbeKey(e.ProbeId, e.Route)).ToHashSet();
        var unknownKeys = keyed.Keys.Where(k => !instantiationKeys.Contains(k)).ToList();
        if (unknownKeys.Count > 0)
        {
            throw new InvalidOperationException(
                $"route report carries composite probe key(s) outside the manifest instantiation for route {route}: "
                + string.Join(", ", unknownKeys.Select(k => k.ToString()))
                + " — the completed set must be a SUBSET of the manifest instantiation, never a superset (MA-VI-R2-1/INV-006/AP-006)");
        }
        childProbesByRoute[route] = keyed;
        if (doc.RootElement.GetProperty("deterministic").TryGetProperty("adjudication_records", out var records)
            && records.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in records.EnumerateArray())
            {
                routeAdjudicationRecords.Add((route, record));
            }
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
        malformedRoutes[route] = $"route report failed closed-schema validation/parsing: {ex.GetType().Name}: {ex.Message} (AP-009: malformed is its own state)";
        childProbesByRoute.Remove(route);
        routeReports.Add(new RouteReport(runId, route, Array.Empty<ProbeResult>(), exit, null, path));
    }
}

var suiteStatus = suiteStatusText switch
{
    "success" => SuiteStatus.Success,
    "failure" => SuiteStatus.Failure,
    _ => SuiteStatus.Unknown,
};

var aggregateResult = VerdictAggregator.Aggregate(manifest, runId, routeReports, suiteStatus);
// QA-005: malformed reports override with their own variant; a route whose
// report carries an UNADJUDICATED in-process failure record surfaces the
// unadjudicated-failure variant at the run level (INV-013 state machine).
var finalVerdicts = new Dictionary<string, RouteOutcome>(StringComparer.Ordinal);
foreach (var kv in aggregateResult.RouteVerdicts)
{
    var route = kv.Key;
    var outcome = kv.Value;
    if (malformedRoutes.TryGetValue(route, out var malformedDetail))
    {
        outcome = new RouteOutcome(RouteState.Incomplete, null,
            new VerdictReason.MalformedReport(route, malformedDetail));
    }
    else if (outcome.State != RouteState.Compatible)
    {
        var unadj = routeAdjudicationRecords.FirstOrDefault(r =>
            r.Route == route
            && r.Record.TryGetProperty("terminal_state", out var ts)
            && ts.GetString() == "UnadjudicatedInProcessFailure");
        if (unadj.Record.ValueKind == JsonValueKind.Object && outcome.Reason is VerdictReason.ProbeFailure)
        {
            var payload = new UnadjudicatedInProcessFailure(
                Enum.TryParse<UnadjudicatedVariant>(unadj.Record.TryGetProperty("variant", out var variant) ? variant.GetString()?.Replace("-", "") : null, ignoreCase: true, out var v) ? v : UnadjudicatedVariant.TypedException,
                new ProbeKey(unadj.Record.TryGetProperty("probe", out var pr) ? pr.GetString() ?? "?" : "?", route),
                unadj.Record.TryGetProperty("stage", out var st) ? st.GetString() ?? "in-process" : "in-process",
                unadj.Record.TryGetProperty("minimal_inputs", out var mi) ? mi.GetString() ?? "" : "",
                unadj.Record.TryGetProperty("typed_diagnostic", out var td) ? td.GetString() : null,
                new IdentityEvidence(runId, "see route report", "see route report", "see route report", "see route report"));
            outcome = new RouteOutcome(RouteState.UnadjudicatedInProcessFailure, null,
                new VerdictReason.UnadjudicatedFailure(payload));
        }
    }
    finalVerdicts[route] = outcome;
}
var result = new RunResult(runId, finalVerdicts, suiteStatus);

// Combined 22-entry per-probe view (manifest order). MA-VI-1: source
// selection is an EXPLICIT OWNERSHIP check against the manifest's committed
// owner column — controller/aggregator-attested entries (P01/P02/P04) come
// only from probeResults; route-child entries come only from that route's own
// (duplicate-rejected) report — never TryAdd insertion-order precedence. A
// route whose report was rejected as malformed contributes NOTHING here, so
// the per-probe view and the verdict agree on the malformed-report state.
var perProbe = new List<Dictionary<string, object?>>();
foreach (var entry in manifest.Entries)
{
    var key = new ProbeKey(entry.ProbeId, entry.Route);
    ProbeResult? r = null;
    if (entry.Owner is "controller" or "aggregator")
    {
        r = probeResults.FirstOrDefault(p => p.Key == key);
    }
    else if (malformedRoutes.ContainsKey(entry.Route))
    {
        perProbe.Add(new Dictionary<string, object?>
        {
            ["probe"] = key.ProbeId,
            ["route"] = key.Route,
            ["status"] = "incomplete",
            ["detail"] = $"route report rejected as malformed — {malformedRoutes[entry.Route]}",
        });
        continue;
    }
    else if (childProbesByRoute.TryGetValue(entry.Route, out var childKeyed) && childKeyed.TryGetValue(key, out var childResult))
    {
        r = childResult;
    }
    if (r is not null)
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
        case VerdictReason.UnadjudicatedFailure uf:
            reasonMap["variant"] = "unadjudicated-failure";
            reasonMap["probe"] = uf.Payload.Probe.ToString();
            reasonMap["stage"] = uf.Payload.Stage;
            reasonMap["minimal_inputs"] = uf.Payload.MinimalInputs;
            reasonMap["typed_diagnostic"] = uf.Payload.TypedDiagnostic;
            reasonMap["identity_evidence_p01_p04"] = "embedded in the route report's adjudication record (propagated below)";
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
    // Scoped to child-owned probes, mirroring VerdictAggregator.Aggregate: the
    // exit/report matrix binds the route CHILD's exit to ITS OWN report;
    // controller-attested P01 and aggregator-owned shared probes surface as
    // probe failures, not exit mismatches (QA-022(2)).
    var childOwned = report.Probes.Where(p => p.Key.Route == r.Route && p.Key.ProbeId != "P01").ToList();
    var mapped = AdjudicationStateMachine.MapExit(r.Exit, null, report with { Probes = childOwned });
    // MA-VI-4: a null MapExit result IS the consistent cell (a non-verdict).
    return mapped is null || mapped.Reason is not (VerdictReason.ExitReportMismatch or VerdictReason.Crash);
});

var runDirectory = Path.GetRelativePath(spikeRoot, Path.GetFullPath(runRoot)).Replace('\\', '/');
var (gitCommit, gitDirty) = ReadGitState(runRoot);

var reportObject = new Dictionary<string, object?>
{
    ["evidence_schema_version"] = 2,
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
        ["solver_path_concrete"] = SolverLayout.SolverRelativePath,
        // MA-ED-2/PR-007: glibc_floor (EA-002 "recorded in evidence") is READ
        // from the startup-validated z3 pin file — the pin is consumed by the
        // execution path, and the floor reaches the run report's binding class.
        ["glibc_floor"] = PinFiles.ValidateZ3Pin(spikeRoot).GlibcFloor,
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
        ["solver_archive_sha256"] = solverArchiveSha,
        ["adjudication_records"] = routeAdjudicationRecords.Count == 0
            ? null
            : routeAdjudicationRecords.Select(r => (object?)JsonSerializer.Deserialize<Dictionary<string, object?>>(r.Record.GetRawText())).ToList(),
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

// QA-016 (closing QA-008's residual fail-open window): the aggregator's exit
// is fail-closed on ANY non-passing aggregation outcome — a failure DETECTED
// AT AGGREGATION (P04 post-suite solver substitution, P02 build-receipt
// mismatch, missing/malformed route report despite child exit 0, run_id-forged
// report rejection) must never hide behind exit 0 on the CI-facing channel.
// Exit derives from the EMITTED run report: any per-probe "fail" → 10
// (probe-failure); any "incomplete" entry or exit/report-matrix inconsistency
// → 20 (INCOMPLETE); only an all-pass, consistent 22-entry view exits 0 — so
// variance runs whose probes all pass still exit 0 and nested in-suite
// launches keep working (their verdicts stay INCOMPLETE via final_suite_status
// per codex R4-02, which is the CONTROLLER/suite channel, not aggregation).
// A verdict-level rejection can hide behind an all-pass per-probe view (a
// forged-run_id report's probes still enter the combined view), so the exit
// also fails closed on any route verdict that is neither COMPATIBLE nor the
// suite-channel INCOMPLETE(suite-failure) that every variance run carries.
var anyProbeFailed = perProbe.Any(p => (string?)p["status"] == "fail");
var anyProbeNotPass = perProbe.Any(p => (string?)p["status"] != "pass");
var aggregationDetectedFailure = result.RouteVerdicts.Values.Any(o =>
    o.State != RouteState.Compatible && o.Reason is not VerdictReason.SuiteFailure);
if (anyProbeFailed)
{
    return ExitCodes.ProbeFailure;
}
if (anyProbeNotPass || !exitReportConsistent || aggregationDetectedFailure)
{
    return ExitCodes.Incomplete;
}
return ExitCodes.RouteProbesPassed;

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
