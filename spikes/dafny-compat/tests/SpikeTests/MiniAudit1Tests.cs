// Mini-audit round 1 (2026-07-22) — fix-locking tests. Each finding's fix
// carries a test that FAILS when the fix is reverted (QA-022 discipline):
//   gitfile merge group (PR-001 + MA-XC-1/MA-VI-5/MA-UC-3) — shared resolver
//   MA-RB-3/MA-HI-2 — append-only, byte-safe sentinel ledger
//   MA-VI-1 — duplicate-key rejection on the production aggregator path
//   MA-VI-2 — probe-status → verdict-reason-variant taxonomy
//   MA-VI-3 — unfiltered family sweep at the capture layer
//   MA-VI-4 — MapExit consistency is not a COMPATIBLE
//   MA-VI-6 — nonce-bound suite receipt gates final_suite_status
//   MA-VI-7 — P04 override path cannot yield an unqualified pass
//   MA-ED-2 — per-kind field coverage (declared ⇒ produced)
//   MA-ED-4 — no silent RID early-return in test sources
//   MA-ID-1 — P06 resource-usage observable gate
//   MA-UC-4 — sln-derived enforcement perimeter
//   PR-002/PR-003/PR-007 — runtime anchors for registry history, probe
//   manifest, and pin files
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

// ---------------------------------------------------------------- gitfile

public class MiniAudit1GitfileTests
{
    // Tests PR-001/MA-XC-1 [unit]: a fabricated linked-worktree-shaped tree —
    // the `.git` FILE with `gitdir:` resolves like a `.git` directory, and
    // HEAD resolution consults the worktree's OWN git dir + commondir refs,
    // never the outer repo's HEAD.
    [Fact]
    public void LinkedWorktree_GitFile_ResolvesWorktreeRootAndHead_NotOuterRepo()
    {
        var scratch = SpikePaths.TestScratch("ma1-gitfile");
        var outer = Path.Combine(scratch, "outer");
        var wt = Path.Combine(scratch, "wt");
        var outerSha = new string('a', 40);
        var wtSha = new string('b', 40);

        Directory.CreateDirectory(Path.Combine(outer, ".git", "refs", "heads"));
        File.WriteAllText(Path.Combine(outer, ".git", "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(outer, ".git", "refs", "heads", "main"), outerSha + "\n");
        File.WriteAllText(Path.Combine(outer, ".git", "refs", "heads", "feature"), wtSha + "\n");

        var privateGitDir = Path.Combine(outer, ".git", "worktrees", "wt");
        Directory.CreateDirectory(privateGitDir);
        File.WriteAllText(Path.Combine(privateGitDir, "HEAD"), "ref: refs/heads/feature\n");
        File.WriteAllText(Path.Combine(privateGitDir, "commondir"), "../..\n");

        Directory.CreateDirectory(Path.Combine(wt, "sub"));
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {privateGitDir}\n");

        // Every resolver surface returns the WORKTREE root and ITS head.
        Assert.Equal(wt, GitResolver.FindRepoRoot(Path.Combine(wt, "sub")));
        Assert.Equal(privateGitDir, GitResolver.ResolveGitDir(wt));
        Assert.Equal(wtSha, GitResolver.ReadHeadCommit(wt));
        Assert.NotEqual(GitResolver.ReadHeadCommit(outer), GitResolver.ReadHeadCommit(wt));
        Assert.Equal(outer, GitResolver.FindRepoRoot(outer));
        Assert.Equal(outerSha, GitResolver.ReadHeadCommit(outer));

        // commondir packed-refs leg: with the loose ref gone, the shared
        // packed-refs (in the COMMON dir) resolves the worktree branch.
        var packedSha = new string('c', 40);
        File.Delete(Path.Combine(outer, ".git", "refs", "heads", "feature"));
        File.WriteAllText(Path.Combine(outer, ".git", "packed-refs"),
            $"# pack-refs with: peeled fully-peeled sorted\n{packedSha} refs/heads/feature\n");
        Assert.Equal(packedSha, GitResolver.ReadHeadCommit(wt));

        // Fail-closed legs: no boundary above the filesystem root → null (the
        // scratch tree itself sits inside the real repo, so the no-boundary
        // case starts at the root); a .git file without gitdir: → typed refusal.
        Assert.Null(GitResolver.FindRepoRoot(Path.GetPathRoot(scratch)!));
        var broken = Path.Combine(scratch, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, ".git"), "not a gitdir pointer\n");
        Assert.Throws<InvalidOperationException>(() => GitResolver.ResolveGitDir(broken));
    }

    // Tests MA-UC-3/AP-007 [unit] (class fix): the literal ".git" resolves to
    // exactly the shared resolver — no production or test-infra source may
    // carry its own .git discovery walk.
    [Fact]
    public void GitLiteral_AppearsOnlyInTheSharedResolver()
    {
        var needle = "\"" + ".git" + "\""; // avoid self-match
        var offenders = new List<string>();
        var scanSet = SpikePaths.NonTestSourceFiles()
            .Concat(new[] { SpikePaths.P("tests", "SpikeTests", "TestInfra", "SpikePaths.cs") });
        foreach (var file in scanSet)
        {
            var rel = Path.GetRelativePath(SpikePaths.SpikeRoot, file).Replace('\\', '/');
            if (rel == "contracts/SpikeContracts/GitResolver.cs")
            {
                continue;
            }
            if (File.ReadAllText(file).Contains(needle, StringComparison.Ordinal))
            {
                offenders.Add(rel);
            }
        }
        Assert.True(offenders.Count == 0,
            "sources outside the shared GitResolver still perform their own .git discovery (AP-007/MA-UC-3): "
            + string.Join(", ", offenders));
    }
}

// ---------------------------------------------------- sentinel ledger (RB-3/HI-2)

public class MiniAudit1SentinelLedgerTests
{
    private static readonly IReadOnlyDictionary<string, string> StubEnv =
        new Dictionary<string, string> { ["PATH"] = "/usr/bin:/bin" };

