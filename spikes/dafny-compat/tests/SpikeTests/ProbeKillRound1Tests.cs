// Probe-round killing tests — round 2026-07-22, survivors M1/M2/M3
// (.correctless/artifacts/probe-results-feature-dafny-compat-spike-06dca9.json).
// Each test pins EXACT pristine behavior that a surviving mutant broke, so the
// mutation applied to the corresponding production source makes the test fail:
//   M1 Components.cs:845  — MapExit anyFailed tri-state collapse
//                           (!= ProbeStatus.Pass -> == ProbeStatus.Fail)
//   M2 Program.cs:594     — aggregator QA-016 exit derivation excludes shared
//                           probes from the typed probe-failure exit
//   M3 HarnessCore.cs:1219 — decoys/-dir sanctioning digest comparison
//                           weakened to an 8-hex-prefix
using System.Globalization;
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

// Tests INV-006/INV-013 [unit] (probe-round M1): the MapExit exit/report
// consistency matrix treats probe status as TRI-state — an Incomplete probe is
// "not passing" for the matrix (anyFailed = Status != Pass), never collapsed
// into a binary pass/fail view. The only prior Incomplete construction
// (ProhibitionTests) went through ComputeRouteVerdict, so these matrix cells
// were uncovered.
public class ProbeKillRound1MapExitTests
{
    // M1 cell (a): child exit 0 (success) + a report that admits an INCOMPLETE
    // probe. Pristine: anyFailed=true, so the success exit is flagged
    // ExitReportMismatch (fail-closed cell). Under the mutant
    // (anyFailed = any Status==Fail), the Incomplete probe is invisible and the
    // cell degrades to COMPATIBLE — exactly the fail-open this test forbids.
    [Fact]
    public void SuccessExit_WithIncompleteProbeInReport_IsExitReportMismatch_NeverCompatible()
    {
        var probes = new List<ProbeResult>
        {
            new(new ProbeKey("P03", "A"), ProbeStatus.Pass),
            new(new ProbeKey("P06", "A"), ProbeStatus.Incomplete, "solver-unavailable (typed incomplete, not a fail)"),
        };
        var report = new RouteReport("run-pk1", "A", probes, ExitCodes.RouteProbesPassed, null, "<run-root>/route-a.json");

        var outcome = AdjudicationStateMachine.MapExit(ExitCodes.RouteProbesPassed, null, report);

        Assert.NotNull(outcome); // MA-VI-4: only CONSISTENT cells are null
        Assert.Equal(RouteState.Incomplete, outcome!.State);
        var mismatch = Assert.IsType<VerdictReason.ExitReportMismatch>(outcome.Reason);
        Assert.Equal(ExitCodes.RouteProbesPassed, mismatch.ExitCode);
    }

    // M1 cell (b): child exit 10 (probe failure) + a report whose non-passing
    // probes are all INCOMPLETE (none Fail). Pristine: anyFailed=true, the exit
    // is CONSISTENT and attribution is ProbeFailure with the first non-passing
    // probe by ordinal (P06 before P07). Under the mutant, anyFailed=false and
    // the cell is misdiagnosed as the "failure exit with an all-pass report"
    // ExitReportMismatch — wrong attribution for an honest failure exit.
    [Fact]
    public void FailureExit_WithOnlyIncompleteProbes_IsConsistentProbeFailure_NotAllPassMismatch()
    {
        var probes = new List<ProbeResult>
        {
            new(new ProbeKey("P03", "A"), ProbeStatus.Pass),
            new(new ProbeKey("P07", "A"), ProbeStatus.Incomplete, "not executed: run aborted (typed incomplete)"),
            new(new ProbeKey("P06", "A"), ProbeStatus.Incomplete, "solver-unavailable (typed incomplete)"),
        };
        var report = new RouteReport("run-pk1", "A", probes, ExitCodes.ProbeFailure, null, "<run-root>/route-a.json");

        var outcome = AdjudicationStateMachine.MapExit(ExitCodes.ProbeFailure, null, report);

        Assert.NotNull(outcome); // MA-VI-4: only CONSISTENT cells are null
        Assert.Equal(RouteState.Incomplete, outcome!.State);
        var attribution = Assert.IsType<VerdictReason.ProbeFailure>(outcome.Reason);
        Assert.Equal(new ProbeKey("P06", "A"), attribution.FirstFailingByManifestOrder);
    }
}

