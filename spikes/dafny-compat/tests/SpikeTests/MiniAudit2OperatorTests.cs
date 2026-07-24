// Mini-audit round 2 (2026-07-22) — OPERATOR-HARDENING slice: the concurrency
// cluster (disk prune / cache seed / cache concurrency), disk reclaim, and the
// behavioral-coverage findings whose round-1 fixes were locked only by
// source-scans. Every test here FAILS when its fix is reverted (QA-022
// discipline) and every concurrency assertion is DETERMINISTIC — the shell
// phases (prune-run-roots / cache-publish / cache-consume / reap-cache-staging)
// drive the REAL production functions against isolated substrate, and the
// "interleave" tests simulate the exact race states rather than sleep-racing.
//
//   MA-ID-R2-1  — pkg-cache atomicity: marker-less/torn dir is IGNORED (behavioral)
//   MA-RB-R2-1  — prune skips a LIVE run's root (concurrent live-root deletion)
//   MA-RB-R2-2  — start-of-run reap of leaked cache staging (dead pid only)
//   MA-RB-R2-3  — concurrent cold seed publishes ONE clean copy, never nested
//   MA-RB-R2-4  — prune protects the out/current TARGET; rejects --keep 0
//   MA-UC-R2-1  — the destructive prune announces the count removed
//   MA-ID-R2-3  — the canonical auto-prune (prune_run_roots) is driven directly
//   MA-UX-R2-1  — clean-runs reclaims out/test-scratch + loose files
//   MA-VI-R2-1  — aggregator: unknown/superset composite key -> all 3 surfaces malformed
//   MA-ID-R2-2  — suite-receipt PRODUCE side binds run-context (+ path==C# const)
//   MA-ID-R2-4  — provision/fetch received env: decoy-first PATH, run-root TMPDIR
using System.Globalization;
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

[Collection(SharedStateMutatingCollection.Name)]
public class MiniAudit2ConcurrencyTests
{
    private static LaunchResult Phase(params string[] args) =>
        Launch.Script("scripts/run-spike.sh", null, args);

    private static void PlantRoot(string outDir, string name, DateTime mtimeUtc, string? innerPid = null)
    {
        var d = Path.Combine(outDir, name);
        Directory.CreateDirectory(d);
        if (innerPid is not null)
        {
            File.WriteAllText(Path.Combine(d, ".inner-pid"), innerPid + "\n");
        }
        File.SetLastWriteTimeUtc(d, mtimeUtc);
    }

    // ------------------------------------------------------------------ MA-RB-R2-1 + MA-UC-R2-1

    // Tests AP-019/MA-RB-R2-1 + MA-UC-R2-1 [integration]: the canonical-start
    // prune must NEVER delete a LIVE run's root — a concurrent canonical run's
    // .inner-pid names a running controller. The live root is planted as the
    // OLDEST (keep-N would target it) with a LIVE pid; it must survive. The
    // destructive prune must also ANNOUNCE its count. On reversion (no live-pid
    // skip) the live root is deleted and this test fails.
    [Fact]
    public void MaRbR21_Prune_SkipsLiveRunRoot_AndAnnouncesCount()
    {
        var outDir = Path.Combine(SpikePaths.TestScratch("ma2-prune-live"), "out");
        Directory.CreateDirectory(outDir);
        var live = Path.Combine(outDir, "runid-live");
        PlantRoot(outDir, "runid-live", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            innerPid: Environment.ProcessId.ToString(CultureInfo.InvariantCulture)); // the test host — alive
        var olds = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            olds.Add(Path.Combine(outDir, $"runid-old{i}"));
            PlantRoot(outDir, $"runid-old{i}", new DateTime(2021, 1, i + 1, 0, 0, 0, DateTimeKind.Utc));
        }