    private static string MintLedger(string scratch, string nonce)
    {
        var ledger = Path.Combine(scratch, "sentinel", "ledger.json");
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        File.WriteAllText(ledger, $"{{ \"nonce\": \"{nonce}\", \"entries\": [] }}\n");
        return ledger;
    }

    private static string WriteStub(string scratch, string ledger, string nonce, string tag)
    {
        var stub = Path.Combine(scratch, "sentinel", $"stub-{tag}");
        File.WriteAllText(stub, HarnessCore.SentinelStubScript(ledger, nonce, tag));
        File.SetUnixFileMode(stub,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return stub;
    }

    // Tests INV-003/MA-RB-3 [integration]: two REAL stub invocations racing
    // the same window both survive — the append-only per-writer-unique design
    // cannot drop an entry the way the shared-tmp read-modify-write did.
    [Fact]
    public void ConcurrentStubInvocations_BothEntriesSurvive()
    {
        var scratch = SpikePaths.TestScratch("ma1-ledger-race");
        const string nonce = "nonce-ma1-race";
        var ledger = MintLedger(scratch, nonce);
        var stub1 = WriteStub(scratch, ledger, nonce, "P05-race00001");
        var stub2 = WriteStub(scratch, ledger, nonce, "P08-race00002");

        for (var round = 0; round < 5; round++)
        {
            var t1 = Task.Run(() => ManagedLauncher.Launch(new LaunchRequest(stub1, new[] { "-smt2", $"round{round}-a" }, scratch, StubEnv, 30)));
            var t2 = Task.Run(() => ManagedLauncher.Launch(new LaunchRequest(stub2, new[] { "-smt2", $"round{round}-b" }, scratch, StubEnv, 30)));
            Task.WaitAll(t1, t2);
            Assert.Equal(0, t1.Result.ExitCode);
            Assert.Equal(0, t2.Result.ExitCode);
        }

        var (_, total, foreign, malformed) = HarnessCore.ReadSentinelLedgerDetailed(ledger, null);
        Assert.Equal(10, total);   // 5 rounds x 2 concurrent writers — nothing dropped
        Assert.Equal(0, foreign);
        Assert.Equal(0, malformed);
        var (_, tag1Count, _, _) = HarnessCore.ReadSentinelLedgerDetailed(ledger, "P05-race00001");
        var (_, tag2Count, _, _) = HarnessCore.ReadSentinelLedgerDetailed(ledger, "P08-race00002");
        Assert.Equal(5, tag1Count);
        Assert.Equal(5, tag2Count);
    }

    // Tests INV-003/MA-HI-2 [integration]: sed/shell metacharacters and
    // whitespace/newline bytes in argv round-trip EXACTLY — the encoding is
    // immune to & | ] and newlines that corrupted the sed-based writer.
    [Fact]
    public void MetacharacterArgv_RoundTripsWithoutCorruption()
    {
        var scratch = SpikePaths.TestScratch("ma1-ledger-bytes");
        const string nonce = "nonce-ma1-bytes";
        var ledger = MintLedger(scratch, nonce);
        var stub = WriteStub(scratch, ledger, nonce, "P05-bytes0001");

        var hostileArgs = new[]
        {
            "-smt2",
            "a&b",
            "c|d",
            "e]f[g",
            "with \"quotes\" and \\backslashes\\",
            "line1\nline2",
            "",
            "$(echo pwned) `id` ;: *",
            "  leading and trailing  ",
        };
        var result = ManagedLauncher.Launch(new LaunchRequest(stub, hostileArgs, scratch, StubEnv, 30));
        Assert.Equal(0, result.ExitCode);

        var entries = HarnessCore.ReadSentinelEntries(ledger);
        var entry = Assert.Single(entries);
        Assert.Equal(nonce, entry.Nonce);
        Assert.Equal("probe:P05-bytes0001", entry.ProbeTag);
        Assert.Equal(hostileArgs, entry.Argv);

        var (_, count, _, malformed) = HarnessCore.ReadSentinelLedgerDetailed(ledger, "P05-bytes0001");
        Assert.Equal(1, count);
        Assert.Equal(0, malformed);
    }

    // Tests MA-RB-3/MA-HI-2 [unit] (declared malformed-line contract):
    // skip-and-COUNT — a truncated/garbage entry never zeroes the ledger and
    // never silently disappears (the probe predicates fail on malformed>0).
    [Fact]
    public void MalformedEntryFile_IsCountedNotSwallowed_AndDoesNotZeroTheLedger()
    {
        var scratch = SpikePaths.TestScratch("ma1-ledger-malformed");
        const string nonce = "nonce-ma1-malformed";
        var ledger = MintLedger(scratch, nonce);
        var stub = WriteStub(scratch, ledger, nonce, "P05-mal000001");
        Assert.Equal(0, ManagedLauncher.Launch(new LaunchRequest(stub, new[] { "-smt2" }, scratch, StubEnv, 30)).ExitCode);

        var entriesDir = Path.Combine(scratch, "sentinel", "entries");
        File.WriteAllText(Path.Combine(entriesDir, "entry-garbage1"), "this is !!! not ??? base64\n");

        var (_, count, _, malformed) = HarnessCore.ReadSentinelLedgerDetailed(ledger, null);
        Assert.Equal(1, count);      // the good entry still counts — no whole-file zeroing
        Assert.Equal(1, malformed);  // the bad one is COUNTED, not skipped silently
    }
}

// ------------------------------------------------------------ verdict units

public class MiniAudit1VerdictUnitTests
{
    private static ProbeManifest Manifest() => ProbeManifest.Load(SpikePaths.P("manifest", "probe-manifest.json"));

