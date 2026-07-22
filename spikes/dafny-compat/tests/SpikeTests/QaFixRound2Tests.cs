// QA fix round 2 — class-fix tests. Each finding's treatment carries a test
// that FAILS when the fix is reverted (QA-022 discipline). These ADD coverage;
// no existing assertion is weakened.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

[Collection(SharedStateMutatingCollection.Name)]
public class QaFixRound2Tests
{
    private static string SchemaPath => SpikePaths.P("schema", "evidence-schema.json");
    private static string RegistryPath => SpikePaths.P("schema", "schema-version-registry.json");
    private static string VarianceSamplePath => SpikePaths.P("evidence", "samples", "run-report.sample.json");
    private static string CanonicalSamplePath => SpikePaths.P("evidence", "samples", "run-report.canonical.sample.json");

    // ------------------------------------------------------------------ QA-015

    // QA-015 class fix: startup-gate sanctioning for decoys/-named directories
    // is a SINGLE predicate (exact script digest). A wrong-digest z3 planted in
    // a decoys/ dir first on PATH must fail the gate closed. With the dead-code
    // fallthrough reverted (location-name sanctioning), the gate would pass and
    // P06 would verify normally against the provisioned solver — exit 0 — so
    // this test fails on reversion.
    [Fact]
    public void QA015_WrongDigestDecoyInDecoysDir_FailsGateClosed()
    {
        var runRoot = SpikePaths.TestScratch("qa015-decoy-digest");
        var provision = Launch.Script("scripts/provision-z3.sh", null, "--run-root", runRoot);
        Assert.True(provision.ExitCode == 0, $"provisioning failed: {provision.StdErr}");

        // A directory NAMED "decoys" sanctions nothing: this z3 is NOT the
        // sanctioned recording script (wrong digest, non-recording).
        var fakeRoot = Path.Combine(runRoot, "fake-root");
        var fakeDecoyDir = Path.Combine(fakeRoot, "decoys");
        SpikePaths.WriteExecutable(Path.Combine(fakeDecoyDir, "z3"), "echo 'Z3 version 4.12.1 - 64 bit'");

        var env = new Dictionary<string, string>(EnvProfiles.For("harness", runRoot).ToDictionary(kv => kv.Key, kv => kv.Value))
        {
            ["PATH"] = fakeDecoyDir + Path.PathSeparator + "/usr/bin:/bin",
        };
        var report = Path.Combine(runRoot, "report.json");
        var result = Launch.HarnessWithEnv("A", env, "--probe", "P06", "--run-root", runRoot, "--out", report);
        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
        using var doc = Launch.Report(report);
        Assert.Contains("startup-gate", doc.RootElement.GetRawText());
    }

    // ------------------------------------------------------------------ QA-016

    // QA-016 class fix (aggregation-computed failure class, missing-report):
    // a route report path from a performed launch that does not exist — with
    // CHILD EXIT 0 — must yield a fail-closed aggregator exit, never success.
    [Fact]
    public void QA016_MissingRouteReport_ChildExitZero_AggregatorExitsFailClosed()
    {
        var scratch = SpikePaths.TestScratch("qa016-missing-report");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        var aRep = Path.Combine(scratch, "route-a.json");
        File.WriteAllText(aRep, RouteReportJson("A", runId));
        var bRep = Path.Combine(scratch, "route-b-DOES-NOT-EXIST.json");

        var (result, runReport) = RunAggregator(scratch, runId, aRep, 0, bRep, 0);
        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
        using var doc = Launch.Report(runReport);
        var b = RouteVerdict(doc, "B");
        Assert.Equal("missing-report", b.GetProperty("verdict_reason").GetProperty("variant").GetString());
    }

