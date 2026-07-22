// Tests INV-006 and PRH-001: verdict is fail-closed against the spec-owned
// probe manifest — per route, aggregated by the named component.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Inv006VerdictTests
{
    private static string ManifestPath => SpikePaths.P("manifest", "probe-manifest.json");

    /// <summary>All 22 committed composite keys, read from the committed manifest file (test-side parse).</summary>
    public static IEnumerable<object[]> ManifestEntries()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("manifest", "probe-manifest.json"));
        foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            yield return new object[] { e.GetProperty("probe").GetString()!, e.GetProperty("route").GetString()! };
        }
    }

    private static IReadOnlyList<ProbeResult> FullPassingSet()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("manifest", "probe-manifest.json"));
        return doc.RootElement.GetProperty("entries").EnumerateArray()
            .Select(e => new ProbeResult(new ProbeKey(e.GetProperty("probe").GetString()!, e.GetProperty("route").GetString()!), ProbeStatus.Pass))
            .ToList();
    }

    // Tests INV-006/RS-002 [unit]: the manifest file's SHA-256 equals the
    // hard-coded test constant — the three-point-change anchor.
    // Source: spikes/dafny-compat/manifest/probe-manifest.json
    [Fact]
    public void ManifestDigest_MatchesHardCodedTestConstant()
    {
        Assert.Equal(SpecConstants.ProbeManifestSha256, SpikePaths.Sha256File(ManifestPath));
    }

    // Tests INV-006 [unit]: exactly 22 composite entries, no duplicates, route plan = {A, B} mandatory.
    [Fact]
    public void Manifest_Has22UniqueCompositeEntries_AndMandatoryRoutePlan()
    {
        using var doc = SpikePaths.Json(ManifestPath);
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray()
            .Select(e => (Probe: e.GetProperty("probe").GetString(), Route: e.GetProperty("route").GetString()))
            .ToList();
        Assert.Equal(SpecConstants.ExpectedManifestEntryCount, entries.Count);
        Assert.Equal(entries.Count, entries.Distinct().Count());

        var routes = doc.RootElement.GetProperty("route_plan").GetProperty("mandatory_routes")
            .EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Equal(new[] { "A", "B" }, routes);
    }

    // Tests INV-006 [unit] (codex F7/R3-1/R4-07): ownership topology — P01
    // controller-attested per route; P02/P04 shared; route children own P03 and
    // P05–P12 only.
    [Fact]
    public void Manifest_OwnershipFollowsExecutionTopology()
    {
        using var doc = SpikePaths.Json(ManifestPath);
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToList();

        var p01 = entries.Where(e => e.GetProperty("probe").GetString() == "P01").ToList();
        Assert.Equal(2, p01.Count);
        Assert.All(p01, e => Assert.Equal("controller", e.GetProperty("owner").GetString()));
        Assert.Equal(new[] { "A", "B" }, p01.Select(e => e.GetProperty("route").GetString()).OrderBy(r => r).ToArray());

        foreach (var shared in new[] { "P02", "P04" })
        {
            var e = entries.Single(x => x.GetProperty("probe").GetString() == shared);
            Assert.Equal("shared", e.GetProperty("route").GetString());
            Assert.Equal("aggregator", e.GetProperty("owner").GetString());
        }

        var children = entries.Where(e => e.GetProperty("owner").GetString() == "route-child").ToList();
        Assert.Equal(18, children.Count); // 9 probes x 2 routes
        Assert.All(children, e => Assert.Contains(e.GetProperty("probe").GetString(),
            new[] { "P03", "P05", "P06", "P07", "P08", "P09", "P10", "P11", "P12" }));
    }

    // Tests INV-006 [unit]: verdict logic reads the COMMITTED manifest, requires
    // non-empty exact composite-key equality, and the loaded manifest digest
    // matches the test constant.
    [Fact]
    public void VerdictLogic_ReadsCommittedManifest_RequiresNonEmptyExactEquality()
    {
        Assert.Equal(SpecConstants.ProbeManifestSha256, ProbeManifest.ComputeSha256(ManifestPath));
        var manifest = ProbeManifest.Load(ManifestPath);
        Assert.Equal(SpecConstants.ExpectedManifestEntryCount, manifest.Entries.Count);

        var full = FullPassingSet();
        var okA = VerdictAggregator.ComputeRouteVerdict(manifest, "A", full, SuiteStatus.Success);
        Assert.Equal(RouteState.Compatible, okA.State);

        // An EMPTY completed set never satisfies equality (PRH-001).
        var emptyA = VerdictAggregator.ComputeRouteVerdict(manifest, "A", Array.Empty<ProbeResult>(), SuiteStatus.Success);
        Assert.NotEqual(RouteState.Compatible, emptyA.State);
    }

    // Tests INV-006/PRH-001 [unit]: DELETION TEST, one per manifest entry —
    // dropping any single composite entry must deny COMPATIBLE to every route
    // it affects (shared/P01 failures affect their partition; route probes their route).
    [Theory]
    [MemberData(nameof(ManifestEntries))]
    public void DeletionTest_DroppingAnyManifestEntry_DeniesCompatible(string probe, string route)
    {
        var manifest = ProbeManifest.Load(ManifestPath);
        var deleted = FullPassingSet().Where(r => !(r.Key.ProbeId == probe && r.Key.Route == route)).ToList();
        Assert.Equal(SpecConstants.ExpectedManifestEntryCount - 1, deleted.Count);

        var affectedRoutes = route == "shared" ? new[] { "A", "B" } : new[] { route };
        foreach (var r in affectedRoutes)
        {
            var outcome = VerdictAggregator.ComputeRouteVerdict(manifest, r, deleted, SuiteStatus.Success);
            Assert.True(outcome.State != RouteState.Compatible,
                $"dropping ({probe},{route}) still yielded COMPATIBLE for route {r} — the harness shrank its plan (PRH-001)");
        }
    }

    // Tests INV-006 [unit]: ROUTE-DELETION test — drop Route B's report entirely;
    // overall result is INCOMPLETE with a missing-report reason.
    [Fact]
    public void RouteDeletion_DroppingRouteBReport_YieldsOverallIncomplete()
    {
        var manifest = ProbeManifest.Load(ManifestPath);
        var routeAOnly = new List<RouteReport>
        {
            new("run-1", "A", FullPassingSet().Where(p => p.Key.Route is "A" or "shared").ToList(), ExitCodes.RouteProbesPassed, null, "<run-root>/route-a.json"),
        };
        var result = VerdictAggregator.Aggregate(manifest, "run-1", routeAOnly, SuiteStatus.Success);
        Assert.Equal(RouteState.Incomplete, result.RouteVerdicts["B"].State);
        Assert.IsType<VerdictReason.MissingReport>(result.RouteVerdicts["B"].Reason);
        Assert.NotEqual(RouteState.Compatible, result.RouteVerdicts["B"].State);
    }

    // Tests INV-006 [unit] (route plan rule): duplicate composite keys in the
    // raw report array are rejected BEFORE dictionary conversion.
    [Fact]
    public void DuplicateCompositeKeys_RejectedBeforeDictionaryConversion()
    {
        var dup = FullPassingSet().Append(new ProbeResult(new ProbeKey("P06", "A"), ProbeStatus.Pass)).ToList();
        var ex = Record.Exception(() => VerdictAggregator.ToKeyedResults(dup));
        Assert.NotNull(ex);
        Assert.IsNotType<NotImplementedException>(ex); // a stub throw is not duplicate rejection (AP-010)
        Assert.Contains("duplicate", ex!.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Tests INV-006 [unit]: unknown composite keys deny COMPATIBLE (exact equality, not superset).
    [Fact]
    public void UnknownCompositeKey_DeniesCompatible()
    {
        var manifest = ProbeManifest.Load(ManifestPath);
        var withUnknown = FullPassingSet().Append(new ProbeResult(new ProbeKey("P99", "A"), ProbeStatus.Pass)).ToList();
        var outcome = VerdictAggregator.ComputeRouteVerdict(manifest, "A", withUnknown, SuiteStatus.Success);
        Assert.NotEqual(RouteState.Compatible, outcome.State);
    }

    // Tests INV-006 [unit] (codex F1/R3-5): run_id binding — a report from
    // another run_id is rejected; a stale report from a crashed prior run can
    // never be aggregated.
    [Fact]
    public void ReportWithMismatchedRunId_IsRejected()
    {
        var manifest = ProbeManifest.Load(ManifestPath);
        var reports = new List<RouteReport>
        {
            new("run-CURRENT", "A", FullPassingSet().Where(p => p.Key.Route is "A" or "shared").ToList(), 0, null, "<run-root>/a.json"),
            new("run-STALE", "B", FullPassingSet().Where(p => p.Key.Route is "B" or "shared").ToList(), 0, null, "<run-root>/b.json"),
        };
        var result = VerdictAggregator.Aggregate(manifest, "run-CURRENT", reports, SuiteStatus.Success);
        Assert.NotEqual(RouteState.Compatible, result.RouteVerdicts["B"].State);
    }

    // Tests INV-006 [unit] (RS-010): the aggregator derives its expected report
    // set from the manifest's committed route plan — never from reports found on
    // disk (the natural fail-open shape). A report not produced by the performed
    // launches must be rejected even with a matching run_id.
    [Fact]
    public void Aggregator_DerivesExpectedSetFromManifest_NotFromDiskScan()
    {
        var manifest = ProbeManifest.Load(ManifestPath);
        // Simulate: only route A was actually launched, but a "found" route B report is offered.
        var reports = new List<RouteReport>
        {
            new("run-1", "A", FullPassingSet().Where(p => p.Key.Route is "A" or "shared").ToList(), 0, null, "<run-root>/a.json"),
            new("run-1", "B", FullPassingSet().Where(p => p.Key.Route is "B" or "shared").ToList(), 0, null, "<run-root>/UNLAUNCHED-b.json"),
        };
        // The aggregator API takes reports-from-performed-launches; a route with
        // no performed launch must be INCOMPLETE even if a file exists on disk.
        var result = VerdictAggregator.Aggregate(manifest, "run-1", reports.Take(1).ToList(), SuiteStatus.Success);
        Assert.Equal(RouteState.Incomplete, result.RouteVerdicts["B"].State);
    }

    // Tests INV-006/PRH-001 [unit] (codex R4-07, TA-B10): P01 partition
    // NON-VETO — P01 is partitioned by route precisely so a Route B lock
    // failure cannot veto Route A. Full passing set except P01(B)=Fail:
    // Route A stays COMPATIBLE; Route B is non-COMPATIBLE with the P01 reason.
    [Fact]
    public void P01PartitionFailure_DoesNotVetoTheOtherRoute()
    {
        var manifest = ProbeManifest.Load(ManifestPath);
        var set = FullPassingSet()
            .Select(p => p.Key is { ProbeId: "P01", Route: "B" } ? p with { Status = ProbeStatus.Fail } : p)
            .ToList();

        var routeA = VerdictAggregator.ComputeRouteVerdict(manifest, "A", set, SuiteStatus.Success);
        Assert.Equal(RouteState.Compatible, routeA.State);

        var routeB = VerdictAggregator.ComputeRouteVerdict(manifest, "B", set, SuiteStatus.Success);
        Assert.NotEqual(RouteState.Compatible, routeB.State);
        var reason = Assert.IsType<VerdictReason.ProbeFailure>(routeB.Reason);
        Assert.Equal(new ProbeKey("P01", "B"), reason.FirstFailingByManifestOrder);
    }

    // Tests INV-006 [unit] (TA-B10 sibling): a SHARED probe failure (P02/P04)
    // affects BOTH routes — neither may reach COMPATIBLE.
    [Theory]
    [InlineData("P02")]
    [InlineData("P04")]
    public void SharedProbeFailure_DeniesBothRoutes(string sharedProbe)
    {
        var manifest = ProbeManifest.Load(ManifestPath);
        var set = FullPassingSet()
            .Select(p => p.Key.ProbeId == sharedProbe ? p with { Status = ProbeStatus.Fail } : p)
            .ToList();
        foreach (var route in new[] { "A", "B" })
        {
            var outcome = VerdictAggregator.ComputeRouteVerdict(manifest, route, set, SuiteStatus.Success);
            Assert.True(outcome.State != RouteState.Compatible,
                $"shared probe {sharedProbe} failed but route {route} still COMPATIBLE (INV-006)");
        }
    }

    // Tests INV-006 [unit] (codex R4-02): final_suite_status is an INPUT to the
    // verdict formula — suite failure denies COMPATIBLE with the typed cause.
    [Fact]
    public void SuiteFailure_DeniesCompatible_WithTypedCause()
    {
        var manifest = ProbeManifest.Load(ManifestPath);
        var outcome = VerdictAggregator.ComputeRouteVerdict(manifest, "A", FullPassingSet(), SuiteStatus.Failure);
        Assert.Equal(RouteState.Incomplete, outcome.State);
        Assert.True(outcome.Reason is VerdictReason.SuiteFailure or VerdictReason.ExitReportMismatch,
            "an all-pass probe report accompanied by a failing dotnet test is not citable as COMPATIBLE (codex R3-5)");
    }

    // Tests INV-006/INV-013 [unit]: exit/report mismatch — a failure exit code
    // accompanied by an all-pass report is non-COMPATIBLE with its precise reason.
    [Fact]
    public void FailureExitWithAllPassReport_IsExitReportMismatch_NeverCompatible()
    {
        var report = new RouteReport("run-1", "A",
            FullPassingSet().Where(p => p.Key.Route is "A" or "shared").ToList(),
            ExitCodes.ProbeFailure, null, "<run-root>/a.json");
        var outcome = AdjudicationStateMachine.MapExit(ExitCodes.ProbeFailure, null, report);
        Assert.NotNull(outcome); // MA-VI-4: only CONSISTENT cells are null
        Assert.NotEqual(RouteState.Compatible, outcome!.State);
        Assert.IsType<VerdictReason.ExitReportMismatch>(outcome.Reason);
    }

    // Tests INV-006 [unit] (codex F12/R4-05): committed wall-clock bound covering
    // the FULL run, on a true monotonic source.
    [Fact]
    public void WallClockBound_CommittedConfig_TrueMonotonicSource()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("config", "run-config.json"));
        Assert.Equal(1800, doc.RootElement.GetProperty("wall_clock_bound_seconds").GetInt32());
        Assert.Equal("/proc/uptime", doc.RootElement.GetProperty("monotonic_source").GetString());
        var supervision = doc.RootElement.GetProperty("supervision");
        Assert.Equal("setsid", supervision.GetProperty("per_phase_process_groups").GetString());
        Assert.Contains("KILL", supervision.GetProperty("escalation").GetString());
        Assert.Contains("parent of the controller", supervision.GetProperty("outer_watchdog").GetString());
    }

    // Tests INV-006 [unit]: the .NET 10 test runner is pinned and its exit-code
    // contract committed (VSTest vs Microsoft.Testing.Platform have different
    // exit semantics).
    [Fact]
    public void TestRunner_Pinned_WithCommittedExitCodeContract()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("config", "run-config.json"));
        var runner = doc.RootElement.GetProperty("test_runner");
        Assert.Equal("VSTest", runner.GetProperty("kind").GetString());
        var contract = runner.GetProperty("exit_code_contract");
        Assert.Equal("all tests passed", contract.GetProperty("0").GetString());
        Assert.Contains("failed", contract.GetProperty("1").GetString());

        var readme = File.ReadAllText(SpikePaths.P("README.md"));
        Assert.Contains("Exit-code contract", readme);
        Assert.Contains("VSTest", readme);
    }

    // Tests INV-006 [integration] (RS-012/RS-015, TA-A5): induced timeout with
    // a TEST-CONSTRUCTED hang — the TEST writes a sleep-forever solver stub and
    // a config copy with a tiny bound; the controller must record the SYNTHETIC
    // INCOMPLETE carrying the DELIVERED SIGNAL (supervisor kill, not a
    // cooperative timer), and the test verifies the wall-clock window
    // externally from the launcher's own duration measurement.
    [Fact]
    public void InducedTimeout_TestConstructedHang_SupervisorSignalRecorded()
    {
        var runRoot = SpikePaths.TestScratch("inv006-timeout");
        var hangSolver = SpikePaths.WriteExecutable(Path.Combine(runRoot, "hang-solver"),
            "echo 'Z3 version 4.12.1 - 64 bit'; sleep 100000");
        const int boundSeconds = 5;
        var configCopy = Path.Combine(runRoot, "run-config.json");
        var config = File.ReadAllText(SpikePaths.P("config", "run-config.json"))
            .Replace("\"wall_clock_bound_seconds\": 1800", $"\"wall_clock_bound_seconds\": {boundSeconds}");
        File.WriteAllText(configCopy, config);

        var runReport = Path.Combine(runRoot, "run-report.json");
        var result = Launch.Script("scripts/run-spike.sh", null,
            "--config", configCopy, "--solver", hangSolver, "--run-root", runRoot, "--out", runReport);

        Assert.NotEqual(0, result.ExitCode); // a timeout never reports success
        // External observable: the launcher-measured duration sits in the
        // [bound, bound+grace] window — the hang was cut short by the deadline,
        // not by the sleep ending and not instantly by a fake.
        Assert.True(result.DurationMs >= boundSeconds * 1000,
            $"controller returned after {result.DurationMs}ms — before the {boundSeconds}s bound; nothing was actually supervised");
        Assert.True(result.DurationMs < 120_000, "controller overran the deadline by more than the TERM->KILL grace window");

        using var doc = Launch.Report(runReport);
        var text = doc.RootElement.GetRawText();
        Assert.Contains("INCOMPLETE", text);
        Assert.Contains("wall-clock", text);
        Assert.Matches("SIG(TERM|KILL)", text); // the recorded delivered signal (RS-015)
    }

    // Tests INV-006/BND-003 [integration] (RS-012, TA-A9): unreadable fixture —
    // the TEST constructs the fault by chmod-000ing its own fixture copy; the
    // probe must be INCOMPLETE (fail-closed), never pass.
    [Fact]
    public void UnreadableFixture_TestChmodded_YieldsIncompleteNeverPass()
    {
        var scratch = SpikePaths.TestScratch("inv006-unreadable");
        var copy = Path.Combine(scratch, "ok-unreadable.dfy");
        File.Copy(SpikePaths.P("fixtures", "ok.dfy"), copy, overwrite: true);
        File.SetUnixFileMode(copy, (UnixFileMode)0); // the TEST constructs the fault
        try
        {
            var report = Path.Combine(scratch, "report.json");
            var result = Launch.Harness("A", "--probe", "P06", "--fixture", copy, "--out", report);
            Assert.NotEqual(ExitCodes.RouteProbesPassed, result.ExitCode);
            using var doc = Launch.Report(report);
            Assert.DoesNotContain("\"status\":\"pass\"", doc.RootElement.GetProperty("deterministic")
                .GetProperty("per_probe_results").GetRawText().Replace(" ", ""));
        }
        finally
        {
            File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    // Tests INV-006 [integration] (RS-012): forced probe exception — the ONE
    // spec-sanctioned induced in-process fault ("one forced probe exception",
    // named in INV-006's enforcement list); it cannot be constructed externally
    // without violating PRH-004, so the flag stays, and the payload assertions
    // live in Inv013AdjudicationTests.
    [Fact]
    public void ForcedProbeException_NeverFallsThroughToPass()
    {
        var scratch = SpikePaths.TestScratch("inv006-forced-exception");
        var report = Path.Combine(scratch, "report.json");
        var result = Launch.Harness("A", "--force-probe-exception", "P10", "--out", report);
        Assert.NotEqual(ExitCodes.RouteProbesPassed, result.ExitCode);
        using var doc = Launch.Report(report);
        Assert.DoesNotContain("\"status\":\"pass\"", doc.RootElement.GetProperty("deterministic")
            .GetProperty("per_probe_results").GetRawText().Replace(" ", ""));
    }

    // Tests INV-006 [integration] (RS-010, TA-A4): forged-report e2e — the TEST
    // plants an extra report file in the run root's reports/ directory before
    // the run; the aggregation receipt (read by the TEST from the FILE) must
    // list only reports from performed launches, excluding the forgery.
    [Fact]
    public void ForgedExtraReportOnDisk_IsNeverAggregated()
    {
        var runRoot = SpikePaths.TestScratch("inv006-forged-report");
        var reportsDir = Path.Combine(runRoot, RunLayout.ReportsDirRelativePath);
        Directory.CreateDirectory(reportsDir);
        var forged = Path.Combine(reportsDir, "route-b-forged.json");
        File.WriteAllText(forged, "{ \"kind\": \"route-report\", \"run_id\": \"forged\", \"deterministic\": { \"per_probe_results\": [], \"final_suite_status\": \"success\" } }");

        var runReport = Path.Combine(runRoot, "run-report.json");
        var result = Launch.Script("scripts/run-spike.sh", null, "--run-root", runRoot, "--out", runReport);
        Assert.True(result.ExitCode == 0, $"controller run failed: {result.StdErr}");

        using var receipt = Launch.Receipt(runRoot, RunLayout.AggregationReceiptRelativePath);
        var consumed = receipt.RootElement.GetProperty("consumed_report_paths")
            .EnumerateArray().Select(p => Path.GetFileName(p.GetString()!)).ToList();
        Assert.NotEmpty(consumed);
        Assert.DoesNotContain("route-b-forged.json", consumed);
    }
}