    private static List<ProbeResult> FullPassingSet() => Manifest().Entries
        .Select(e => new ProbeResult(new ProbeKey(e.ProbeId, e.Route), ProbeStatus.Pass)).ToList();

    // Tests INV-013/MA-VI-4 [unit]: the exit/report CONSISTENT-success cell is
    // NULL — a non-verdict that no caller can read as COMPATIBLE. Only genuine
    // findings are non-null.
    [Fact]
    public void MapExit_ConsistentSuccessCell_IsNull_NeverACompatibleVerdict()
    {
        var report = new RouteReport("run-ma1", "A",
            new List<ProbeResult> { new(new ProbeKey("P06", "A"), ProbeStatus.Pass) },
            ExitCodes.RouteProbesPassed, null, "<run-root>/route-a.json");
        Assert.Null(AdjudicationStateMachine.MapExit(ExitCodes.RouteProbesPassed, null, report));
    }

    // Tests INV-013/MA-VI-2 [unit] (probe-status x verdict-reason-variant):
    // a FAILING probe yields the probe-failure variant.
    [Fact]
    public void FirstNonPassing_Fail_YieldsProbeFailureVariant()
    {
        var set = FullPassingSet()
            .Select(p => p.Key == new ProbeKey("P06", "A") ? p with { Status = ProbeStatus.Fail } : p).ToList();
        var outcome = VerdictAggregator.ComputeRouteVerdict(Manifest(), "A", set, SuiteStatus.Success);
        Assert.Equal(RouteState.Incomplete, outcome.State);
        var reason = Assert.IsType<VerdictReason.ProbeFailure>(outcome.Reason);
        Assert.Equal(new ProbeKey("P06", "A"), reason.FirstFailingByManifestOrder);
    }

    // Tests INV-013/MA-VI-2 [unit]: an INCOMPLETE probe (typed prerequisite
    // cause, e.g. solver-unavailable) yields PREREQUISITE-FAILURE carrying the
    // probe key + its typed detail — never a probe-failure relabel.
    [Fact]
    public void FirstNonPassing_Incomplete_YieldsPrerequisiteFailureVariant_WithProbeKeyAndDetail()
    {
        var set = FullPassingSet()
            .Select(p => p.Key == new ProbeKey("P06", "A")
                ? p with { Status = ProbeStatus.Incomplete, Detail = "solver-unavailable: <run-root>/solver/z3-4.12.1/bin/z3 missing (BND-002 fail-closed)" }
                : p)
            .ToList();
        var outcome = VerdictAggregator.ComputeRouteVerdict(Manifest(), "A", set, SuiteStatus.Success);
        Assert.Equal(RouteState.Incomplete, outcome.State);
        var reason = Assert.IsType<VerdictReason.PrerequisiteFailure>(outcome.Reason);
        Assert.Contains("P06(A)", reason.Detail);
        Assert.Contains("solver-unavailable", reason.Detail);
    }

    // Tests INV-004/MA-ID-1 [unit]: the P06 required-observable gate — a task
    // whose typed surface exposed no resource usage yields the typed
    // absent-capability detail; full observation yields null.
    [Fact]
    public void P06ResourceUsageGate_FailsTyped_WhenObservableAbsent()
    {
        VerificationRun Run(bool observed) => new(
            VerificationStage.Verification,
            new[] { "M" },
            new[] { new VerificationTask("M", SolverOutcome.Valid, observed) },
            Array.Empty<TypedDiagnostic>(),
            0,
            "solver");
        Assert.Null(HarnessCore.AbsentResourceUsageCapability(Run(observed: true)));
        var detail = HarnessCore.AbsentResourceUsageCapability(Run(observed: false));
        Assert.NotNull(detail);
        Assert.Contains("solver_resource_usage_observed", detail);
        Assert.Contains("absent", detail, StringComparison.OrdinalIgnoreCase);
    }