    // QA-016 class fix (aggregation-computed failure class, forged run_id):
    // a schema-valid, ALL-PASS route report bound to a different run_id — with
    // child exit 0 — is rejected at aggregation; the exit channel must be
    // fail-closed even though every per-probe entry in the combined view
    // passes (this is exactly the class that hid behind exit 0).
    [Fact]
    public void QA016_ForgedRunIdReport_AllProbesPass_AggregatorExitsFailClosed()
    {
        var scratch = SpikePaths.TestScratch("qa016-forged-runid");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        var aRep = Path.Combine(scratch, "route-a.json");
        var bRep = Path.Combine(scratch, "route-b.json");
        File.WriteAllText(aRep, RouteReportJson("A", runId));
        File.WriteAllText(bRep, RouteReportJson("B", "runid-FORGED-0123456789"));

        var (result, runReport) = RunAggregator(scratch, runId, aRep, 0, bRep, 0);
        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
        using var doc = Launch.Report(runReport);
        var b = RouteVerdict(doc, "B");
        Assert.Equal("malformed-report", b.GetProperty("verdict_reason").GetProperty("variant").GetString());
        Assert.Contains("stale", b.GetProperty("verdict_reason").GetProperty("detail").GetString());
        // Right-reason guard: the forged report's own probes DID pass in the
        // combined view — the nonzero exit comes from the verdict rejection.
        foreach (var p in doc.RootElement.GetProperty("deterministic").GetProperty("per_probe_results").EnumerateArray())
        {
            Assert.Equal("pass", p.GetProperty("status").GetString());
        }
    }

    // QA-016 class fix (aggregation-computed failure class, malformed report):
    // unparseable route report with child exit 0 → fail-closed exit (the
    // variant itself is asserted by QA005_MalformedRouteReport_OwnVariant).
    [Fact]
    public void QA016_MalformedRouteReport_ChildExitZero_AggregatorExitsFailClosed()
    {
        var scratch = SpikePaths.TestScratch("qa016-malformed");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        var aRep = Path.Combine(scratch, "route-a.json");
        var bRep = Path.Combine(scratch, "route-b.json");
        File.WriteAllText(aRep, RouteReportJson("A", runId));
        File.WriteAllText(bRep, "{ this is : not json [");

        var (result, _) = RunAggregator(scratch, runId, aRep, 0, bRep, 0);
        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
    }

    // QA-016 positive control: a fully passing VARIANCE aggregation (suite
    // status unknown — route verdicts INCOMPLETE(suite-failure) by
    // construction, the suite channel) still exits 0, so nested in-suite
    // controller launches keep working.
    [Fact]
    public void QA016_AllPassVarianceAggregation_StillExitsZero()
    {
        var scratch = SpikePaths.TestScratch("qa016-all-pass");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        var aRep = Path.Combine(scratch, "route-a.json");
        var bRep = Path.Combine(scratch, "route-b.json");
        File.WriteAllText(aRep, RouteReportJson("A", runId));
        File.WriteAllText(bRep, RouteReportJson("B", runId));

        var (result, runReport) = RunAggregator(scratch, runId, aRep, 0, bRep, 0);
        Assert.True(result.ExitCode == ExitCodes.RouteProbesPassed,
            $"all-pass variance aggregation must keep exit 0 (QA-016): exit {result.ExitCode?.ToString() ?? result.Signal}: {result.StdErr}");
        using var doc = Launch.Report(runReport);
        foreach (var p in doc.RootElement.GetProperty("deterministic").GetProperty("per_probe_results").EnumerateArray())
        {
            Assert.Equal("pass", p.GetProperty("status").GetString());
        }
    }

    // ------------------------------------------------------------------ QA-017