// Tests INV-006/QA-016 [integration] (probe-round M2): the aggregator's
// CI-facing exit contract for the SHARED-probe failure class. Every prior
// aggregator-launching test wrote a PASSING build receipt, so a failing P02
// never reached the exit-derivation block end-to-end.
[Collection(SharedStateMutatingCollection.Name)]
public class ProbeKillRound1AggregatorExitTests
{
    private static string SchemaPath => SpikePaths.P("schema", "evidence-schema.json");
    private static string RegistryPath => SpikePaths.P("schema", "schema-version-registry.json");

    // M2: a valid nonce-bound run whose BUILD receipt violates P02 (nonzero
    // build exit, or SDK-pin mismatch) while both route children exit 0 with
    // all-pass reports. P02 is the only shared probe that can be status=fail
    // (P04 faults record Incomplete; P01 is per-route), and the aggregator exit
    // must be EXACTLY the typed probe-failure code 10 — not merely nonzero.
    // Under the mutant (anyProbeFailed excludes route=="shared"), the same run
    // falls through to 20 (INCOMPLETE), breaking the typed exit contract:
    // retry-on-INCOMPLETE CI logic would retry a build-receipt violation
    // instead of hard-failing it.
    [Theory]
    [InlineData(7, SpecConstants.SdkPin, "build-exit-7")]
    [InlineData(0, "10.0.999-wrong", "sdk-pin-mismatch")]
    public void SharedP02Failure_AggregatorExitsTypedProbeFailure_Exactly10(int buildExit, string sdkVersion, string label)
    {
        var scratch = SpikePaths.TestScratch($"pk1-m2-{label}");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId, buildExit, sdkVersion);
        var aRep = Path.Combine(scratch, "route-a.json");
        var bRep = Path.Combine(scratch, "route-b.json");
        File.WriteAllText(aRep, RouteReportJson("A", runId));
        File.WriteAllText(bRep, RouteReportJson("B", runId));

        var (result, runReport) = RunAggregator(scratch, runId, aRep, 0, bRep, 0);

        // The exit is the TYPED probe-failure code, exactly (QA-016).
        Assert.True(result.ExitCode == ExitCodes.ProbeFailure,
            $"shared P02 failure ({label}) must exit {ExitCodes.ProbeFailure} (typed probe-failure), " +
            $"got {result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? result.Signal}: {result.StdErr}");