    // Tests INV-009/PR-002 [unit]: a FALSIFIED retired registry row is refused
    // at runtime by the compiled-in history anchor; a REMOVED retired row too.
    [Fact]
    public void Pr002_FalsifiedOrRemovedRetiredRegistryRow_RefusedAtRuntime()
    {
        var scratch = SpikePaths.TestScratch("ma1-pr002");
        var schema = SpikePaths.P("schema", "evidence-schema.json");
        var registryText = File.ReadAllText(SpikePaths.P("schema", "schema-version-registry.json"));

        var falsified = Path.Combine(scratch, "registry-falsified.json");
        File.WriteAllText(falsified, registryText.Replace(
            SpecConstants.EvidenceSchemaV1Sha256, new string('d', 64)));
        var ex1 = Assert.Throws<InvalidOperationException>(() => EvidenceSchema.ValidateSchemaFile(schema, falsified));
        Assert.Contains("retired", ex1.Message, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(registryText);
        var keptRows = doc.RootElement.GetProperty("versions").EnumerateArray()
            .Where(v => v.GetProperty("version").GetInt32() != 1)
            .Select(v => v.GetRawText());
        var removed = Path.Combine(scratch, "registry-removed.json");
        File.WriteAllText(removed, "{ \"versions\": [ " + string.Join(", ", keptRows) + " ] }");
        var ex2 = Assert.Throws<InvalidOperationException>(() => EvidenceSchema.ValidateSchemaFile(schema, removed));
        Assert.Contains("retired", ex2.Message, StringComparison.OrdinalIgnoreCase);

        // Anchor agreement: compiled history == test-suite constants (three-point change).
        Assert.Equal(SpecConstants.EvidenceSchemaV1Sha256, VerdictAggregator.RetiredSchemaRegistryRows[1]);
    }

    // Tests RS-002/PR-003 [unit]: the probe-manifest STARTUP anchor — the
    // compiled digest equals the test-suite constant, the committed file
    // passes, and a tampered manifest is refused with the typed message. A
    // wiring scan proves both the route-child startup path and the aggregator
    // startup path consume the validator.
    [Fact]
    public void Pr003_ProbeManifestStartupAnchor_RefusesTamperedManifest_AndIsWired()
    {
        Assert.Equal(SpecConstants.ProbeManifestSha256, VerdictAggregator.ProbeManifestSha256TrustAnchor);
        VerdictAggregator.ValidateProbeManifestFile(SpikePaths.P("manifest", "probe-manifest.json")); // committed file passes

        var scratch = SpikePaths.TestScratch("ma1-pr003");
        var tampered = Path.Combine(scratch, "probe-manifest.json");
        File.WriteAllText(tampered, File.ReadAllText(SpikePaths.P("manifest", "probe-manifest.json")) + "\n");
        var ex = Assert.Throws<InvalidOperationException>(() => VerdictAggregator.ValidateProbeManifestFile(tampered));
        Assert.Contains("trust anchor", ex.Message);

        Assert.Contains("ValidateProbeManifestFile", File.ReadAllText(SpikePaths.P("contracts", "SpikeContracts", "HarnessCore.cs")));
        Assert.Contains("ValidateProbeManifestFile", File.ReadAllText(SpikePaths.P("aggregator", "SpikeAggregator", "Program.cs")));
    }

    // Tests BND-002/BND-004/PR-007 [unit]: the pin FILES are consumed — the
    // committed files pass the compiled anchors (and supply glibc_floor), any
    // corruption is a typed refusal, and the execution-path call sites exist
    // (route-child startup, aggregator startup, control-cell startup).
    [Fact]
    public void Pr007_PinFiles_AnchoredAndConsumed_CorruptionRefused()
    {
        var z3Pin = PinFiles.ValidateZ3Pin(SpikePaths.SpikeRoot);
        Assert.False(string.IsNullOrEmpty(z3Pin.GlibcFloor));
        PinFiles.ValidateNet8ControlPin(SpikePaths.SpikeRoot);
        Assert.Equal(SpecConstants.Net8ControlArchiveSha256, PinFiles.Net8ControlArchiveSha256Anchor);
        Assert.Equal(SpecConstants.Net8ControlRuntimeVersion, PinFiles.Net8ControlRuntimeVersionAnchor);

        var scratch = SpikePaths.TestScratch("ma1-pr007");
        Directory.CreateDirectory(Path.Combine(scratch, "config"));
        File.WriteAllText(Path.Combine(scratch, "config", "z3-pin.json"),
            File.ReadAllText(SpikePaths.P("config", "z3-pin.json")).Replace(SolverLayout.Z3PinnedSha256, new string('e', 64)));
        File.WriteAllText(Path.Combine(scratch, "config", "net8-control-pin.json"),
            File.ReadAllText(SpikePaths.P("config", "net8-control-pin.json")).Replace("\"8.0.29\"", "\"8.0.999\""));
        var exZ3 = Assert.Throws<InvalidOperationException>(() => PinFiles.ValidateZ3Pin(scratch));
        Assert.Contains("PR-007", exZ3.Message);
        var exNet8 = Assert.Throws<InvalidOperationException>(() => PinFiles.ValidateNet8ControlPin(scratch));
        Assert.Contains("PR-007", exNet8.Message);

        Assert.Contains("ValidateZ3Pin", File.ReadAllText(SpikePaths.P("contracts", "SpikeContracts", "HarnessCore.cs")));
        var aggregatorSource = File.ReadAllText(SpikePaths.P("aggregator", "SpikeAggregator", "Program.cs"));
        Assert.Contains("ValidateZ3Pin", aggregatorSource);
        Assert.Contains("ValidateNet8ControlPin", aggregatorSource);
        Assert.Contains("ValidateNet8ControlPin", File.ReadAllText(SpikePaths.P("contracts", "SpikeContracts", "ControlCore.cs")));
    }

    // Tests MA-UC-4 [unit]: the SOLUTION is the authoritative enforcement
    // perimeter — its project set equals SpikePaths.Projects, and every
    // project owning a packages.lock.json appears in run-spike.sh's
    // lock-receipt list, so an added project cannot silently escape lock-pin
    // enforcement or the prohibition scans.
    [Fact]
    public void Uc4_SolutionProjectSet_MatchesSpikePathsProjects_AndLockReceiptList()
    {
        var slnText = File.ReadAllText(Path.Combine(SpikePaths.SpikeRoot, "DafnyCompatSpike.sln"));
        var slnProjects = System.Text.RegularExpressions.Regex
            .Matches(slnText, "Project\\(\"\\{[^}]+\\}\"\\)\\s*=\\s*\"([^\"]+)\",\\s*\"([^\"]+\\.csproj)\"")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Replace('\\', '/'), StringComparer.Ordinal);
        Assert.Equal(SpikePaths.Projects.Keys.ToHashSet(), slnProjects.Keys.ToHashSet());
        foreach (var (name, rel) in SpikePaths.Projects)
        {
            Assert.Equal(rel, slnProjects[name]);
        }

        var lockLine = File.ReadAllLines(SpikePaths.P("scripts", "run-spike.sh"))
            .SingleOrDefault(l => l.TrimStart().StartsWith("for lock in ", StringComparison.Ordinal));
        Assert.False(lockLine is null, "run-spike.sh no longer enumerates lock-receipt project directories (MA-UC-4)");
        var receiptDirs = lockLine!.TrimStart()["for lock in ".Length..].Split(';')[0]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, rel) in SpikePaths.Projects)
        {
            if (File.Exists(SpikePaths.LockFilePath(name)))
            {
                var dir = Path.GetDirectoryName(rel)!.Replace('\\', '/');
                Assert.True(receiptDirs.Contains(dir),
                    $"project {name} has a packages.lock.json but its directory '{dir}' is missing from run-spike.sh's lock-receipt list (MA-UC-4)");
            }
        }
    }