    // QA-017: the PROVISION phase runs under the real per-phase setsid
    // supervisor. The TEST constructs the hang externally: a huge sparse file
    // planted at the run root's staged-archive path makes provisioning's
    // digest check (sha256sum) run far past the shrunken wall-clock bound —
    // the supervisor must kill the detached provision session at the deadline
    // and the synthetic INCOMPLETE must carry PROVISION-phase attribution.
    // QA-018 class fix: after the kill, no process of the killed phase's
    // session may survive (swept via /proc cmdlines referencing the fault).
    [Fact]
    public void QA017_ProvisionPhaseHang_SupervisorKills_WithPhaseAttribution_NoSurvivors()
    {
        var runRoot = SpikePaths.TestScratch("qa017-provision-hang");
        var sparseArchive = Path.Combine(runRoot, "z3-4.12.1-x64-glibc-2.35.zip");
        using (var fs = new FileStream(sparseArchive, FileMode.Create, FileAccess.Write))
        {
            fs.SetLength(400L * 1024 * 1024 * 1024); // sparse: ~0 bytes on disk, minutes to hash
        }

        const int boundSeconds = 30;
        var configCopy = Path.Combine(runRoot, "run-config.json");
        File.WriteAllText(configCopy, File.ReadAllText(SpikePaths.P("config", "run-config.json"))
            .Replace("\"wall_clock_bound_seconds\": 1800", $"\"wall_clock_bound_seconds\": {boundSeconds}"));

        var runReport = Path.Combine(runRoot, "run-report.json");
        var result = Launch.Script("scripts/run-spike.sh", null,
            "--config", configCopy, "--run-root", runRoot, "--out", runReport);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.DurationMs >= boundSeconds * 1000,
            $"controller returned after {result.DurationMs}ms — before the {boundSeconds}s bound; the provision phase was not actually supervised (QA-017)");
        Assert.True(result.DurationMs < 240_000, "controller overran the deadline by more than the TERM->KILL grace window");
        using var doc = Launch.Report(runReport);
        var text = doc.RootElement.GetRawText();
        Assert.Contains("INCOMPLETE", text);
        Assert.Contains("wall-clock", text);
        Assert.Matches("SIG(TERM|KILL)", text);
        Assert.Contains("provision", text); // typed PER-PHASE attribution, not the generic outer backstop