        // Right-reason guard: the ONLY non-passing entry in the emitted
        // 22-entry view is P02(shared) — the typed exit can come from nothing else.
        using var doc = Launch.Report(runReport);
        foreach (var p in doc.RootElement.GetProperty("deterministic").GetProperty("per_probe_results").EnumerateArray())
        {
            var isP02 = p.GetProperty("probe").GetString() == "P02" && p.GetProperty("route").GetString() == "shared";
            Assert.Equal(isP02 ? "fail" : "pass", p.GetProperty("status").GetString());
        }
        foreach (var route in new[] { "A", "B" })
        {
            var reason = RouteVerdict(doc, route).GetProperty("verdict_reason");
            Assert.Equal("probe-failure", reason.GetProperty("variant").GetString());
            Assert.Equal("P02(shared)", reason.GetProperty("first_failing_probe_by_manifest_order").GetString());
        }
    }

    // ---------------------------------------------------------------- helpers
    // (mirroring QaFixRound2Tests' aggregator staging, with the build receipt
    // parameterized — the receipt fault is the point of the test)

    private static string NewRunId() => $"runid-pk1-{Guid.NewGuid():N}"[..26];

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
            "--run-id", runId, "--nonce", "nonce-pk1", "--run-root", scratch,
            "--restore-receipt", Path.Combine(scratch, "restore-receipt.json"),
            "--build-receipt", Path.Combine(scratch, "build-receipt.json"),
            "--suite-status", "unknown",
            "--report", $"A={aRep}:{aExit}", "--report", $"B={bRep}:{bExit}",
            "--out", runReport, "--aggregation-receipt", Path.Combine(scratch, "agg.json"));
        Assert.True(File.Exists(runReport), $"aggregator produced no run report: {result.StdOut} {result.StdErr}");
        return (result, runReport);
    }

    /// <summary>Full nine-probe route-child report (P03, P05–P12), schema-valid, all-pass.</summary>
    private static string RouteReportJson(string route, string runId)
    {
        var probes = new[] { "P03", "P05", "P06", "P07", "P08", "P09", "P10", "P11", "P12" };
        var entries = string.Join(",\n", probes.Select(p =>
            $"      {{ \"probe\": \"{p}\", \"route\": \"{route}\", \"status\": \"pass\", \"detail\": null }}"));
        return "{\n" +
               "  \"evidence_schema_version\": 2,\n" +
               $"  \"evidence_schema_sha256\": \"{SpecConstants.EvidenceSchemaSha256}\",\n" +
               $"  \"probe_manifest_sha256\": \"{SpecConstants.ProbeManifestSha256}\",\n" +
               $"  \"run_id\": \"{runId}\",\n" +
               "  \"kind\": \"route-report\",\n" +
               "  \"binding_identity\": {\n" +
               "    \"run_directory\": \"out/pk1\", \"git_commit_id\": \"abc\", \"git_dirty_flag\": false,\n" +
               "    \"host_rid\": \"linux-x64\", \"sdk_version\": \"10.0.302\", \"actual_runtime_version\": \"10.0.2\"\n" +
               "  },\n" +
               "  \"deterministic\": {\n" +
               "    \"per_probe_results\": [\n" + entries + "\n    ],\n" +
               "    \"final_suite_status\": \"unknown\"\n" +
               "  },\n" +
               "  \"volatile\": {}\n" +
               "}\n";
    }

    private static void WriteReceipts(string dir, string runId, int buildExit, string sdkVersion)
    {
        File.WriteAllText(Path.Combine(dir, "restore-receipt.json"),
            $"{{ \"run_id\": \"{runId}\", \"nonce\": \"nonce-pk1\", \"exit\": 0, \"argv\": [\"dotnet\",\"restore\"],\n" +
            "  \"p01_partitions\": { \"A\": { \"projects\": [], \"exit\": 0 }, \"B\": { \"projects\": [], \"exit\": 0 } },\n" +
            "  \"lock_sha256\": {} }\n");
        File.WriteAllText(Path.Combine(dir, "build-receipt.json"),
            $"{{ \"run_id\": \"{runId}\", \"nonce\": \"nonce-pk1\", \"exit\": {buildExit}, \"sdk_version\": \"{sdkVersion}\", \"artifacts\": [] }}\n");
    }
}