    // Tests RS-005/MA-ED-4 [unit] (class fix): no environment-scoped test may
    // report success when its scoped body did not run — the silent
    // early-return RID-gate pattern is banned from test sources (gates go
    // through SpikePaths.RequireProvenRid, which throws loudly).
    [Fact]
    public void Ed4_NoSilentRidEarlyReturn_InTestSources()
    {
        var gate = "!SpikePaths." + "IsLinuxX64"; // concatenated to avoid self-match
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(SpikePaths.P("tests", "SpikeTests"), "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(gate, StringComparison.Ordinal))
                {
                    continue;
                }
                for (var j = i; j < Math.Min(i + 4, lines.Length); j++)
                {
                    if (lines[j].TrimEnd().EndsWith("return;", StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                        break;
                    }
                }
            }
        }
        Assert.True(offenders.Count == 0,
            "silent RID early-return gates found (a scope-gate exit must be a non-pass outcome — use SpikePaths.RequireProvenRid): "
            + string.Join(", ", offenders));
    }
}

// ------------------------------------------------- aggregator-launch armor

[Collection(SharedStateMutatingCollection.Name)]
public class MiniAudit1AggregatorTests
{
    private static string SchemaPath => SpikePaths.P("schema", "evidence-schema.json");
    private static string RegistryPath => SpikePaths.P("schema", "schema-version-registry.json");

    private static string NewRunId() => $"runid-ma1-{Guid.NewGuid():N}"[..26];

    private static void ProvisionInto(string runRoot)
    {
        var provision = Launch.Script("scripts/provision-z3.sh", null, "--run-root", runRoot);
        Assert.True(provision.ExitCode == 0, $"provisioning failed: {provision.StdErr}");
    }

    private static JsonElement RouteVerdict(JsonDocument runReport, string route) =>
        runReport.RootElement.GetProperty("deterministic").GetProperty("route_verdicts").EnumerateArray()
            .Single(v => v.GetProperty("route").GetString() == route);

    private static (LaunchResult Result, string RunReportPath) RunAggregator(
        string scratch, string runId, string aRep, string bRep,
        string suiteStatus = "unknown", string? solverOverride = null)
    {
        var aggregator = RunContext.Resolve("SpikeAggregator");
        var env = EnvProfiles.For("harness", RunContext.RunRoot());
        var runReport = Path.Combine(scratch, "run-report.json");
        var argv = new List<string>
        {
            "--manifest", SpikePaths.P("manifest", "probe-manifest.json"),
            "--schema", SchemaPath,
            "--registry", RegistryPath,
            "--run-id", runId, "--nonce", "nonce-ma1", "--run-root", scratch,
            "--restore-receipt", Path.Combine(scratch, "restore-receipt.json"),
            "--build-receipt", Path.Combine(scratch, "build-receipt.json"),
            "--suite-status", suiteStatus,
            "--report", $"A={aRep}:0", "--report", $"B={bRep}:0",
            "--out", runReport, "--aggregation-receipt", Path.Combine(scratch, "agg.json"),
        };
        if (solverOverride is not null)
        {
            argv.Add("--solver");
            argv.Add(solverOverride);
        }
        var result = Launch.Dll(aggregator.AbsolutePath, env, argv.ToArray());
        return (result, runReport);
    }

    private static void WriteReceipts(string dir, string runId)
    {
        File.WriteAllText(Path.Combine(dir, "restore-receipt.json"),
            $"{{ \"run_id\": \"{runId}\", \"nonce\": \"nonce-ma1\", \"exit\": 0, \"argv\": [\"dotnet\",\"restore\"],\n" +
            "  \"p01_partitions\": { \"A\": { \"projects\": [], \"exit\": 0 }, \"B\": { \"projects\": [], \"exit\": 0 } },\n" +
            "  \"lock_sha256\": {} }\n");
        File.WriteAllText(Path.Combine(dir, "build-receipt.json"),
            $"{{ \"run_id\": \"{runId}\", \"nonce\": \"nonce-ma1\", \"exit\": 0, \"sdk_version\": \"{SpecConstants.SdkPin}\", \"artifacts\": [] }}\n");
    }