        var result = Phase("--phase", "prune-run-roots", "--out-dir", outDir, "--keep", "2");
        Assert.Equal(0, result.ExitCode);
        Assert.True(Directory.Exists(live),
            "prune deleted a LIVE run's root (its .inner-pid names a running controller) — the concurrent live-root-deletion class (MA-RB-R2-1)");
        Assert.Equal(2, olds.Count(Directory.Exists)); // keep-2 over the 5 non-live roots
        Assert.Contains("pruned", result.StdErr); // MA-UC-R2-1: destructive default is announced
    }

    // ------------------------------------------------------------------ MA-RB-R2-4

    // Tests AP-004/MA-RB-R2-4 [integration]: prune reads out/current's
    // SPIKE_RUN_CONTEXT and protects the run root it TARGETS (not just the
    // out/current directory name), so a keep-N sweep never dangles the dev-loop
    // pointer; and --keep 0 is refused. The referent is planted as the OLDEST so
    // keep-1 would delete it absent the protection.
    [Fact]
    public void MaRbR24_Prune_ProtectsOutCurrentReferent_AndRejectsKeepZero()
    {
        var outDir = Path.Combine(SpikePaths.TestScratch("ma2-prune-referent"), "out");
        Directory.CreateDirectory(Path.Combine(outDir, "current"));
        var referent = Path.Combine(outDir, "runid-referent");
        PlantRoot(outDir, "runid-referent", new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.WriteAllText(Path.Combine(outDir, "current", "spike.runsettings"),
            "<RunSettings><RunConfiguration><EnvironmentVariables>\n" +
            $"<SPIKE_RUN_CONTEXT>{Path.Combine(referent, "run-context.json")}</SPIKE_RUN_CONTEXT>\n" +
            "</EnvironmentVariables></RunConfiguration></RunSettings>\n");
        var others = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            others.Add(Path.Combine(outDir, $"runid-o{i}"));
            PlantRoot(outDir, $"runid-o{i}", new DateTime(2021, 1, i + 1, 0, 0, 0, DateTimeKind.Utc));
        }

        var result = Phase("--phase", "prune-run-roots", "--out-dir", outDir, "--keep", "1");
        Assert.Equal(0, result.ExitCode);
        Assert.True(Directory.Exists(referent),
            "prune deleted the out/current pointer's TARGET — the dangling-referent class the fix promised to close (MA-RB-R2-4)");
        Assert.Equal(1, others.Count(Directory.Exists)); // keep-1 over the 4 non-referent roots

        var keepZero = Phase("--phase", "prune-run-roots", "--out-dir", outDir, "--keep", "0");
        Assert.Equal(20, keepZero.ExitCode); // MA-RB-R2-4: --keep 0 refused
    }

    // ------------------------------------------------------------------ MA-ID-R2-3

    // Tests MA-ID-R2-3 [integration]: the canonical-start auto-prune
    // (prune_run_roots — the SAME function the outer watchdog calls, not a
    // re-implementation) leaves a BOUNDED root count. Plant 8 roots, keep 3 ->
    // exactly the newest 3 survive. A regression in the real keep-N math
    // (unbounded / off-by-one) fails here.
    [Fact]
    public void MaIdR23_Prune_DrivesRealPruneRunRoots_LeavesBoundedCount()
    {
        var outDir = Path.Combine(SpikePaths.TestScratch("ma2-prune-bounded"), "out");
        Directory.CreateDirectory(outDir);
        var roots = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            roots.Add(Path.Combine(outDir, $"runid-b{i:D2}"));
            PlantRoot(outDir, $"runid-b{i:D2}", DateTime.UtcNow.AddMinutes(-i)); // i=0 newest
        }
        var result = Phase("--phase", "prune-run-roots", "--out-dir", outDir, "--keep", "3");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, roots.Count(Directory.Exists));
        Assert.True(Directory.Exists(roots[0]) && Directory.Exists(roots[1]) && Directory.Exists(roots[2]),
            "the newest 3 roots must survive a keep-3 prune (MA-ID-R2-3)");
    }

    // ------------------------------------------------------------------ MA-ID-R2-1

    // Tests INV-014/AP-016/MA-ID-R2-1 [integration] (behavioral, replacing the
    // round-1 source-scan lock): the shared cache is TRUSTED only with the
    // completion marker present. Seed atomically via the real publish path
    // (marker LAST), prove consume trusts it; then delete the marker (the exact
    // state a kill-mid-seed leaves — no marker) and prove the consume guard
    // IGNORES the torn dir (a fresh restore runs), never consuming it. On
    // reversion (marker guard dropped) the torn dir is consumed -> test fails.
    [Fact]
    public void MaIdR21_PkgCache_AtomicSeed_TornDirIgnoredNotConsumed()
    {
        var scratch = SpikePaths.TestScratch("ma2-pkgcache");
        var cache = Path.Combine(scratch, "cache");
        var src = Path.Combine(scratch, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "pkg.txt"), "hello");

        var pub = Phase("--phase", "cache-publish", "--out-dir", cache, "--project", src);
        Assert.Equal(0, pub.ExitCode);
        Assert.True(File.Exists(Path.Combine(cache, "packages.complete")),
            "the completion marker must be written (LAST) by the atomic seed");
        Assert.True(File.Exists(Path.Combine(cache, "packages", "pkg.txt")));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(cache, "packages"), "packages.staged.*")); // no nesting

        var trusted = Phase("--phase", "cache-consume", "--out-dir", cache);
        Assert.Equal(0, trusted.ExitCode);
        Assert.Contains("trusted", trusted.StdErr);

        // Torn/kill-mid-seed state == marker absent. The consume guard ignores it.
        File.Delete(Path.Combine(cache, "packages.complete"));
        var ignored = Phase("--phase", "cache-consume", "--out-dir", cache);
        Assert.Equal(0, ignored.ExitCode);
        Assert.Contains("ignored", ignored.StdErr);
        Assert.DoesNotContain("trusted", ignored.StdErr);
    }

    // ------------------------------------------------------------------ MA-RB-R2-3

    // Tests AP-016/AP-019/MA-RB-R2-3 [integration] (deterministic interleave
    // simulation): two concurrent cold seeders publish EXACTLY ONE clean copy,
    // never a nested/doubled cache. (a) The other seeder already published
    // (marker present) — our staged copy is dropped, the winner's copy is
    // untouched. (b) A torn partial packages dir (no marker) from a killed
    // seeder is REPLACED wholesale by mv -T, never moved INSIDE. On reversion
    // (plain mv / no marker re-check) case (b) nests staged under packages.
    [Fact]
    public void MaRbR23_ConcurrentColdSeed_OneCleanCopy_NeverNested()
    {
        var scratch = SpikePaths.TestScratch("ma2-seed-race");

        // (a) the other cold seeder already won (marker present).
        var cacheA = Path.Combine(scratch, "a");
        Directory.CreateDirectory(Path.Combine(cacheA, "packages"));
        File.WriteAllText(Path.Combine(cacheA, "packages", "run1.txt"), "run1");
        File.WriteAllText(Path.Combine(cacheA, "packages.complete"), "seeded\n");
        var srcA = Path.Combine(scratch, "srcA");
        Directory.CreateDirectory(srcA);
        File.WriteAllText(Path.Combine(srcA, "run2.txt"), "run2");
        var pubA = Phase("--phase", "cache-publish", "--out-dir", cacheA, "--project", srcA);
        Assert.Equal(0, pubA.ExitCode);
        Assert.True(File.Exists(Path.Combine(cacheA, "packages", "run1.txt")), "winner's clean copy was clobbered");
        Assert.False(File.Exists(Path.Combine(cacheA, "packages", "run2.txt")), "loser's copy leaked into the published cache");
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(cacheA, "packages"), "packages.staged.*"));

        // (b) a torn partial packages dir (no marker) — mv -T must replace, not nest.
        var cacheB = Path.Combine(scratch, "b");
        Directory.CreateDirectory(Path.Combine(cacheB, "packages"));
        File.WriteAllText(Path.Combine(cacheB, "packages", "torn.txt"), "torn");
        var srcB = Path.Combine(scratch, "srcB");
        Directory.CreateDirectory(srcB);
        File.WriteAllText(Path.Combine(srcB, "clean.txt"), "clean");
        var pubB = Phase("--phase", "cache-publish", "--out-dir", cacheB, "--project", srcB);
        Assert.Equal(0, pubB.ExitCode);
        Assert.True(File.Exists(Path.Combine(cacheB, "packages", "clean.txt")));
        Assert.False(File.Exists(Path.Combine(cacheB, "packages", "torn.txt")),
            "the torn content survived — mv -T did not replace the partial cache (MA-RB-R2-3)");
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(cacheB, "packages"), "packages.staged.*")); // never nested
        Assert.Empty(Directory.EnumerateDirectories(cacheB, "packages.*.old")); // rotation dir cleaned
    }

    // ------------------------------------------------------------------ MA-RB-R2-2

    // Tests AP-017/MA-RB-R2-2 [integration]: start-of-run reaping of leaked cache
    // staging removes staged/.old dirs whose pid-suffix names a DEAD process
    // (int.MaxValue can never be a live Linux pid) plus the legacy bare
    // packages.old, while PRESERVING a LIVE seeder's staged dir (this test host's
    // own pid). On reversion (no reaper) the dead staged dirs accumulate
    // unbounded in the never-pruned out/cache.
    [Fact]
    public void MaRbR22_ReapCacheStaging_ReapsDead_PreservesLive()
    {
        var cache = Path.Combine(SpikePaths.TestScratch("ma2-reap"), "cache");
        Directory.CreateDirectory(cache);
        var live = Path.Combine(cache, $"packages.staged.{Environment.ProcessId}");
        var deadStaged = Path.Combine(cache, $"packages.staged.{int.MaxValue}");
        var deadOld = Path.Combine(cache, $"packages.{int.MaxValue - 1}.old");
        var legacyOld = Path.Combine(cache, "packages.old");
        foreach (var d in new[] { live, deadStaged, deadOld, legacyOld })
        {
            Directory.CreateDirectory(d);
        }

        var result = Phase("--phase", "reap-cache-staging", "--out-dir", cache);
        Assert.Equal(0, result.ExitCode);
        Assert.True(Directory.Exists(live), "reaped a LIVE seeder's in-flight staged dir (MA-RB-R2-2)");
        Assert.False(Directory.Exists(deadStaged), "did not reap a dead-pid staged dir");
        Assert.False(Directory.Exists(deadOld), "did not reap a dead-pid rotation (.old) dir");
        Assert.False(Directory.Exists(legacyOld), "did not reap the legacy packages.old dir");
    }

    // ------------------------------------------------------------------ MA-UX-R2-1

    // Tests INV-014/DD-008/MA-UX-R2-1 [integration]: clean-runs.sh reclaims
    // out/test-scratch (the LARGEST consumer) — all but the newest token dir —
    // plus loose out/preprocess.*.xml / out/*.log, while preserving out/cache and
    // the newest token. Isolated --out-dir. On reversion (test-scratch not
    // pruned) the planted stale token survives -> test fails.
    [Fact]
    public void MaUxR21_CleanRuns_ReclaimsTestScratch_AndLooseFiles_PreservesCacheAndNewest()
    {
        var outDir = Path.Combine(SpikePaths.TestScratch("ma2-cleanruns-scratch"), "out");
        var scratchRoot = Path.Combine(outDir, "test-scratch");
        Directory.CreateDirectory(scratchRoot);
        var newest = Path.Combine(scratchRoot, "run-newest");
        var stale1 = Path.Combine(scratchRoot, "run-stale1");
        var stale2 = Path.Combine(scratchRoot, "run-stale2");
        Directory.CreateDirectory(stale2);
        File.WriteAllText(Path.Combine(stale2, "big"), "x");
        File.SetLastWriteTimeUtc(stale2, DateTime.UtcNow.AddMinutes(-30));
        Directory.CreateDirectory(stale1);
        File.SetLastWriteTimeUtc(stale1, DateTime.UtcNow.AddMinutes(-20));
        Directory.CreateDirectory(newest);
        File.SetLastWriteTimeUtc(newest, DateTime.UtcNow.AddMinutes(-1)); // newest survives
        var looseXml = Path.Combine(outDir, "preprocess.abcdef.xml");
        var looseLog = Path.Combine(outDir, "run.log");
        File.WriteAllText(looseXml, "<dump/>");
        File.WriteAllText(looseLog, "log");
        var cacheDir = Path.Combine(outDir, "cache");
        Directory.CreateDirectory(cacheDir);
        var cacheSentinel = Path.Combine(cacheDir, "keep-me");
        File.WriteAllText(cacheSentinel, "keep");

        var result = Launch.Script("scripts/clean-runs.sh", null, "--out-dir", outDir, "--keep", "3");
        Assert.True(result.ExitCode == 0, $"clean-runs failed: {Tail(result.StdErr)}");

        Assert.True(Directory.Exists(newest), "clean-runs deleted the newest test-scratch token (must keep newest)");
        Assert.False(Directory.Exists(stale1), "clean-runs did not reclaim a stale test-scratch token (MA-UX-R2-1)");
        Assert.False(Directory.Exists(stale2), "clean-runs did not reclaim a stale test-scratch token (MA-UX-R2-1)");
        Assert.False(File.Exists(looseXml), "clean-runs did not reclaim a loose preprocess dump");
        Assert.False(File.Exists(looseLog), "clean-runs did not reclaim a loose .log");
        Assert.True(File.Exists(cacheSentinel), "clean-runs deleted out/cache (forbidden)");
    }

    private static string Tail(string s)
    {
        var lines = s.Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToArray();
        return string.Join(" | ", lines.TakeLast(6));
    }
}