        // QA-018 class fix: sweep — no process of the killed provision session
        // survives (nothing still references the planted fault file).
        AssertNoSurvivingProcessReferencing(sparseArchive);
    }

    // ------------------------------------------------------------------ QA-018

    // QA-018(b): a nested controller run inherits the PARENT's remaining
    // wall-clock budget via SPIKE_PARENT_DEADLINE — min(own bound, parent
    // remaining). With the committed 1800s bound and a parent deadline a few
    // seconds away, the run must be killed almost immediately with the
    // synthetic wall-clock INCOMPLETE. On reversion (cap dropped), the healthy
    // variance run completes with exit 0 and this test fails.
    [Fact]
    public void QA018_NestedRun_InheritsParentDeadlineCap()
    {
        var runRoot = SpikePaths.TestScratch("qa018-parent-cap");
        var uptime = (long)double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0],
            System.Globalization.CultureInfo.InvariantCulture);
        var env = new Dictionary<string, string>
        {
            ["SPIKE_PARENT_DEADLINE"] = (uptime + 6).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        var runReport = Path.Combine(runRoot, "run-report.json");
        var result = Launch.Script("scripts/run-spike.sh", env,
            "--run-root", runRoot, "--out", runReport);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.DurationMs >= 6_000,
            $"controller returned after {result.DurationMs}ms — before the 6s parent deadline; nothing was capped (QA-018)");
        Assert.True(result.DurationMs < 180_000,
            $"controller ran {result.DurationMs}ms past a 6s parent deadline — the inherited cap did not bind (QA-018)");
        using var doc = Launch.Report(runReport);
        var text = doc.RootElement.GetRawText();
        Assert.Contains("INCOMPLETE", text);
        Assert.Contains("wall-clock", text);
    }

    // ------------------------------------------------------------------ QA-019

    // QA-019: committed samples are actually run through the closed-schema
    // validator — masked never means unvalidated.
    [Theory]
    [InlineData("run-report.sample.json")]
    [InlineData("run-report.canonical.sample.json")]
    public void QA019_CommittedSample_PassesClosedSchemaValidation(string name)
    {
        var path = SpikePaths.P("evidence", "samples", name);
        Assert.True(File.Exists(path), $"committed sample {name} missing — regenerate the PAIR via scripts/regen-sample.sh (DD-008)");
        EvidenceSchema.ValidateSchemaFile(SchemaPath, RegistryPath);
        EvidenceSchema.ValidateReport(File.ReadAllText(path), SchemaPath); // throws on any violation
        using var doc = SpikePaths.Json(path);
        Assert.Equal("run-report", doc.RootElement.GetProperty("kind").GetString());
    }

    // QA-019 (verdict-recomputation consistency, no circularity): recompute
    // each committed sample's route verdicts from its OWN per_probe_results +
    // final_suite_status through the real aggregator formula and assert they
    // equal the recorded (masked-in-equality) route_verdicts.
    [Theory]
    [InlineData("run-report.sample.json")]
    [InlineData("run-report.canonical.sample.json")]
    public void QA019_CommittedSample_RouteVerdicts_RecomputeFromOwnProbeResults(string name)
    {
        var path = SpikePaths.P("evidence", "samples", name);
        using var doc = SpikePaths.Json(path);
        var det = doc.RootElement.GetProperty("deterministic");

        var results = det.GetProperty("per_probe_results").EnumerateArray()
            .Select(p => new ProbeResult(
                new ProbeKey(p.GetProperty("probe").GetString()!, p.GetProperty("route").GetString()!),
                Enum.Parse<ProbeStatus>(p.GetProperty("status").GetString()!, ignoreCase: true)))
            .ToList();
        var suiteStatus = det.GetProperty("final_suite_status").GetString() switch
        {
            "success" => SuiteStatus.Success,
            "failure" => SuiteStatus.Failure,
            _ => SuiteStatus.Unknown,
        };

        var manifest = ProbeManifest.Load(SpikePaths.P("manifest", "probe-manifest.json"));
        foreach (var route in manifest.MandatoryRoutes)
        {
            var computed = VerdictAggregator.ComputeRouteVerdict(manifest, route, results, suiteStatus);
            var recorded = det.GetProperty("route_verdicts").EnumerateArray()
                .Single(v => v.GetProperty("route").GetString() == route);
            Assert.True(StateText(computed) == recorded.GetProperty("state").GetString(),
                $"{name} route {route}: recorded state '{recorded.GetProperty("state").GetString()}' diverges from the verdict recomputed from the sample's own probe results ('{StateText(computed)}') — the masked subtree is inconsistent (QA-019)");
            var recordedReason = recorded.GetProperty("verdict_reason");
            if (computed.Reason is null)
            {
                Assert.Equal(JsonValueKind.Null, recordedReason.ValueKind);
            }
            else
            {
                Assert.Equal(ReasonVariant(computed.Reason), recordedReason.GetProperty("variant").GetString());
            }
        }
    }

    // QA-019: a bootstrap pass-1 pair must never be silently citable as final.
    // The canonical sample must record final_suite_status=success; the ONLY
    // tolerated exception is the documented two-pass bootstrap intermediate
    // (final_suite_status=failure) and ONLY while the ADR's adr_lint block
    // still claims nothing (all pending). A variance-mode 'unknown' committed
    // as canonical is never acceptable.
    [Fact]
    public void QA019_CanonicalSample_SuiteStatusSuccess_OrUncitedBootstrapIntermediate()
    {
        using var doc = SpikePaths.Json(CanonicalSamplePath);
        var status = doc.RootElement.GetProperty("deterministic").GetProperty("final_suite_status").GetString();
        if (status == "success")
        {
            // Committed convergence state: suite-attested success, both routes COMPATIBLE.
            foreach (var rv in doc.RootElement.GetProperty("deterministic").GetProperty("route_verdicts").EnumerateArray())
            {
                Assert.Equal("COMPATIBLE", rv.GetProperty("state").GetString());
            }
        }
        else
        {
            Assert.True(status == "failure",
                $"canonical sample records final_suite_status={status} — a variance-mode sample committed as canonical is never acceptable (QA-006/QA-019)");
            Assert.True(AdrLintBlockAllPending(),
                "canonical sample records final_suite_status=failure (bootstrap pass-1) while ADR-0001's adr_lint block carries a non-pending claim — a bootstrap intermediate is never citable as final (QA-019)");
        }
    }

    // ------------------------------------------------------------------ QA-021

    // QA-021 class fix: after any nested/variance controller runs, out/current
    // still points at THIS suite's canonical run context — nested runs may
    // never republish the shared pointer, and it never points into a
    // test-scratch fixture root.
    [Fact]
    public void QA021_OutCurrent_PointsAtCanonicalRunContext_NeverAtTestScratch()
    {
        var runsettings = Path.Combine(SpikePaths.SpikeRoot, "out", "current", "spike.runsettings");
        Assert.True(File.Exists(runsettings),
            "out/current/spike.runsettings missing — only canonical controller runs publish it; run scripts/run-spike.sh (QA-021)");
        var published = SpikePaths.Xml(runsettings).Descendants("SPIKE_RUN_CONTEXT").Single().Value;
        Assert.Equal(RunContext.RequirePath(), published);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}test-scratch{Path.DirectorySeparatorChar}", published);
    }

    // ------------------------------------------------------------------ QA-022

    // QA-022(1): the QA-011 per-probe sub-nonce tag filter DISCRIMINATES — a
    // same-nonce entry carrying a foreign probe tag WOULD be counted if the
    // filter were removed; this test fails on that reversion.
    [Fact]
    public void QA022_SentinelLedger_TagFilter_ExcludesForeignProbeTags()
    {
        var scratch = SpikePaths.TestScratch("qa022-ledger-tag");
        var ledger = Path.Combine(scratch, "ledger.json");
        const string nonce = "nonce-qa022-tagfilter";
        File.WriteAllText(ledger,
            $"{{ \"nonce\": \"{nonce}\", \"entries\": [ " +
            $"{{ \"nonce\": \"{nonce}\", \"argv\": [\"probe:P05-own00001\", \"-smt2\"] }}, " +
            $"{{ \"nonce\": \"{nonce}\", \"argv\": [\"probe:P99-foreign1\", \"-smt2\"] }}, " +
            $"{{ \"nonce\": \"nonce-OTHER-RUN\", \"argv\": [\"probe:P05-own00001\", \"-smt2\"] }} ] }}\n");

        var tagged = HarnessCore.ReadSentinelLedger(ledger, "P05-own00001");
        Assert.Equal(1, tagged.EntriesForNonce); // NOT 2: the foreign probe tag is excluded (QA-011)
        Assert.Equal(1, tagged.ForeignEntries);  // the foreign-NONCE entry is never counted for this run

        var untagged = HarnessCore.ReadSentinelLedger(ledger, null);
        Assert.Equal(2, untagged.EntriesForNonce); // run-nonce total, both probe tags
    }

    // QA-022(2): aggregation-level per-partition P01 attribution — a restore
    // receipt with MIXED p01_partitions exits must yield P01(A)=pass and
    // P01(B)=fail in the EMITTED run report, route B failing on P01(B) by
    // manifest order while route A's only obstacle is the suite channel.
    // (The run-level fail-closed stop on restore failure is a recorded
    // residual in ADR-0001 — attribution, not verdict-under-broken-restore,
    // is the preserved property.)
    [Fact]
    public void QA022_MixedP01Partitions_AttributedPerRoute_InEmittedReport()
    {
        var scratch = SpikePaths.TestScratch("qa022-p01-mixed");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId, p01AExit: 0, p01BExit: 7);
        var aRep = Path.Combine(scratch, "route-a.json");
        var bRep = Path.Combine(scratch, "route-b.json");
        File.WriteAllText(aRep, RouteReportJson("A", runId));
        File.WriteAllText(bRep, RouteReportJson("B", runId));

        var (result, runReport) = RunAggregator(scratch, runId, aRep, 0, bRep, 0);
        Assert.NotEqual(ExitCodes.RouteProbesPassed, result.ExitCode); // fail-closed (QA-016)
        using var doc = Launch.Report(runReport);

        var perProbe = doc.RootElement.GetProperty("deterministic").GetProperty("per_probe_results").EnumerateArray().ToList();
        Assert.Equal("pass", perProbe.Single(p => p.GetProperty("probe").GetString() == "P01" && p.GetProperty("route").GetString() == "A").GetProperty("status").GetString());
        Assert.Equal("fail", perProbe.Single(p => p.GetProperty("probe").GetString() == "P01" && p.GetProperty("route").GetString() == "B").GetProperty("status").GetString());

        var aReason = RouteVerdict(doc, "A").GetProperty("verdict_reason");
        var bReason = RouteVerdict(doc, "B").GetProperty("verdict_reason");
        Assert.Equal("suite-failure", aReason.GetProperty("variant").GetString()); // A unaffected by B's lock fault
        Assert.Equal("probe-failure", bReason.GetProperty("variant").GetString());
        Assert.Equal("P01(B)", bReason.GetProperty("first_failing_probe_by_manifest_order").GetString());
    }

    // QA-022(3): the run-level unadjudicated-failure mapping (QA-005's "own
    // variant" branch in the aggregator) — a route report carrying an
    // UnadjudicatedInProcessFailure adjudication record plus its failing probe
    // must surface the unadjudicated-failure verdict variant at the run level.
    [Fact]
    public void QA022_RunLevelUnadjudicatedFailure_MappedFromRouteAdjudicationRecord()
    {
        var scratch = SpikePaths.TestScratch("qa022-unadj");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        var aRep = Path.Combine(scratch, "route-a.json");
        var bRep = Path.Combine(scratch, "route-b.json");
        File.WriteAllText(aRep, RouteReportJson("A", runId));
        File.WriteAllText(bRep, RouteReportJson("B", runId, failingProbe: "P06", withUnadjudicatedRecord: true));

        var (result, runReport) = RunAggregator(scratch, runId, aRep, 0, bRep, ExitCodes.ProbeFailure);
        Assert.Equal(ExitCodes.ProbeFailure, result.ExitCode); // fail-closed (QA-016)
        using var doc = Launch.Report(runReport);
        var b = RouteVerdict(doc, "B");
        Assert.Equal("UNADJUDICATED_IN_PROCESS_FAILURE", b.GetProperty("state").GetString());
        var reason = b.GetProperty("verdict_reason");
        Assert.Equal("unadjudicated-failure", reason.GetProperty("variant").GetString());
        Assert.Equal("P06(B)", reason.GetProperty("probe").GetString());
    }

    // ------------------------------------------------------------------ QA-024

    // QA-024: the schema-validating read-out regen-sample.sh depends on — the
    // aggregator's --print-suite-status mode reads a report through the
    // trust-anchor + closed-schema path and prints the suite status and
    // per-route verdict variants (never a line-grep).
    [Fact]
    public void QA024_PrintSuiteStatus_SchemaValidatingReadout_OnCommittedCanonicalSample()
    {
        var aggregator = RunContext.Resolve("SpikeAggregator");
        var env = EnvProfiles.For("harness", RunContext.RunRoot());
        var result = Launch.Dll(aggregator.AbsolutePath, env,
            "--schema", SchemaPath, "--registry", RegistryPath,
            "--print-suite-status", CanonicalSamplePath);
        Assert.True(result.ExitCode == 0, $"--print-suite-status failed on the committed canonical sample: {result.StdErr}");
        Assert.Contains("final_suite_status=success", result.StdOut);
        Assert.Contains("route_verdict route=A state=COMPATIBLE variant=none", result.StdOut);
        Assert.Contains("route_verdict route=B state=COMPATIBLE variant=none", result.StdOut);

        var variance = Launch.Dll(aggregator.AbsolutePath, env,
            "--schema", SchemaPath, "--registry", RegistryPath,
            "--print-suite-status", VarianceSamplePath);
        Assert.True(variance.ExitCode == 0, $"--print-suite-status failed on the committed variance sample: {variance.StdErr}");
        Assert.Contains("final_suite_status=unknown", variance.StdOut);
        Assert.Contains("variant=suite-failure", variance.StdOut);
    }

    [Fact]
    public void QA024_PrintSuiteStatus_MalformedReport_FailsClosed()
    {
        var scratch = SpikePaths.TestScratch("qa024-malformed-readout");
        var bogus = Path.Combine(scratch, "not-a-report.json");
        File.WriteAllText(bogus, "{ this is : not json [");
        var aggregator = RunContext.Resolve("SpikeAggregator");
        var env = EnvProfiles.For("harness", RunContext.RunRoot());
        var result = Launch.Dll(aggregator.AbsolutePath, env,
            "--schema", SchemaPath, "--registry", RegistryPath,
            "--print-suite-status", bogus);
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("final_suite_status=", result.StdOut);
    }

    // ---------------------------------------------------------------- helpers

    private static string NewRunId() => $"runid-qa2-{Guid.NewGuid():N}"[..26];

    private static void ProvisionInto(string runRoot)
    {
        var provision = Launch.Script("scripts/provision-z3.sh", null, "--run-root", runRoot);
        Assert.True(provision.ExitCode == 0, $"provisioning failed: {provision.StdErr}");
    }

    private static JsonElement RouteVerdict(JsonDocument runReport, string route) =>
        runReport.RootElement.GetProperty("deterministic").GetProperty("route_verdicts").EnumerateArray()
            .Single(v => v.GetProperty("route").GetString() == route);

    private static (LaunchResult Result, string RunReportPath) RunAggregator(
        string scratch, string runId, string aRep, int aExit, string bRep, int bExit)
    {
        var aggregator = RunContext.Resolve("SpikeAggregator");
        var env = EnvProfiles.For("harness", RunContext.RunRoot());
        var runReport = Path.Combine(scratch, "run-report.json");
        var result = Launch.Dll(aggregator.AbsolutePath, env,
            "--manifest", SpikePaths.P("manifest", "probe-manifest.json"),
            "--schema", SchemaPath,
            "--registry", RegistryPath,
            "--run-id", runId, "--nonce", "nonce-qa2", "--run-root", scratch,
            "--restore-receipt", Path.Combine(scratch, "restore-receipt.json"),
            "--build-receipt", Path.Combine(scratch, "build-receipt.json"),
            "--suite-status", "unknown",
            "--report", $"A={aRep}:{aExit}", "--report", $"B={bRep}:{bExit}",
            "--out", runReport, "--aggregation-receipt", Path.Combine(scratch, "agg.json"));
        Assert.True(File.Exists(runReport), $"aggregator produced no run report: {result.StdOut} {result.StdErr}");
        return (result, runReport);
    }

    /// <summary>Full nine-probe route-child report (P03, P05–P12), schema-valid.</summary>
    private static string RouteReportJson(string route, string runId, string? failingProbe = null, bool withUnadjudicatedRecord = false)
    {
        var probes = new[] { "P03", "P05", "P06", "P07", "P08", "P09", "P10", "P11", "P12" };
        var entries = string.Join(",\n", probes.Select(p =>
            $"      {{ \"probe\": \"{p}\", \"route\": \"{route}\", \"status\": \"{(p == failingProbe ? "fail" : "pass")}\", \"detail\": null }}"));
        var records = withUnadjudicatedRecord
            ? ",\n    \"adjudication_records\": [ { " +
              $"\"adjudication_record_id\": \"unadj-{failingProbe}-{route}\", " +
              $"\"route\": \"{route}\", " +
              "\"terminal_state\": \"UnadjudicatedInProcessFailure\", " +
              $"\"probe\": \"{failingProbe}\", " +
              "\"variant\": \"typed-exception\", " +
              "\"stage\": \"in-process\", " +
              "\"minimal_inputs\": \"fixtures/ok.dfy\", " +
              "\"typed_diagnostic\": \"System.MissingMethodException: induced (QA-022)\" } ]"
            : "";
        return "{\n" +
               "  \"evidence_schema_version\": 2,\n" +
               $"  \"evidence_schema_sha256\": \"{SpecConstants.EvidenceSchemaSha256}\",\n" +
               $"  \"probe_manifest_sha256\": \"{SpecConstants.ProbeManifestSha256}\",\n" +
               $"  \"run_id\": \"{runId}\",\n" +
               "  \"kind\": \"route-report\",\n" +
               "  \"binding_identity\": {\n" +
               "    \"run_directory\": \"out/qa2\", \"git_commit_id\": \"abc\", \"git_dirty_flag\": false,\n" +
               "    \"host_rid\": \"linux-x64\", \"sdk_version\": \"10.0.302\", \"actual_runtime_version\": \"10.0.2\"\n" +
               "  },\n" +
               "  \"deterministic\": {\n" +
               "    \"per_probe_results\": [\n" + entries + "\n    ],\n" +
               "    \"final_suite_status\": \"unknown\"" + records + "\n" +
               "  },\n" +
               "  \"volatile\": {}\n" +
               "}\n";
    }

    private static void WriteReceipts(string dir, string runId, int p01AExit = 0, int p01BExit = 0)
    {
        File.WriteAllText(Path.Combine(dir, "restore-receipt.json"),
            $"{{ \"run_id\": \"{runId}\", \"nonce\": \"nonce-qa2\", \"exit\": 0, \"argv\": [\"dotnet\",\"restore\"],\n" +
            $"  \"p01_partitions\": {{ \"A\": {{ \"projects\": [], \"exit\": {p01AExit} }}, \"B\": {{ \"projects\": [], \"exit\": {p01BExit} }} }},\n" +
            "  \"lock_sha256\": {} }\n");
        File.WriteAllText(Path.Combine(dir, "build-receipt.json"),
            $"{{ \"run_id\": \"{runId}\", \"nonce\": \"nonce-qa2\", \"exit\": 0, \"sdk_version\": \"{SpecConstants.SdkPin}\", \"artifacts\": [] }}\n");
    }

    private static string StateText(RouteOutcome outcome) => outcome.State switch
    {
        RouteState.Compatible => "COMPATIBLE",
        RouteState.Incomplete => "INCOMPLETE",
        RouteState.Incompatible => $"INCOMPATIBLE({outcome.Class})",
        RouteState.UpstreamDefect => "UPSTREAM_DEFECT",
        _ => "UNADJUDICATED_IN_PROCESS_FAILURE",
    };

    private static string ReasonVariant(VerdictReason reason) => reason switch
    {
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

    private static bool AdrLintBlockAllPending()
    {
        var lines = File.ReadAllLines(SpikePaths.Repo("docs", "adr", "ADR-0001-dafny-integration-boundary.md"));
        var inYaml = false;
        var decisionPending = false;
        var verdicts = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inYaml = !inYaml;
                continue;
            }
            if (!inYaml)
            {
                continue;
            }
            var commentIdx = line.IndexOf(" #", StringComparison.Ordinal);
            if (commentIdx >= 0)
            {
                line = line[..commentIdx].Trim();
            }
            if (line.StartsWith("boundary_decision:", StringComparison.Ordinal))
            {
                decisionPending = line["boundary_decision:".Length..].Trim() == "pending";
            }
            else if (line.StartsWith("verdict:", StringComparison.Ordinal))
            {
                verdicts.Add(line["verdict:".Length..].Trim());
            }
        }
        return decisionPending && verdicts.Count > 0 && verdicts.All(v => v == "pending");
    }

    private static void AssertNoSurvivingProcessReferencing(string pathFragment)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (!ProcessesReferencing(pathFragment).Any())
            {
                return;
            }
            Thread.Sleep(500);
        }
        var survivors = ProcessesReferencing(pathFragment).ToList();
        Assert.True(survivors.Count == 0,
            $"processes of the killed phase's session survived the supervisor kill (QA-018): {string.Join(", ", survivors)}");
    }

    private static IEnumerable<string> ProcessesReferencing(string pathFragment)
    {
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(dir);
            if (!name.All(char.IsAsciiDigit))
            {
                continue;
            }
            string cmdline;
            try
            {
                cmdline = File.ReadAllText(Path.Combine(dir, "cmdline")).Replace('\0', ' ');
            }
            catch (Exception)
            {
                continue;
            }
            if (cmdline.Contains(pathFragment, StringComparison.Ordinal))
            {
                yield return $"pid {name}: {cmdline.Trim()}";
            }
        }
    }
}
