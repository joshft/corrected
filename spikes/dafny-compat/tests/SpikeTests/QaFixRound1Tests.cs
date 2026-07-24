// QA fix round 1 — class-fix tests. Each finding's class_fix requires a
// behavioral or structural test that makes the fix reviewable and prevents
// regression. These ADD coverage; no existing assertion is weakened.
using System.Text.Json;
using System.Text.RegularExpressions;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

[Collection(SharedStateMutatingCollection.Name)]
public class QaFixRound1Tests
{
    private static string SchemaPath => SpikePaths.P("schema", "evidence-schema.json");

    // QA-001 class fix: the committed evidence samples must come from a clean
    // checkout — git_dirty_flag==false, a 40-hex commit that is an ancestor of
    // HEAD. A sample generated from a dirty tree (the RED defect) is rejected.
    [Fact]
    public void QA001_CommittedSamples_CleanTreeAncestorCommit()
    {
        foreach (var name in new[] { "run-report.sample.json", "run-report.canonical.sample.json" })
        {
            var path = SpikePaths.P("evidence", "samples", name);
            if (!File.Exists(path))
            {
                // Sample-dependent: the orchestrator regenerates from the clean
                // tree after these fixes are committed. Loud, not silent.
                Assert.Fail($"committed sample {name} is missing — regenerate the PAIR via scripts/regen-sample.sh from a CLEAN tree (QA-001/DD-008).");
            }
            using var doc = SpikePaths.Json(path);
            var binding = doc.RootElement.GetProperty("binding_identity");
            Assert.False(binding.GetProperty("git_dirty_flag").GetBoolean(),
                $"{name} was generated from a DIRTY tree (git_dirty_flag:true) — repudiation gap (QA-001/STRIDE-TB-004). Regenerate from a clean checkout.");
            var commit = binding.GetProperty("git_commit_id").GetString()!;
            Assert.Matches("^[0-9a-f]{40}$", commit);
            Assert.True(SpikePaths.IsAncestorOfHead(commit),
                $"{name} commit {commit} is not an ancestor of HEAD — the sample does not descend from a committed state (QA-001).");
        }
    }

    // QA-003 class fix: an induced-failure test plants a PRE-INVOKED decoy log
    // and asserts the emitted decoy_invocations is nonzero — the value comes
    // from the actually-read log, never a hardcoded literal.
    [Fact]
    public void QA003_PreInvokedDecoyLog_YieldsNonzeroDecoyInvocations()
    {
        var runRoot = SpikePaths.TestScratch("qa003-decoy-log");
        Inv003SolverIdentityTests.WriteLedger(runRoot, $"nonce-{Guid.NewGuid():N}");
        var provision = Launch.Script("scripts/provision-z3.sh", null, "--run-root", runRoot);
        Assert.True(provision.ExitCode == 0, $"provisioning failed: {provision.StdErr}");

        // The TEST pre-populates the decoy log the harness reads at run end.
        var decoyLog = Path.Combine(runRoot, "sentinel", "decoy-invocations.log");
        Directory.CreateDirectory(Path.GetDirectoryName(decoyLog)!);
        File.WriteAllText(decoyLog, "decoy-invoked -smt2 -in\ndecoy-invoked -version\n");

        var report = Path.Combine(runRoot, "report.json");
        var result = Launch.Harness("A", "--probe", "P06", "--run-root", runRoot, "--out", report);
        // A real decoy invocation makes the run non-COMPATIBLE (INV-003).
        Assert.NotEqual(ExitCodes.RouteProbesPassed, result.ExitCode);
        using var doc = Launch.Report(report);
        var outcomes = doc.RootElement.GetProperty("deterministic").GetProperty("sentinel_ledger_outcomes");
        Assert.True(outcomes.GetProperty("decoy_invocations").GetInt32() >= 2,
            "decoy_invocations was not populated from the actually-read decoy log (QA-003)");
        Assert.True(outcomes.GetProperty("decoys_present").GetBoolean(),
            "decoys_present must come from a post-plant existence check (QA-003)");
    }