    private static void WriteSuiteReceipt(string runRoot, string runId, string nonce, int suiteExit)
    {
        var path = Path.Combine(runRoot, RunLayout.SuiteReceiptRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            $"{{ \"run_id\": \"{runId}\", \"nonce\": \"{nonce}\", \"suite_exit\": {suiteExit}, \"source_manifest_sha256\": \"unknown\" }}\n");
    }

    private static string RouteReportJson(string route, string runId, string? probeStatusOverride = null, string? overrideProbe = null, bool duplicateP06 = false)
    {
        var probes = new List<string>();
        foreach (var p in new[] { "P03", "P05", "P06", "P07", "P08", "P09", "P10", "P11", "P12" })
        {
            var status = p == overrideProbe ? probeStatusOverride! : "pass";
            var detail = p == overrideProbe && status != "pass"
                ? "\"solver-unavailable: injected typed incomplete (MA-VI-2 induced)\""
                : "null";
            probes.Add($"      {{ \"probe\": \"{p}\", \"route\": \"{route}\", \"status\": \"{status}\", \"detail\": {detail} }}");
            if (p == "P06" && duplicateP06)
            {
                probes.Add($"      {{ \"probe\": \"P06\", \"route\": \"{route}\", \"status\": \"fail\", \"detail\": \"duplicate composite key (MA-VI-1 induced)\" }}");
            }
        }
        return "{\n" +
               "  \"evidence_schema_version\": 2,\n" +
               $"  \"evidence_schema_sha256\": \"{SpecConstants.EvidenceSchemaSha256}\",\n" +
               $"  \"probe_manifest_sha256\": \"{SpecConstants.ProbeManifestSha256}\",\n" +
               $"  \"run_id\": \"{runId}\",\n" +
               "  \"kind\": \"route-report\",\n" +
               "  \"binding_identity\": {\n" +
               "    \"run_directory\": \"out/ma1\", \"git_commit_id\": \"abc\", \"git_dirty_flag\": false,\n" +
               "    \"host_rid\": \"linux-x64\", \"sdk_version\": \"10.0.302\", \"actual_runtime_version\": \"10.0.2\"\n" +
               "  },\n" +
               "  \"deterministic\": {\n" +
               "    \"per_probe_results\": [\n" + string.Join(",\n", probes) + "\n    ],\n" +
               "    \"final_suite_status\": \"unknown\"\n" +
               "  },\n" +
               "  \"volatile\": {}\n" +
               "}\n";
    }

    private static (string A, string B) StageReports(string scratch, string runId, string? bStatus = null, string? bProbe = null, bool duplicateP06inB = false)
    {
        var aRep = Path.Combine(scratch, "route-a.json");
        var bRep = Path.Combine(scratch, "route-b.json");
        File.WriteAllText(aRep, RouteReportJson("A", runId));
        File.WriteAllText(bRep, RouteReportJson("B", runId, bStatus, bProbe, duplicateP06inB));
        return (aRep, bRep);
    }