[Collection(SharedStateMutatingCollection.Name)]
public class MiniAudit2AggregatorTests
{
    private static string SchemaPath => SpikePaths.P("schema", "evidence-schema.json");
    private static string RegistryPath => SpikePaths.P("schema", "schema-version-registry.json");

    // ------------------------------------------------------------------ MA-VI-R2-1

    // Tests INV-006/AP-006/MA-VI-R2-1 [integration, launch-through-binary]: a
    // schema-clean route report carrying an unknown/SUPERSET composite key
    // (P99(B), which the schema does not constrain) must be malformed on ALL
    // THREE surfaces — the verdict (malformed-report), the per_probe_results view
    // (that route's entries incomplete), AND the exit/report matrix
    // (inconsistent) — not just the verdict. On reversion (ingestion accepts the
    // superset key) the verdict stays malformed but the per-probe view shows
    // all-pass and the matrix shows consistent, and this test fails.
    [Fact]
    public void MaViR21_UnknownSupersetKey_MalformedOnAllThreeSurfaces()
    {
        var scratch = SpikePaths.TestScratch("ma2-superset-key");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        var aRep = Path.Combine(scratch, "route-a.json");
        var bRep = Path.Combine(scratch, "route-b.json");
        File.WriteAllText(aRep, RouteReportJson("A", runId));
        // Route B is a schema-valid ALL-PASS report PLUS an unknown superset key.
        File.WriteAllText(bRep, RouteReportJson("B", runId, extraProbe: "P99"));

        var (result, runReport) = RunAggregator(scratch, runId, aRep, 0, bRep, 0);

        // Fail-closed exit (QA-016 clause: not COMPATIBLE, not suite-failure).
        Assert.NotEqual(ExitCodes.RouteProbesPassed, result.ExitCode);
        using var doc = Launch.Report(runReport);
        var det = doc.RootElement.GetProperty("deterministic");

        // Surface 1: the verdict.
        var bVerdict = det.GetProperty("route_verdicts").EnumerateArray().Single(v => v.GetProperty("route").GetString() == "B");
        var bReason = bVerdict.GetProperty("verdict_reason");
        Assert.Equal("malformed-report", bReason.GetProperty("variant").GetString());
        Assert.Contains("manifest instantiation", bReason.GetProperty("detail").GetString()); // MY ingestion path, not a schema reject

        // Surface 2: the per_probe_results view — the malformed route child
        // contributes NOTHING, so every child-owned route-B probe (P03, P05–P12)
        // reads incomplete. (P01(B) stays pass: it is controller-attested, not
        // from the rejected child report; P99 is not a manifest entry so it never
        // appears — pre-fix it and P03–P12 would all read pass.)
        var childProbeIds = new[] { "P03", "P05", "P06", "P07", "P08", "P09", "P10", "P11", "P12" };
        var bChild = det.GetProperty("per_probe_results").EnumerateArray()
            .Where(p => p.GetProperty("route").GetString() == "B"
                        && childProbeIds.Contains(p.GetProperty("probe").GetString()))
            .ToList();
        Assert.Equal(childProbeIds.Length, bChild.Count);
        Assert.All(bChild, p => Assert.Equal("incomplete", p.GetProperty("status").GetString()));

        // Surface 3: the exit/report matrix.
        Assert.Equal("inconsistent", det.GetProperty("exit_report_matrix_outcome").GetString());
    }