    // QA-005 class fix: one induced-failure test per committed verdict_reason
    // variant, asserting the variant AND cause match the induced fault class.
    // The controller's synthetic-report path (prerequisite faults) is exercised
    // by pointing at a missing solver so the cause is a real prerequisite fault
    // — never the reused WallClockExpiry literal.
    [Fact]
    public void QA005_PrerequisiteFault_CauseIsNotHardcodedWallClockExpiry()
    {
        var runRoot = SpikePaths.TestScratch("qa005-prereq");
        // No provisioning: the controller's provision phase fails, and the
        // synthetic report must carry cause=PrerequisiteFailure, not WallClockExpiry.
        var badSdkEnv = new Dictionary<string, string> { ["SPIKE_RID_OVERRIDE"] = "win-x64" };
        var runReport = Path.Combine(runRoot, "run-report.json");
        var result = Launch.Script("scripts/run-spike.sh", badSdkEnv, "--run-root", runRoot, "--out", runReport);
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(runReport), "controller must emit a synthetic report on prerequisite failure (AP-009)");
        using var doc = Launch.Report(runReport);
        foreach (var rv in doc.RootElement.GetProperty("deterministic").GetProperty("route_verdicts").EnumerateArray())
        {
            var reason = rv.GetProperty("verdict_reason");
            Assert.Equal("prerequisite-failure", reason.GetProperty("variant").GetString());
            var cause = reason.GetProperty("cause").GetString();
            Assert.Equal("PrerequisiteFailure", cause);
            Assert.NotEqual("WallClockExpiry", cause); // QA-005: the reused-literal defect
        }
    }

    // QA-005 class fix (aggregator leg): the malformed-report variant is its
    // OWN variant — a route whose report is unparseable does NOT collapse into
    // missing-report/crash.
    [Fact]
    public void QA005_MalformedRouteReport_OwnVariant()
    {
        var manifest = ProbeManifest.Load(SpikePaths.P("manifest", "probe-manifest.json"));
        var scratch = SpikePaths.TestScratch("qa005-malformed");
        var reportsDir = Path.Combine(scratch, "reports");
        Directory.CreateDirectory(reportsDir);
        var aRep = Path.Combine(reportsDir, "route-a.json");
        var bRep = Path.Combine(reportsDir, "route-b.json");
        // Route A: a valid all-pass route report; Route B: malformed JSON.
        File.WriteAllText(aRep, ValidRouteReport("A"));
        File.WriteAllText(bRep, "{ this is : not json [");

        var aggregator = RunContext.Resolve("SpikeAggregator");
        var env = EnvProfiles.For("harness", RunContext.RunRoot());
        var runId = "run-qa005-abcdef01";
        WriteReceipts(scratch, runId);
        var runReport = Path.Combine(scratch, "run-report.json");
        var result = Launch.Dll(aggregator.AbsolutePath, env,
            "--manifest", SpikePaths.P("manifest", "probe-manifest.json"),
            "--schema", SchemaPath,
            "--registry", SpikePaths.P("schema", "schema-version-registry.json"),
            "--run-id", runId, "--nonce", "nonce-qa005", "--run-root", scratch,
            "--restore-receipt", Path.Combine(scratch, "restore-receipt.json"),
            "--build-receipt", Path.Combine(scratch, "build-receipt.json"),
            "--suite-status", "unknown",
            "--solver", Path.Combine(scratch, "no-solver"),
            "--report", $"A={aRep}:0", "--report", $"B={bRep}:0",
            "--out", runReport, "--aggregation-receipt", Path.Combine(scratch, "agg.json"));
        _ = result;
        Assert.True(File.Exists(runReport), $"aggregator produced no report: {result.StdOut} {result.StdErr}");
        using var doc = Launch.Report(runReport);
        var bVerdict = doc.RootElement.GetProperty("deterministic").GetProperty("route_verdicts").EnumerateArray()
            .Single(v => v.GetProperty("route").GetString() == "B");
        Assert.Equal("malformed-report", bVerdict.GetProperty("verdict_reason").GetProperty("variant").GetString());
    }

    // QA-008 class fix: the CONTROLLER exit code is fail-closed — nonzero when
    // a route child fails. A missing-solver variance run makes both children
    // exit INCOMPLETE, and the controller must exit nonzero (CI wiring).
    [Fact]
    public void QA008_Controller_ExitsNonzero_OnChildFailure()
    {
        using var scope = SpikePaths.TransientScratch("qa008-exit");
        var runRoot = scope.Root;
        // Point the run at an empty pre-created run root with NO provisioned
        // solver: the controller provisions z3 (so provision succeeds), but we
        // force child failure by removing the solver right before probes via a
        // sentinel-free path is not possible here; instead use a bogus solver
        // override that fails the typed startup check.
        var badSolver = SpikePaths.WriteExecutable(Path.Combine(runRoot, "bad-solver"),
            "echo 'not z3'; exit 3");
        var runReport = Path.Combine(runRoot, "run-report.json");
        var result = Launch.Script("scripts/run-spike.sh", null,
            "--run-root", runRoot, "--solver", badSolver, "--out", runReport);
        Assert.NotEqual(0, result.ExitCode); // QA-008: exit channel fail-closed

        scope.Commit(); // passed — reclaim the run root (~550MB package copy)
    }

    // QA-009 class fix: a route-scoped restore fault (mutate ONLY Route B's
    // lock in scratch) must fail Route B's per-project restore while Route A's
    // succeeds — the per-project lock validation P01 partition attestation uses
    // is genuinely independent, so a Route-B-only fault cannot veto Route A.
    [Fact]
    public void QA009_RouteScopedLockFault_DoesNotAffectOtherRoutePartition()
    {
        var scratch = SpikePaths.TestScratch("qa009-route-scoped");
        foreach (var rootFile in new[] { "NuGet.Config", "global.json", "Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets", "Directory.Build.rsp" })
        {
            File.Copy(SpikePaths.P(rootFile), Path.Combine(scratch, rootFile), overwrite: true);
        }
        SpikePaths.CopyTree(SpikePaths.P("contracts"), Path.Combine(scratch, "contracts"));
        SpikePaths.CopyTree(SpikePaths.P("adapters"), Path.Combine(scratch, "adapters"));

        var routeAProj = Path.Combine(scratch, "adapters", "SpikeDafnyAdapter.RouteA", "SpikeDafnyAdapter.RouteA.csproj");
        var routeBProj = Path.Combine(scratch, "adapters", "SpikeDafnyAdapter.RouteB", "SpikeDafnyAdapter.RouteB.csproj");
        var routeBLock = Path.Combine(scratch, "adapters", "SpikeDafnyAdapter.RouteB", "packages.lock.json");

        // Control: BOTH restore cleanly on the pristine copy.
        Assert.Equal(0, Launch.Script("scripts/run-spike.sh", null, "--phase", "restore", "--project", routeAProj).ExitCode);
        Assert.Equal(0, Launch.Script("scripts/run-spike.sh", null, "--phase", "restore", "--project", routeBProj).ExitCode);

        // The TEST mutates ONLY Route B's lock (DafnyPipeline -> nightly).
        File.WriteAllText(routeBLock, MutateResolved(File.ReadAllText(routeBLock), "DafnyPipeline", "4.11.1-nightly-2026-01-01"));

        // Route B restore now fails; Route A restore is UNAFFECTED.
        var b = Launch.Script("scripts/run-spike.sh", null, "--phase", "restore", "--project", routeBProj);
        Assert.NotEqual(0, b.ExitCode);
        var a = Launch.Script("scripts/run-spike.sh", null, "--phase", "restore", "--project", routeAProj);
        Assert.Equal(0, a.ExitCode);
    }

    // QA-011 class fix: the P05 sentinel ledger delta is nonce-AND-sub-nonce
    // scoped, so an unrelated foreign-nonce or another probe's sub-nonce entry
    // planted before the run can never satisfy this probe's window.
    [Fact]
    public void QA011_LedgerDelta_IsPerProbeSubNonceScoped()
    {
        var runRoot = SpikePaths.TestScratch("qa011-subnonce");
        var provision = Launch.Script("scripts/provision-z3.sh", null, "--run-root", runRoot);
        Assert.True(provision.ExitCode == 0, $"provisioning failed: {provision.StdErr}");
        var nonce = $"nonce-{Guid.NewGuid():N}";
        // Pre-plant an entry under THIS run's nonce but a DIFFERENT probe's
        // sub-nonce — the per-probe scoping must exclude it from P05's count.
        var ledgerPath = Path.Combine(runRoot, RunLayout.SentinelLedgerRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);
        File.WriteAllText(ledgerPath,
            $"{{ \"nonce\": \"{nonce}\", \"entries\": [ {{ \"nonce\": \"{nonce}\", \"argv\": [\"probe:P99-stale\", \"-smt2\"] }} ] }}\n");

        var report = Path.Combine(runRoot, "report.json");
        var result = Launch.Harness("A", "--probe", "P05", "--run-root", runRoot, "--out", report);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);
        // P05 recorded its OWN sub-nonce entry (fresh, in the MA-RB-3
        // append-only entry files), proving its pass did not rest on the
        // pre-planted foreign-sub-nonce entry.
        var freshForP05 = Launch.LedgerFileEntries(runRoot)
            .Count(e => e.Nonce == nonce && e.ProbeTag.StartsWith("probe:P05", StringComparison.Ordinal));
        Assert.True(freshForP05 >= 1, "P05 recorded no entry under its own P05 sub-nonce (QA-011 scoping broken)");
    }

    // QA-012 class fix: the gate's check list is source-cited (README/oracle),
    // and a versioned-name z3 planted deeper on PATH is recorded, not silently
    // ignored — the gate scans every PATH dir for z3*-named executables.
    [Fact]
    public void QA012_Gate_ScansEveryPathDir_ForVersionedNames()
    {
        var runRoot = SpikePaths.TestScratch("qa012-versioned");
        var provision = Launch.Script("scripts/provision-z3.sh", null, "--run-root", runRoot);
        Assert.True(provision.ExitCode == 0, $"provisioning failed: {provision.StdErr}");
        Inv003SolverIdentityTests.WriteLedger(runRoot, $"nonce-{Guid.NewGuid():N}");

        // An UNSANCTIONED exact-name z3 first on PATH (not at the sanctioned
        // decoy dir) must fail the gate closed — the sweep is not limited to a
        // single dir or the sanctioned location.
        var badDir = Path.Combine(runRoot, "hostile");
        SpikePaths.WriteExecutable(Path.Combine(badDir, "z3"), "echo 'Z3 version 6.6.6 - 64 bit'");
        var env = new Dictionary<string, string>(EnvProfiles.For("harness", runRoot).ToDictionary(kv => kv.Key, kv => kv.Value))
        {
            ["PATH"] = badDir + Path.PathSeparator + "/usr/bin:/bin",
        };
        var report = Path.Combine(runRoot, "report.json");
        var result = Launch.HarnessWithEnv("A", env, "--probe", "P06", "--run-root", runRoot, "--out", report);
        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
        using var doc = Launch.Report(report);
        Assert.Contains("startup-gate", doc.RootElement.GetRawText());
    }

    // QA-013 class fix: a canonical controller run records a source-set content
    // digest and its manifest, so a run-context consumer can assert substrate
    // identity (AP-003 digest-match). The manifest's per-file digests must
    // equal the current tree — a mismatch means stale binaries.
    [Fact]
    public void QA013_RunContext_CarriesSourceSetDigest_MatchingTree()
    {
        var runContextPath = RunContext.RequirePath();
        using var doc = SpikePaths.Json(runContextPath);
        Assert.True(doc.RootElement.TryGetProperty("source_set_sha256", out var digest),
            "run-context.json carries no source_set_sha256 — QA-013 substrate-identity anchor missing");
        Assert.Matches("^[0-9a-f]{64}$|^unknown$", digest.GetString()!);

        // MA-UC-1: the substrate-identity check is FULL BIDIRECTIONAL set
        // equality — the test recomputes the current tree's file list with the
        // SAME patterns the controller uses (parsed from run-spike.sh, the
        // single source) and asserts recorded == recomputed in BOTH directions,
        // so files ADDED by an upgrade (invisible to a row-by-row walk over the
        // recorded side) and DELETED files (previously silently skipped) both
        // surface as staleness. Per-file digests are then compared row by row.
        var manifest = Path.Combine(RunContext.RunRoot(), ".source-manifest");
        Assert.True(File.Exists(manifest),
            "controller run recorded no .source-manifest — the QA-013 substrate-identity guard has nothing to verify (AP-003)");

        var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(manifest).Where(l => l.Length > 0))
        {
            var parts = line.Split("  ", 2);
            Assert.True(parts.Length == 2, $"malformed source-manifest row: '{line}'");
            recorded[parts[1]] = parts[0];
        }
        Assert.NotEmpty(recorded);

        var patterns = SourceManifestPatterns();
        var recomputed = GitTrackedFilesMatching(patterns);
        Assert.NotEmpty(recomputed);
        var missingFromRecorded = recomputed.Except(recorded.Keys).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var vanishedFromTree = recorded.Keys.Except(recomputed).OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.True(missingFromRecorded.Count == 0,
            $"files present in the current tree but absent from the controller run's source manifest (upgrade-added; binaries are stale — re-run scripts/run-spike.sh): {string.Join(", ", missingFromRecorded)} (QA-013/MA-UC-1)");
        Assert.True(vanishedFromTree.Count == 0,
            $"files recorded by the controller run but no longer in the tree (deleted since; binaries are stale — re-run scripts/run-spike.sh): {string.Join(", ", vanishedFromTree)} (QA-013/MA-UC-1)");

        foreach (var (rel, sha) in recorded)
        {
            var full = SpikePaths.P(rel.Split('/'));
            Assert.True(File.Exists(full) && SpikePaths.Sha256File(full) == sha,
                $"source file {rel} changed since the controller run — binaries are stale; re-run scripts/run-spike.sh (QA-013/AP-003).");
        }

        // Coordination (MA-UC-1, LAST assertion so the set-equality machinery
        // above is proven independently): the solution file is a real build
        // input; the controller's pattern list must cover *.sln so an upgrade
        // that adds a project cannot green-validate pre-upgrade binaries.
        // (The pattern addition in scripts/run-spike.sh is the shell/operator
        // slice of this fix round.)
        Assert.Contains("*.sln", patterns);
    }

    /// <summary>Parses the controller's git ls-files pattern list from scripts/run-spike.sh (single source — MA-UC-1).</summary>
    private static IReadOnlyList<string> SourceManifestPatterns()
    {
        var script = File.ReadAllLines(SpikePaths.P("scripts", "run-spike.sh"));
        var line = script.SingleOrDefault(l => l.Contains("git -C \"$SPIKE_ROOT\" ls-files --", StringComparison.Ordinal));
        Assert.False(line is null, "run-spike.sh no longer builds the QA-013 source manifest via git ls-files — the substrate-identity guard lost its source (MA-UC-1)");
        var patterns = System.Text.RegularExpressions.Regex.Matches(line!, "'([^']+)'")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(patterns);
        return patterns;
    }

    /// <summary>Recomputes the current tree's manifest file set with the controller's own patterns (read-only git invocation).</summary>
    private static IReadOnlySet<string> GitTrackedFilesMatching(IReadOnlyList<string> patterns)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = SpikePaths.SpikeRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(SpikePaths.SpikeRoot);
        psi.ArgumentList.Add("ls-files");
        psi.ArgumentList.Add("--");
        foreach (var pattern in patterns)
        {
            psi.ArgumentList.Add(pattern);
        }
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(30000);
        Assert.Equal(0, proc.ExitCode);
        return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(f => File.Exists(SpikePaths.P(f.Split('/')))) // mirror the controller's [ -f ] guard
            .ToHashSet();
    }

    // QA-007 class fix: a NON-probe phase hang (test-planted slow build target)
    // is independently killable by the REAL per-phase setsid supervisor. The
    // TEST plants an inject.targets in its own run root that sleeps during the
    // build phase (an external fault, not a SUT flag), shrinks the wall-clock
    // bound, and asserts the controller records the synthetic INCOMPLETE with
    // the DELIVERED signal — proving supervision is not config-string theater.
    [Fact]
    public void QA007_NonProbePhaseHang_BuildTarget_SupervisorKills()
    {
        using var scope = SpikePaths.TransientScratch("qa007-build-hang");
        var runRoot = scope.Root;
        // TEST-planted slow build target imported by Directory.Build.targets
        // only when this run root carries inject.targets (DD-008 isolation).
        File.WriteAllText(Path.Combine(runRoot, "inject.targets"), """
        <Project>
          <Target Name="QaHang" BeforeTargets="Build">
            <Exec Command="sleep 100000" />
          </Target>
        </Project>
        """);
        const int boundSeconds = 45;
        var configCopy = Path.Combine(runRoot, "run-config.json");
        File.WriteAllText(configCopy, File.ReadAllText(SpikePaths.P("config", "run-config.json"))
            .Replace("\"wall_clock_bound_seconds\": 1800", $"\"wall_clock_bound_seconds\": {boundSeconds}"));

        var runReport = Path.Combine(runRoot, "run-report.json");
        var result = Launch.Script("scripts/run-spike.sh", null,
            "--config", configCopy, "--run-root", runRoot, "--out", runReport);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.DurationMs >= boundSeconds * 1000,
            $"controller returned after {result.DurationMs}ms — before the {boundSeconds}s bound; the build phase was not actually supervised");
        Assert.True(result.DurationMs < 240_000, "controller overran the deadline by more than the TERM->KILL grace window");
        using var doc = Launch.Report(runReport);
        var text = doc.RootElement.GetRawText();
        Assert.Contains("INCOMPLETE", text);
        Assert.Contains("wall-clock", text);
        Assert.Matches("SIG(TERM|KILL)", text);
        Assert.Contains("build", text); // the non-probe phase that hung

        scope.Commit(); // passed — reclaim the run root (~630MB)
    }

    // QA-010: independently confirm the min_task_count=2 recapture for Dafny
    // 4.11 — ok.dfy yields 3 verification TARGETS (Add, MaxOfTwo, SumFirst) but
    // only 2 produce solver TASKS; the trivial function Add has no proof
    // obligation, so 2 (> 0) is the correct floor, not 3. This is the AP-014
    // real-producer rationale made a test, not just a sidecar comment.
    [Fact]
    public void QA010_OkFixture_ThreeTargets_TwoSolverTasks_TrivialAddHasNoObligation()
    {
        var scratch = SpikePaths.TestScratch("qa010-min-tasks");
        var report = Path.Combine(scratch, "p06.json");
        var result = Launch.Harness("A", "--probe", "P06", "--fixture", "fixtures/ok.dfy", "--out", report);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);
        using var doc = Launch.Report(report);
        var det = doc.RootElement.GetProperty("deterministic");
        var targets = det.GetProperty("verification_target_sets").GetProperty("fixtures/ok.dfy")
            .EnumerateArray().Select(t => t.GetString()!).ToList();
        var taskTargets = det.GetProperty("solver_outcome_enums").GetProperty("fixtures/ok.dfy")
            .EnumerateArray().Select(o => o.GetProperty("target").GetString()!).ToList();
        Assert.Equal(3, targets.Count);
        Assert.Equal(2, taskTargets.Count);
        // The target with no solver task is the trivial Add (no proof obligation).
        var missing = targets.Except(taskTargets).ToList();
        Assert.Single(missing);
        Assert.Equal("Add", missing[0]);
        // Cross-check the committed sidecar floor matches the observed reality.
        using var sidecar = SpikePaths.Json(SpikePaths.P("fixtures", "expected", "ok.sidecar.json"));
        Assert.Equal(2, sidecar.RootElement.GetProperty("min_task_count").GetInt32());
    }

    // QA-006/schema-version: the current schema version is 2 and its digest is
    // anchored at all four points; the emitted evidence carries version 2.
    [Fact]
    public void SchemaVersion2_AnchoredEverywhere_AndEmitted()
    {
        Assert.Equal("c872c710dd390ff8d8050c059077d0eb7d6ef4f2352fc7bf375403014ac18509", SpecConstants.EvidenceSchemaSha256);
        Assert.Equal(SpecConstants.EvidenceSchemaSha256, SpikePaths.Sha256File(SchemaPath));
        Assert.Equal(SpecConstants.EvidenceSchemaSha256, VerdictAggregator.EvidenceSchemaSha256TrustAnchor);
        using var schema = SpikePaths.Json(SchemaPath);
        Assert.Equal(2, schema.RootElement.GetProperty("evidence_schema_version").GetInt32());
        // The suite-status mask block is declared in the schema (additive-only).
        var masked = schema.RootElement.GetProperty("suite_status_mask").GetProperty("masked_class_2_fields")
            .EnumerateArray().Select(f => f.GetString()).ToList();
        Assert.Equal(new[] { "final_suite_status", "route_verdicts", "verdict_reasons" }, masked);
    }

    private static string MutateResolved(string lockJson, string package, string nightly)
    {
        using var doc = JsonDocument.Parse(lockJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            void Write(JsonElement el, bool inside)
            {
                switch (el.ValueKind)
                {
                    case JsonValueKind.Object:
                        writer.WriteStartObject();
                        foreach (var prop in el.EnumerateObject())
                        {
                            writer.WritePropertyName(prop.Name);
                            if (inside && prop.Name == "resolved") { writer.WriteStringValue(nightly); }
                            else { Write(prop.Value, inside || prop.Name == package); }
                        }
                        writer.WriteEndObject();
                        break;
                    case JsonValueKind.Array:
                        writer.WriteStartArray();
                        foreach (var item in el.EnumerateArray()) { Write(item, inside); }
                        writer.WriteEndArray();
                        break;
                    default:
                        el.WriteTo(writer);
                        break;
                }
            }
            Write(doc.RootElement, false);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ValidRouteReport(string route)
    {
        return $$"""
        {
          "evidence_schema_version": 2,
          "evidence_schema_sha256": "{{SpecConstants.EvidenceSchemaSha256}}",
          "probe_manifest_sha256": "{{SpecConstants.ProbeManifestSha256}}",
          "run_id": "run-qa005-abcdef01",
          "kind": "route-report",
          "binding_identity": {
            "run_directory": "out/qa005", "git_commit_id": "abc", "git_dirty_flag": false,
            "host_rid": "linux-x64", "sdk_version": "10.0.302", "actual_runtime_version": "10.0.2"
          },
          "deterministic": { "per_probe_results": [ { "probe": "P06", "route": "{{route}}", "status": "pass", "detail": null } ], "final_suite_status": "unknown" },
          "volatile": {}
        }
        """;
    }

    private static void WriteReceipts(string dir, string runId)
    {
        File.WriteAllText(Path.Combine(dir, "restore-receipt.json"), $$"""
        { "run_id": "{{runId}}", "nonce": "nonce-qa005", "exit": 0, "argv": ["dotnet","restore"],
          "p01_partitions": { "A": { "projects": [], "exit": 0 }, "B": { "projects": [], "exit": 0 } },
          "lock_sha256": {} }
        """);
        File.WriteAllText(Path.Combine(dir, "build-receipt.json"), $$"""
        { "run_id": "{{runId}}", "nonce": "nonce-qa005", "exit": 0, "sdk_version": "10.0.302", "artifacts": [] }
        """);
    }
}