    // Tests INV-006/MA-VI-6 [integration]: a FORGED --suite-status success
    // contradicting the nonce-bound suite receipt is REFUSED — no run report
    // is minted.
    [Fact]
    public void Vi6_ForgedSuiteStatus_ContradictingReceipt_IsRefused()
    {
        var scratch = SpikePaths.TestScratch("ma1-vi6-forged");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        WriteSuiteReceipt(scratch, runId, "nonce-ma1", suiteExit: 1);
        var (aRep, bRep) = StageReports(scratch, runId);

        var (result, runReport) = RunAggregator(scratch, runId, aRep, bRep, suiteStatus: "success");
        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
        Assert.False(File.Exists(runReport), "a refused forged suite-status still minted a run report (MA-VI-6)");
        Assert.Contains("suite receipt", result.StdOut + result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    // Tests INV-006/MA-VI-6 [integration]: a stale receipt (wrong run_id) is
    // refused outright.
    [Fact]
    public void Vi6_SuiteReceipt_WrongRunId_IsRefused()
    {
        var scratch = SpikePaths.TestScratch("ma1-vi6-stalereceipt");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        WriteSuiteReceipt(scratch, "runid-SOMEONE-ELSES-RUN00", "nonce-ma1", suiteExit: 0);
        var (aRep, bRep) = StageReports(scratch, runId);

        var (result, _) = RunAggregator(scratch, runId, aRep, bRep, suiteStatus: "success");
        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
        Assert.Contains("suite receipt", result.StdOut + result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    // Tests INV-006/MA-VI-6 [integration]: WITHOUT a receipt, a bare
    // --suite-status success claim is DOWNGRADED to unknown (fail-closed —
    // COMPATIBLE is unreachable from an unvalidated claim); with a matching
    // success receipt, success is derived and recorded.
    [Fact]
    public void Vi6_SuiteStatus_DerivedFromReceipt_UnvalidatedClaimDowngraded()
    {
        var scratch = SpikePaths.TestScratch("ma1-vi6-derive");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        var (aRep, bRep) = StageReports(scratch, runId);

        // No receipt: the success claim downgrades to unknown.
        var (bare, bareReport) = RunAggregator(scratch, runId, aRep, bRep, suiteStatus: "success");
        Assert.Equal(ExitCodes.RouteProbesPassed, bare.ExitCode); // all-pass variance shape (QA-016)
        using (var doc = Launch.Report(bareReport))
        {
            Assert.Equal("unknown", doc.RootElement.GetProperty("deterministic").GetProperty("final_suite_status").GetString());
            foreach (var route in new[] { "A", "B" })
            {
                Assert.NotEqual("COMPATIBLE", RouteVerdict(doc, route).GetProperty("state").GetString());
            }
        }

        // Matching receipt: success is DERIVED from the receipt.
        WriteSuiteReceipt(scratch, runId, "nonce-ma1", suiteExit: 0);
        var (derived, derivedReport) = RunAggregator(scratch, runId, aRep, bRep, suiteStatus: "success");
        Assert.Equal(ExitCodes.RouteProbesPassed, derived.ExitCode);
        using (var doc = Launch.Report(derivedReport))
        {
            Assert.Equal("success", doc.RootElement.GetProperty("deterministic").GetProperty("final_suite_status").GetString());
        }
    }

    // Tests INV-006/MA-VI-1 [integration]: a duplicate composite key in a
    // route child's raw report array surfaces as MALFORMED-REPORT in BOTH the
    // verdict AND the emitted per_probe_results view — never a first-wins
    // "pass" row beside an INCOMPLETE verdict.
    [Fact]
    public void Vi1_DuplicateCompositeKey_MalformedInVerdict_AndPerProbeView()
    {
        var scratch = SpikePaths.TestScratch("ma1-vi1-dup");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        var (aRep, bRep) = StageReports(scratch, runId, duplicateP06inB: true);

        var (result, runReport) = RunAggregator(scratch, runId, aRep, bRep);
        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
        using var doc = Launch.Report(runReport);

        var bReason = RouteVerdict(doc, "B").GetProperty("verdict_reason");
        Assert.Equal("malformed-report", bReason.GetProperty("variant").GetString());
        Assert.Contains("duplicate", bReason.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);

        // The per-probe VIEW agrees: route B child entries are the
        // malformed-report state, not first-wins "pass".
        foreach (var p in doc.RootElement.GetProperty("deterministic").GetProperty("per_probe_results").EnumerateArray())
        {
            if (p.GetProperty("route").GetString() != "B" || p.GetProperty("probe").GetString() == "P01")
            {
                continue;
            }
            Assert.Equal("incomplete", p.GetProperty("status").GetString());
            Assert.Contains("malformed", p.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    // Tests INV-003/MA-VI-7 [integration]: a --solver override outside the
    // standard layout can NEVER produce an unqualified P04 pass — the skipped
    // provisioning cross-check is named, P04 records Incomplete, and the
    // run-level verdict carries the prerequisite-failure variant naming
    // P04(shared) (the MA-VI-2 aggregator-channel pair for status=incomplete).
    [Fact]
    public void Vi7_SolverOverride_P04NeverUnqualifiedPass()
    {
        var scratch = SpikePaths.TestScratch("ma1-vi7-override");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        var (aRep, bRep) = StageReports(scratch, runId);

        // Override: a byte-identical COPY of the provisioned binary at a
        // non-standard path — banner and digest are genuine, only the layout
        // (and hence the provisioning cross-check) differs.
        var provisioned = Path.Combine(scratch, SolverLayout.SolverRelativePath);
        var overridePath = Path.Combine(scratch, "custom-solver", "z3");
        Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
        File.Copy(provisioned, overridePath);
        File.SetUnixFileMode(overridePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var (result, runReport) = RunAggregator(scratch, runId, aRep, bRep, solverOverride: overridePath);
        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
        using var doc = Launch.Report(runReport);

        var p04 = doc.RootElement.GetProperty("deterministic").GetProperty("per_probe_results").EnumerateArray()
            .Single(p => p.GetProperty("probe").GetString() == "P04");
        Assert.Equal("incomplete", p04.GetProperty("status").GetString());
        Assert.Contains("provisioning cross-check not applicable", p04.GetProperty("detail").GetString());

        foreach (var route in new[] { "A", "B" })
        {
            var reason = RouteVerdict(doc, route).GetProperty("verdict_reason");
            Assert.Equal("prerequisite-failure", reason.GetProperty("variant").GetString());
            Assert.Contains("P04(shared)", reason.GetProperty("detail").GetString());
        }
    }

    // Tests INV-009/MA-ED-2 [integration] (class fix): per-kind field
    // coverage — every schema-declared partition field this map requires per
    // report kind is PRODUCED by fresh reports (the substrate run's route and
    // control reports; a fresh in-test aggregation for the run report), so
    // "declared but never produced" fails loudly. Map anchored to the schema
    // digest: bumping the schema forces reviewing this map.
    [Fact]
    public void Ed2_PerKindFieldCoverage_DeclaredFieldsAreProduced()
    {
        Assert.Equal(SpecConstants.EvidenceSchemaSha256, SpikePaths.Sha256File(SchemaPath)); // map anchored to THIS schema

        var runRoot = RunContext.RunRoot();
        var requiredByKind = new Dictionary<string, (string[] Binding, string[] Deterministic)>
        {
            ["route-report"] = (
                new[] { "run_product_paths_concrete", "raw_digests_of_path_bearing_generated_files", "hostfxr_identity_concrete", "corelib_identity_concrete", "sentinel_nonce", "solver_path_concrete" },
                new[] { "deps_json_digests_normalized", "solver_resource_usage_observed", "sentinel_ledger_outcomes", "executed_solver_sha256", "per_probe_results", "final_suite_status" }),
            ["control-report"] = (
                new[] { "hostfxr_identity_concrete", "corelib_identity_concrete" },
                new[] { "per_probe_results", "final_suite_status" }),
        };

        void AssertCovered(string path, string kind)
        {
            using var doc = Launch.Report(path);
            Assert.Equal(kind, doc.RootElement.GetProperty("kind").GetString());
            var (bindingFields, detFields) = requiredByKind[kind];
            var binding = doc.RootElement.GetProperty("binding_identity");
            foreach (var field in bindingFields)
            {
                Assert.True(binding.TryGetProperty(field, out var v) && v.ValueKind != JsonValueKind.Null,
                    $"{Path.GetFileName(path)} ({kind}): schema-declared binding field '{field}' has no producer (MA-ED-2)");
            }
            var det = doc.RootElement.GetProperty("deterministic");
            foreach (var field in detFields)
            {
                Assert.True(det.TryGetProperty(field, out var v) && v.ValueKind != JsonValueKind.Null,
                    $"{Path.GetFileName(path)} ({kind}): schema-declared deterministic field '{field}' has no producer (MA-ED-2)");
            }
        }

        AssertCovered(Path.Combine(runRoot, "reports", "route-a.json"), "route-report");
        AssertCovered(Path.Combine(runRoot, "reports", "route-b.json"), "route-report");
        AssertCovered(Path.Combine(runRoot, "reports", "control-a.json"), "control-report");
        AssertCovered(Path.Combine(runRoot, "reports", "control-b.json"), "control-report");

        // run-report: freshly aggregated in-test so glibc_floor (pin-sourced)
        // and the binding record are proven produced, independent of sample regen.
        var scratch = SpikePaths.TestScratch("ma1-ed2-runreport");
        ProvisionInto(scratch);
        var runId = NewRunId();
        WriteReceipts(scratch, runId);
        var (aRep, bRep) = StageReports(scratch, runId);
        var (result, runReport) = RunAggregator(scratch, runId, aRep, bRep);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);
        using var runDoc = Launch.Report(runReport);
        Assert.Equal("run-report", runDoc.RootElement.GetProperty("kind").GetString());
        var runBinding = runDoc.RootElement.GetProperty("binding_identity");
        foreach (var field in new[] { "glibc_floor", "restore_argv_concrete", "solver_path_concrete" })
        {
            Assert.True(runBinding.TryGetProperty(field, out var v) && v.ValueKind != JsonValueKind.Null,
                $"run-report: schema-declared binding field '{field}' has no producer (MA-ED-2)");
        }
        Assert.Equal(PinFiles.ValidateZ3Pin(SpikePaths.SpikeRoot).GlibcFloor,
            runBinding.GetProperty("glibc_floor").GetString());
        var runDet = runDoc.RootElement.GetProperty("deterministic");
        foreach (var field in new[] { "route_verdicts", "per_probe_results", "final_suite_status", "exit_report_matrix_outcome" })
        {
            Assert.True(runDet.TryGetProperty(field, out var v) && v.ValueKind != JsonValueKind.Null,
                $"run-report: schema-declared deterministic field '{field}' has no producer (MA-ED-2)");
        }
    }
}

// --------------------------------------------------- harness-launch armor

[Collection(SharedStateMutatingCollection.Name)]
public class MiniAudit1HarnessTests
{
    // Tests INV-002/MA-VI-3 [integration]: an out-of-universe FAMILY-named
    // assembly injected at the CAPTURE layer (loaded via the harness's
    // induced-failure hook from a path outside the deps.json mapping) fails
    // P03 with a typed detail — the filtered equality universe can no longer
    // pass vacuously over it.
    [Fact]
    public void Vi3_OutOfUniverseFamilyAssembly_FailsP03_AtCaptureLayer()
    {
        var scratch = SpikePaths.TestScratch("ma1-vi3-inject");
        var harnessDir = Path.GetDirectoryName(RunContext.Resolve("RouteAHarness").AbsolutePath)!;
        var familyDll = Directory.EnumerateFiles(harnessDir, "Dafny*.dll").OrderBy(f => f, StringComparer.Ordinal).First();
        var injected = Path.Combine(scratch, "stray", Path.GetFileName(familyDll));
        Directory.CreateDirectory(Path.GetDirectoryName(injected)!);
        File.Copy(familyDll, injected);

        var reportPath = Path.Combine(scratch, "report.json");
        var result = Launch.Harness("A", "--probe", "P03", "--load-extra-assembly", injected, "--out", reportPath);
        Assert.Equal(ExitCodes.ProbeFailure, result.ExitCode);

        using var doc = Launch.Report(reportPath);
        var p03 = doc.RootElement.GetProperty("deterministic").GetProperty("per_probe_results").EnumerateArray()
            .Single(p => p.GetProperty("probe").GetString() == "P03");
        Assert.Equal("fail", p03.GetProperty("status").GetString());
        Assert.Contains("out-of-universe", p03.GetProperty("detail").GetString());
        Assert.Contains("MA-VI-3", p03.GetProperty("detail").GetString());
    }
}