    // ------------------------------------------------------------------ MA-ID-R2-2

    // Tests INV-006/MA-ID-R2-2 [integration] (PRODUCE side + shell<->C# binding),
    // provable green from a SINGLE clean-checkout run (`rm -rf out`) — AP-021.
    // The suite receipt is the controller's product of THIS run, written only
    // AFTER the suite phase, so an in-suite test can never read the current run's
    // own suite receipt — and it MUST NOT enumerate PRIOR run roots for one. The
    // former version did: it required at least one prior run's receipt to carry
    // suite_exit==0, i.e. a prior successful suite to prove this suite can succeed
    // — a non-bootstrappable circular gate that passed only on leaked accumulated
    // `out/` state (AP-010) and deadlocked from clean (PMB-002). Two from-clean,
    // current-run-bound checks replace it:
    //   (1) shell<->C# binding: run-spike.sh writes the suite receipt at the path
    //       RunLayout.SuiteReceiptRelativePath names, and binds its
    //       source_manifest_sha256 to $SRC_DIGEST — the SAME variable the
    //       run-context writes as source_set_sha256 — so source_manifest ==
    //       source_set holds by construction, from clean, not via a prior green
    //       receipt. (The produced receipt's run_id/nonce binding to the current
    //       run is additionally enforced at RUNTIME, fail-closed, by the aggregator
    //       on every canonical run — SpikeAggregator MA-VI-6.)
    //   (2) the produce-side binding pattern on a REAL artifact of THIS run,
    //       reached via RunContext (never a prior run root on disk): the build
    //       receipt (written before the suite) binds to the current run-context —
    //       run_id == RunContext.RunId(), nonce present.
    [Fact]
    public void MaIdR22_SuiteReceipt_ProduceSideBinding_AndPathMatchesConstant()
    {
        // (1) shell<->C# path + field binding (mirrors QA-013/Ea008; no prior run).
        var script = File.ReadAllText(SpikePaths.P("scripts", "run-spike.sh"));
        Assert.Contains("SUITE_RECEIPT=\"$RUN_ROOT/receipts/suite-receipt.json\"", script);
        Assert.Equal("receipts/suite-receipt.json", RunLayout.SuiteReceiptRelativePath);
        // source_manifest_sha256 (suite receipt) and source_set_sha256
        // (run-context) are both written from $SRC_DIGEST => equal by construction.
        Assert.Contains("\"source_manifest_sha256\": \"%s\"", script);
        Assert.Contains("${SRC_DIGEST:-unknown}", script);
        Assert.Contains("\"source_set_sha256\": \"%s\",\\n' \"$SRC_DIGEST\"", script);

        // (2) produce-side binding on a REAL current-run artifact, via RunContext
        // (AP-021). The build receipt is written before the suite phase, so it is
        // present while this test runs, and binds to this run's context.
        var runRoot = RunContext.RunRoot();
        using var build = Launch.Receipt(runRoot, RunLayout.BuildReceiptRelativePath);
        Assert.Equal(RunContext.RunId(), build.RootElement.GetProperty("run_id").GetString());
        Assert.False(string.IsNullOrEmpty(build.RootElement.GetProperty("nonce").GetString()),
            "current run's build receipt has no nonce — the produce side did not bind it to the run (MA-ID-R2-2).");
    }