// Tests INV-003/QA-015 [integration] (probe-round M3): the decoys/-dir
// sanctioning predicate consumes FULL 64-hex digests. The existing armor
// (QA-015 wrong-digest decoy, QA-022 tag filter) also passes under a predicate
// weakened to the first 8 hex chars, because arbitrary wrong digests differ in
// their first 8 chars too; this test constructs the discriminating input.
[Collection(SharedStateMutatingCollection.Name)]
public class ProbeKillRound1DecoyDigestTests
{
    // M3: meet-in-the-middle search — the expected decoy-script content derives
    // from the candidate's own run root (SanctionedDecoyScript embeds the
    // decoy-log path), so both digest sides are test-influenceable in memory:
    // hash the sanctioned script over many synthetic fake-root paths AND many
    // junk decoy contents, and find a cross pair whose digests share the first
    // 8 hex chars (32 bits) but differ as full digests. Only the winning pair
    // is materialized on disk. Pristine (full-digest comparison): the planted
    // z3 is NOT sanctioned — the startup gate fails closed exactly like the
    // QA-015 wrong-digest case. Under the prefix mutant it would be sanctioned,
    // the gate would pass, and P06 would verify normally (exit 0).
    [Fact]
    public void PrefixCollidingDecoyInDecoysDir_StillFailsGateClosed_FullDigestComparison()
    {
        var runRoot = SpikePaths.TestScratch("pk1-m3-decoy-prefix");
        ProvisionInto(runRoot);

        // Deterministic in-memory search (index-driven, no RNG). Expected side:
        // 120k synthetic fake-root paths; candidate side: content variants until
        // a 32-bit prefix collision lands (expected after ~2^32/120000 ≈ 36k).
        var searchBase = Path.Combine(runRoot, "fake-roots");
        var byPrefix = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < 120_000; i++)
        {
            var root = Path.Combine(searchBase, "r" + i.ToString(CultureInfo.InvariantCulture));
            var digest = SpikePaths.Sha256Text(HarnessCore.SanctionedDecoyScript(root));
            byPrefix.TryAdd(digest[..8], root);
        }
        string? collidingRoot = null;
        string? collidingContent = null;
        for (var j = 0; j < 4_000_000 && collidingRoot is null; j++)
        {
            var content = "#!/bin/sh\n# unsanctioned decoy variant " + j.ToString(CultureInfo.InvariantCulture) + " (probe-round M3)\nexit 3\n";
            var digest = SpikePaths.Sha256Text(content);
            if (byPrefix.TryGetValue(digest[..8], out var root)
                && SpikePaths.Sha256Text(HarnessCore.SanctionedDecoyScript(root)) != digest)
            {
                collidingRoot = root;
                collidingContent = content;
            }
        }
        Assert.True(collidingRoot is not null && collidingContent is not null,
            "no 32-bit digest-prefix collision found within the deterministic search budget (120k roots x 4M contents)");

        // Materialize ONLY the winning pair: <collidingRoot>/decoys/z3.
        var decoyDir = Path.Combine(collidingRoot!, "decoys");
        Directory.CreateDirectory(decoyDir);
        var decoy = Path.Combine(decoyDir, "z3");
        File.WriteAllText(decoy, collidingContent);
        File.SetUnixFileMode(decoy,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        // The discriminating property, asserted on the ON-DISK bytes: an 8-hex
        // prefix predicate calls this z3 sanctioned; the full comparison must not.
        var expectedFull = SpikePaths.Sha256Text(HarnessCore.SanctionedDecoyScript(collidingRoot!));
        var actualFull = SpikePaths.Sha256File(decoy);
        Assert.Equal(expectedFull[..8], actualFull[..8]);
        Assert.NotEqual(expectedFull, actualFull);

        // QA-015 staging: the colliding decoys/ dir first on PATH; the gate's
        // ambient-discovery outcome is this z3 and sanctioning is digest-only.
        var env = new Dictionary<string, string>(EnvProfiles.For("harness", runRoot).ToDictionary(kv => kv.Key, kv => kv.Value))
        {
            ["PATH"] = decoyDir + Path.PathSeparator + "/usr/bin:/bin",
        };
        var report = Path.Combine(runRoot, "report.json");
        var result = Launch.HarnessWithEnv("A", env, "--probe", "P06", "--run-root", runRoot, "--out", report);

        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
        using var doc = Launch.Report(report);
        var text = doc.RootElement.GetRawText();
        Assert.Contains("startup-gate", text);
        Assert.Contains("unsanctioned z3-named executable", text); // the exact PATH-discovery refusal branch
    }

    private static void ProvisionInto(string runRoot)
    {
        var provision = Launch.Script("scripts/provision-z3.sh", null, "--run-root", runRoot);
        Assert.True(provision.ExitCode == 0, $"provisioning failed: {provision.StdErr}");
    }
}