    // ------------------------------------------------------------------ MA-ID-R2-4

    // Tests EA-008/MA-ID-R2-4 [integration] (received-env, not scan): the two
    // network launch classes (provision + fetch) actually RECEIVED a
    // decoy-first PATH and a run-root TMPDIR, read from the substrate run's
    // env-audit RECEIPT (the exact dictionary passed at each launch), not a
    // source grep. On reversion (either class stops going through the constructed
    // per-launch environment) its entry is missing or its PATH/TMPDIR wrong.
    [Fact]
    public void MaIdR24_ProvisionAndFetch_ReceivedDecoyFirstPath_RunRootTmpdir()
    {
        var runRoot = RunContext.RunRoot();
        using var audit = Launch.Receipt(runRoot, RunLayout.EnvAuditReceiptRelativePath);
        var launches = audit.RootElement.GetProperty("launches").EnumerateArray().ToList();
        foreach (var cls in new[] { "provision", "fetch" })
        {
            var entry = launches.FirstOrDefault(l => l.GetProperty("class").GetString() == cls);
            Assert.True(entry.ValueKind == JsonValueKind.Object,
                $"no '{cls}' launch recorded in env-audit.json — the {cls} phase did not run under the constructed per-launch environment (MA-ID-R2-4)");
            var env = entry.GetProperty("env");
            var path = env.GetProperty("PATH").GetString()!;
            var tmpdir = env.GetProperty("TMPDIR").GetString()!;
            Assert.StartsWith(Path.Combine(runRoot, "decoys"), path); // decoy-first PATH (INV-003)
            Assert.Equal(Path.Combine(runRoot, "tmp"), tmpdir);       // run-root TMPDIR
        }
    }

    // ---------------------------------------------------------------- helpers

    private static string NewRunId() => $"runid-ma2-{Guid.NewGuid():N}"[..26];

    private static void ProvisionInto(string runRoot)
    {
        var provision = Launch.Script("scripts/provision-z3.sh", null, "--run-root", runRoot);
        Assert.True(provision.ExitCode == 0, $"provisioning failed: {provision.StdErr}");
    }

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
            "--run-id", runId, "--nonce", "nonce-ma2", "--run-root", scratch,
            "--restore-receipt", Path.Combine(scratch, "restore-receipt.json"),
            "--build-receipt", Path.Combine(scratch, "build-receipt.json"),
            "--suite-status", "unknown",
            "--report", $"A={aRep}:{aExit}", "--report", $"B={bRep}:{bExit}",
            "--out", runReport, "--aggregation-receipt", Path.Combine(scratch, "agg.json"));
        Assert.True(File.Exists(runReport), $"aggregator produced no run report: {result.StdOut} {result.StdErr}");
        return (result, runReport);
    }

    /// <summary>Full nine-probe route-child report (P03, P05–P12), optionally with an extra (superset) probe key.</summary>
    private static string RouteReportJson(string route, string runId, string? extraProbe = null)
    {
        var probes = new List<string> { "P03", "P05", "P06", "P07", "P08", "P09", "P10", "P11", "P12" };
        if (extraProbe is not null)
        {
            probes.Add(extraProbe);
        }
        var entries = string.Join(",\n", probes.Select(p =>
            $"      {{ \"probe\": \"{p}\", \"route\": \"{route}\", \"status\": \"pass\", \"detail\": null }}"));
        return "{\n" +
               "  \"evidence_schema_version\": 2,\n" +
               $"  \"evidence_schema_sha256\": \"{SpecConstants.EvidenceSchemaSha256}\",\n" +
               $"  \"probe_manifest_sha256\": \"{SpecConstants.ProbeManifestSha256}\",\n" +
               $"  \"run_id\": \"{runId}\",\n" +
               "  \"kind\": \"route-report\",\n" +
               "  \"binding_identity\": {\n" +
               "    \"run_directory\": \"out/ma2\", \"git_commit_id\": \"abc\", \"git_dirty_flag\": false,\n" +
               "    \"host_rid\": \"linux-x64\", \"sdk_version\": \"10.0.302\", \"actual_runtime_version\": \"10.0.2\"\n" +
               "  },\n" +
               "  \"deterministic\": {\n" +
               "    \"per_probe_results\": [\n" + entries + "\n    ],\n" +
               "    \"final_suite_status\": \"unknown\"\n" +
               "  },\n" +
               "  \"volatile\": {}\n" +
               "}\n";
    }

    private static void WriteReceipts(string dir, string runId)
    {
        File.WriteAllText(Path.Combine(dir, "restore-receipt.json"),
            $"{{ \"run_id\": \"{runId}\", \"nonce\": \"nonce-ma2\", \"exit\": 0, \"argv\": [\"dotnet\",\"restore\"],\n" +
            "  \"p01_partitions\": { \"A\": { \"projects\": [], \"exit\": 0 }, \"B\": { \"projects\": [], \"exit\": 0 } },\n" +
            "  \"lock_sha256\": {} }\n");
        File.WriteAllText(Path.Combine(dir, "build-receipt.json"),
            $"{{ \"run_id\": \"{runId}\", \"nonce\": \"nonce-ma2\", \"exit\": 0, \"sdk_version\": \"{SpecConstants.SdkPin}\", \"artifacts\": [] }}\n");
    }
}
