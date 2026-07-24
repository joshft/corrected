// GREEN — shared route-harness orchestration (Dafny-free; INV-008). Both route
// harness executables delegate here: argv parsing, the fail-closed startup
// gate (INV-003/EA-005), always-on decoy planting, sentinel-ledger machinery
// (RS-003d), probe execution through the ISpikeRouteAdapter seam, evidence
// construction bound to the committed schema (INV-009), atomic emission
// (RS-017f), the exit-code contract (INV-013), and the per-route terminal
// verdict summary (INV-014).
//
// This class launches NO processes (PRH-004: the managed launcher in
// Components.cs is the sole sanctioned launch site; the provisioned solver is
// only ever launched by Boogie inside the seam — the one permitted child
// outside both layers).

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace Corrected.Spike.Contracts;

public static class HarnessCore
{
    private sealed record ProbeSpec(string DefaultFixture, bool NeedsRealSolver, bool UsesSentinel);

    private static readonly IReadOnlyDictionary<string, ProbeSpec> ProbeSpecs = new Dictionary<string, ProbeSpec>(StringComparer.Ordinal)
    {
        ["P03"] = new("fixtures/ok.dfy", NeedsRealSolver: true, UsesSentinel: false),
        ["P05"] = new("fixtures/ok.dfy", NeedsRealSolver: false, UsesSentinel: true),
        ["P06"] = new("fixtures/ok.dfy", NeedsRealSolver: true, UsesSentinel: false),
        ["P07"] = new("fixtures/bad.dfy", NeedsRealSolver: true, UsesSentinel: false),
        ["P08"] = new("fixtures/syntax-error.dfy", NeedsRealSolver: false, UsesSentinel: true),
        ["P09"] = new("fixtures/resolve-error.dfy", NeedsRealSolver: false, UsesSentinel: true),
        ["P10"] = new("fixtures/classify.dfy", NeedsRealSolver: false, UsesSentinel: false),
        ["P11"] = new("fixtures/ok.dfy", NeedsRealSolver: true, UsesSentinel: false),
        ["P12"] = new("fixtures/incl-root.dfy", NeedsRealSolver: false, UsesSentinel: false),
        ["hygiene"] = new("fixtures/ok.dfy", NeedsRealSolver: false, UsesSentinel: false),
        ["closure-single"] = new("fixtures/ok.dfy", NeedsRealSolver: false, UsesSentinel: false),
        ["anchor-stdlib"] = new("fixtures/stdlib-anchor.dfy", NeedsRealSolver: true, UsesSentinel: false),
    };

    private sealed class RunState
    {
        public required string RouteId;
        public required ISpikeRouteAdapter Adapter;
        public required IReadOnlyList<string> Probes;
        public required string SpikeRoot;
        public required string RepoRoot;
        public required string RunRoot;
        public bool RunRootOverridden;
        public string? FixtureOverride;
        public string? SolverOverride;
        public string? StandardLibraries;
        public string? VerifyIncludedFiles;
        public string? ForcedExceptionProbe;
        public required string ReportPath;
        public string RunId = "";
        public string? SentinelNonce;
        public List<ProbeResult> Results = new();
        public List<Dictionary<string, object?>> NodeTable = new();
        public List<Dictionary<string, object?>> AdjudicationRecords = new();
        public Dictionary<string, string?> OptionReadback = new(StringComparer.Ordinal);
        public Dictionary<string, object?>? Canary;
        public Dictionary<string, List<string>> ClosureSets = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> TargetSets = new(StringComparer.Ordinal);
        public Dictionary<string, List<Dictionary<string, object?>>> OutcomeEnums = new(StringComparer.Ordinal);
        public Dictionary<string, bool> ResourceUsage = new(StringComparer.Ordinal);
        public Dictionary<string, long> RawResourceCounts = new(StringComparer.Ordinal);
        public Dictionary<string, string> FixtureDigests = new(StringComparer.Ordinal);
        public Dictionary<string, string> SidecarDigests = new(StringComparer.Ordinal);
        public List<(string SimpleName, string Path, string Sha, string? Version)> LoadedAssemblies = new();
        public Dictionary<string, string> ResolvedPackageVersions = new(StringComparer.Ordinal);
        public string? ExecutedSolverSha;
        public string? SolverArchiveSha;
        public bool DecoysPresent;
        public int DecoyInvocations;
        public string? PlantedDecoyScriptSha;
        public string? SentinelLedgerPath;
        public Task? PendingSentinelLeg;
        public string? SolverConcreteRel;
        public string? HarnessTfm;
        public int LedgerEntriesBefore;
        public int LedgerEntriesAfter;
        public int ForeignNonceEntries;
        public (string Path, string Sha)? HostfxrIdentity;
        public (string Path, string Sha)? CorelibIdentity;
        public List<string> FamilySweepViolations = new();
        public string? LoadExtraAssembly;
        public Dictionary<string, string> RawGeneratedFileDigests = new(StringComparer.Ordinal);
        public Dictionary<string, string> NormalizedDepsJsonDigests = new(StringComparer.Ordinal);
        public DateTime Started = DateTime.UtcNow;
    }

    public static int Run(string routeId, ISpikeRouteAdapter adapter, string[] args)
    {
        var state = new RunState
        {
            RouteId = routeId,
            Adapter = adapter,
            Probes = Array.Empty<string>(),
            SpikeRoot = FindSpikeRoot(),
            RepoRoot = "",
            RunRoot = "",
            ReportPath = "",
        };
        // Gitfile merge group (PR-001/MA-XC-1/MA-UC-3): the ONE shared resolver;
        // FAIL CLOSED below when no repo boundary exists — never a spike-root
        // fallback that later reads the wrong (or no) repo's HEAD.
        state.RepoRoot = GitResolver.FindRepoRoot(state.SpikeRoot) ?? "";
        state.RunId = "runid-" + Guid.NewGuid().ToString("N")[..16];

        try
        {
            ParseArgs(state, args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"route {routeId} verdict: INCOMPLETE; failed probes: -; argument error: {ex.Message}");
            return ExitCodes.Incomplete;
        }

        // Trust anchors: refuse to run against a substituted schema/manifest
        // (INV-009 — validation BEFORE any evidence is produced).
        var schemaPath = Path.Combine(state.SpikeRoot, "schema", "evidence-schema.json");
        var registryPath = Path.Combine(state.SpikeRoot, "schema", "schema-version-registry.json");
        try
        {
            EvidenceSchema.ValidateSchemaFile(schemaPath, registryPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"route {routeId} verdict: INCOMPLETE; schema trust anchor rejected: {ex.Message}");
            return ExitCodes.Incomplete;
        }

        // Further startup anchors, all BEFORE any probe runs:
        // - PR-003: the spec-owned probe manifest against the compiled digest;
        // - PR-007: the committed z3 pin file (it feeds P04/solver identity);
        // - gitfile fail-closed rule (MA-UC-3): no repo boundary → refusal,
        //   never evidence bound to an unknowable tree state.
        try
        {
            VerdictAggregator.ValidateProbeManifestFile(Path.Combine(state.SpikeRoot, "manifest", "probe-manifest.json"));
            PinFiles.ValidateZ3Pin(state.SpikeRoot);
            if (state.RepoRoot.Length == 0)
            {
                throw new InvalidOperationException(
                    "no git repo boundary (.git directory or gitfile) above the spike root — evidence git binding is impossible; refusing to run (INV-009/MA-UC-3 fail-closed)");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"route {routeId} verdict: INCOMPLETE; startup trust anchor rejected: {ex.Message}");
            return ExitCodes.Incomplete;
        }

        var plantFailure = PlantAlwaysOnDecoys(state);

        var gateFailure = plantFailure is not null
            ? $"always-on decoy planting failed — {plantFailure} (INV-003/QA-003 fail-closed)"
            : StartupGate(state);
        if (gateFailure is not null)
        {
            foreach (var probe in state.Probes)
            {
                state.Results.Add(new ProbeResult(new ProbeKey(probe, routeId), ProbeStatus.Incomplete,
                    $"startup-gate: {gateFailure} (INV-003/EA-005 fail-closed)"));
            }
            EmitReport(state, schemaPath);
            Summarize(state);
            Console.Error.WriteLine($"startup-gate failure: {gateFailure}");
            return ExitCodes.Incomplete;
        }

        // Prerequisite: the pinned solver must exist for solver-requiring probes.
        var solverPath = state.SolverOverride ?? Path.Combine(state.RunRoot, SolverLayout.SolverRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var needsSolver = state.Probes.Any(p => ProbeSpecs.TryGetValue(p, out var s) && s.NeedsRealSolver);
        if (needsSolver && !File.Exists(solverPath))
        {
            const string remediation = "run provisioning first: scripts/provision-z3.sh";
            foreach (var probe in state.Probes)
            {
                state.Results.Add(new ProbeResult(new ProbeKey(probe, routeId),
                    ProbeStatus.Incomplete,
                    ProbeSpecs.TryGetValue(probe, out var s2) && s2.NeedsRealSolver
                        ? $"solver-unavailable: {CanonicalizeValue(state, solverPath)} missing — {remediation} (BND-002 fail-closed)"
                        : "not executed: run aborted on solver prerequisite"));
            }
            EmitReport(state, schemaPath);
            Summarize(state);
            Console.Error.WriteLine($"prerequisite failure: solver missing at {solverPath} — {remediation}");
            return ExitCodes.Incomplete;
        }
        // QA-002: executed_solver_sha256 is ALWAYS the recomputed digest of the
        // file at the option-manifest solver path (set at emission from the
        // post-run readback); the BND-002 release-asset pin is recorded as the
        // separate solver_archive_sha256 field. Here we only note the retained
        // archive digest; the executed digest is computed at emission time.
        var retainedAsset = Path.Combine(state.RunRoot, "z3-4.12.1-x64-glibc-2.35.zip");
        if (File.Exists(retainedAsset))
        {
            state.SolverArchiveSha = Sha256File(retainedAsset);
        }
        if (File.Exists(solverPath))
        {
            state.SolverConcreteRel = Path.GetRelativePath(state.RunRoot, solverPath).Replace('\\', '/');
        }

        // QA-014: typed prover-startup check — the configured solver must
        // launch and identify itself before any verification probe runs; a
        // message-text sniff over diagnostics is no longer the classifier.
        if (needsSolver)
        {
            var banner = ManagedLauncher.Launch(new LaunchRequest(
                solverPath,
                new[] { "-version" },
                state.RunRoot,
                new Dictionary<string, string> { ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin" },
                TimeoutSeconds: 20));
            if (banner.ExitCode != 0 || !banner.StdOut.Contains("Z3 version", StringComparison.Ordinal))
            {
                const string remediation = "run provisioning first: scripts/provision-z3.sh";
                foreach (var probe in state.Probes)
                {
                    state.Results.Add(new ProbeResult(new ProbeKey(probe, routeId), ProbeStatus.Incomplete,
                        ProbeSpecs.TryGetValue(probe, out var s3) && s3.NeedsRealSolver
                            ? $"solver-unavailable: configured solver failed the typed startup check (exit {banner.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? banner.Signal}) — {remediation} (BND-002 fail-closed)"
                            : "not executed: run aborted on solver prerequisite"));
                }
                EmitReport(state, schemaPath);
                Summarize(state);
                Console.Error.WriteLine($"prerequisite failure: solver startup check failed for {solverPath} — {remediation}");
                return ExitCodes.Incomplete;
            }
        }

        // Sentinel machinery (RS-003d): the ledger FILE must be pre-created at
        // count zero before the run; the harness reads the nonce from the file.
        if (state.Probes.Any(p => ProbeSpecs.TryGetValue(p, out var s) && s.UsesSentinel))
        {
            var ledgerPath = Path.Combine(state.RunRoot, RunLayout.SentinelLedgerRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(ledgerPath))
            {
                foreach (var probe in state.Probes)
                {
                    state.Results.Add(new ProbeResult(new ProbeKey(probe, routeId), ProbeStatus.Incomplete,
                        "sentinel ledger missing — it must be PRE-CREATED at count zero before the run (RS-003d)"));
                }
                EmitReport(state, schemaPath);
                Summarize(state);
                Console.Error.WriteLine($"prerequisite failure: sentinel ledger missing at {ledgerPath}");
                return ExitCodes.Incomplete;
            }
            (state.SentinelNonce, state.LedgerEntriesBefore, state.ForeignNonceEntries, _) = ReadLedger(ledgerPath, null);
            state.SentinelLedgerPath = ledgerPath;
        }

        if (state.LoadExtraAssembly is not null)
        {
            // Induced-failure hook (MA-VI-3 test armor, like --force-probe-exception):
            // loads an assembly OUTSIDE the deps.json universe so the capture-layer
            // family sweep can be exercised end-to-end in a child-harness run.
            _ = Assembly.LoadFile(Path.GetFullPath(state.LoadExtraAssembly));
        }

        foreach (var probeId in state.Probes)
        {
            ExecuteProbe(state, probeId, solverPath, schemaPath);
        }

        // INV-003(b): every run asserts ZERO decoy invocations; the emitted
        // count comes from the actually-read decoy log (QA-003).
        var decoyLog = Path.Combine(state.RunRoot, "sentinel", "decoy-invocations.log");
        var decoyInvocations = File.Exists(decoyLog) ? File.ReadAllLines(decoyLog).Count(l => l.Length > 0) : 0;
        state.DecoyInvocations = decoyInvocations;
        if (decoyInvocations > 0)
        {
            state.Results = state.Results
                .Select(r => r.Status == ProbeStatus.Pass
                    ? r with { Status = ProbeStatus.Fail, Detail = $"decoy z3 was EXECUTED {decoyInvocations} time(s) during this run (INV-003)" }
                    : r)
                .ToList();
        }

        EmitReport(state, schemaPath);
        Summarize(state);

        if (state.Results.Any(r => r.Status == ProbeStatus.Fail))
        {
            return ExitCodes.ProbeFailure;
        }
        if (state.Results.Any(r => r.Status == ProbeStatus.Incomplete))
        {
            return ExitCodes.Incomplete;
        }
        return ExitCodes.RouteProbesPassed;
    }

    // ---------------------------------------------------------------- probes

    private static void ExecuteProbe(RunState state, string probeId, string solverPath, string schemaPath)
    {
        if (!ProbeSpecs.TryGetValue(probeId, out var spec))
        {
            state.Results.Add(new ProbeResult(new ProbeKey(probeId, state.RouteId), ProbeStatus.Fail,
                $"unknown probe id '{probeId}' — the plan is manifest-owned and cannot be extended at runtime (INV-006)"));
            return;
        }
        var fixture = state.FixtureOverride ?? spec.DefaultFixture;
        var fixtureKey = FixtureKey(state, fixture);
        RecordFixtureDigest(state, fixture, fixtureKey);

        string effectiveSolver;
        string? probeTag = null;
        if (spec.UsesSentinel)
        {
            probeTag = $"{probeId}-{Guid.NewGuid():N}"[..(probeId.Length + 9)];
            effectiveSolver = WriteSentinelStub(state, state.SentinelLedgerPath!, state.SentinelNonce!, probeTag);
        }
        else
        {
            effectiveSolver = state.SolverOverride ?? solverPath;
        }
        var options = BuildProbeOptions(state, effectiveSolver);

        try
        {
            if (state.ForcedExceptionProbe == probeId)
            {
                throw new InvalidOperationException(
                    $"forced probe exception injected for {probeId} (INV-006 induced-failure subset; RS-012)");
            }
            switch (probeId)
            {
                case "P03":
                    RunP03(state, fixture, fixtureKey, options);
                    break;
                case "P05":
                    RunP05(state, fixture, fixtureKey, options, probeTag!);
                    break;
                case "P06":
                    RunP06(state, fixture, fixtureKey, options, effectiveSolver);
                    break;
                case "P07":
                    RunP07(state, fixture, fixtureKey, options);
                    break;
                case "P08":
                    RunFrontend(state, "P08", fixture, fixtureKey, options, VerificationStage.Parse, probeTag!);
                    break;
                case "P09":
                    RunFrontend(state, "P09", fixture, fixtureKey, options, VerificationStage.Resolution, probeTag!);
                    break;
                case "P10":
                    RunP10(state, fixture, fixtureKey, options);
                    break;
                case "P11":
                    RunP11(state, fixture, fixtureKey, options);
                    break;
                case "P12":
                    RunP12(state, fixture, fixtureKey, options);
                    break;
                case "hygiene":
                    RunHygiene(state, fixture, fixtureKey, options);
                    break;
                case "closure-single":
                    RunClosureSingle(state, fixture, fixtureKey, options);
                    break;
                case "anchor-stdlib":
                    RunAnchorStdlib(state, fixture, fixtureKey, options);
                    break;
            }
        }
        catch (Exception ex)
        {
            HandleProbeException(state, probeId, fixture, ex, solverPath);
        }
    }

    private static void HandleProbeException(RunState state, string probeId, string fixture, Exception ex, string solverPath)
    {
        var flattened = Flatten(ex);
        var unreadable = flattened.Any(e =>
            e is UnauthorizedAccessException
            || e is DirectoryNotFoundException
            || (e is FileNotFoundException fnf && !fnf.Message.Contains("assembly", StringComparison.OrdinalIgnoreCase)));
        if (unreadable)
        {
            // BND-003: unreadable/missing fixture → probe INCOMPLETE, fail-closed.
            state.Results.Add(new ProbeResult(new ProbeKey(probeId, state.RouteId), ProbeStatus.Incomplete,
                $"fixture unreadable/inaccessible — fail-closed (BND-003): {flattened[^1].GetType().Name}: {flattened[^1].Message}"));
            return;
        }
        // INV-013: in-process failures enter the adjudication state machine as
        // UNADJUDICATED_IN_PROCESS_FAILURE — never a silent fall-through and
        // never any INCOMPATIBLE without adjudication.
        var payload = new UnadjudicatedInProcessFailure(
            UnadjudicatedVariant.TypedException,
            new ProbeKey(probeId, state.RouteId),
            Stage: "in-process",
            MinimalInputs: fixture,
            TypedDiagnostic: $"{ex.GetType().FullName}: {ex.Message}",
            IdentityEvidence: BuildIdentityEvidence(state, solverPath));
        _ = AdjudicationStateMachine.EnterInProcessFailure(payload);
        state.AdjudicationRecords.Add(new Dictionary<string, object?>
        {
            ["adjudication_record_id"] = $"unadj-{probeId}-{state.RouteId}",
            ["route"] = state.RouteId,
            ["terminal_state"] = "UnadjudicatedInProcessFailure",
            ["probe"] = probeId,
            ["variant"] = "typed-exception",
            ["stage"] = "in-process",
            ["minimal_inputs"] = fixture,
            ["typed_diagnostic"] = payload.TypedDiagnostic,
            ["identity_evidence_p01_p04"] = new Dictionary<string, object?>
            {
                ["restore_identity"] = payload.IdentityEvidence!.RestoreIdentity,
                ["sdk_build_identity"] = payload.IdentityEvidence.SdkBuildIdentity,
                ["loaded_assembly_identity"] = payload.IdentityEvidence.LoadedAssemblyIdentity,
                ["solver_identity"] = payload.IdentityEvidence.SolverIdentity,
            },
        });
        state.Results.Add(new ProbeResult(new ProbeKey(probeId, state.RouteId), ProbeStatus.Fail,
            $"UNADJUDICATED_IN_PROCESS_FAILURE (typed exception): {payload.TypedDiagnostic}"));
        Console.Error.WriteLine($"probe {probeId}: unadjudicated in-process failure: {payload.TypedDiagnostic}");
        Console.Error.WriteLine(ex.ToString());
    }

    private static List<Exception> Flatten(Exception ex)
    {
        var list = new List<Exception>();
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    list.AddRange(Flatten(inner));
                }
            }
            else
            {
                list.Add(cur);
            }
        }
        return list;
    }

    private static IdentityEvidence BuildIdentityEvidence(RunState state, string solverPath)
    {
        var lockPath = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.deps.json").FirstOrDefault();
        return new IdentityEvidence(
            state.RunId,
            RestoreIdentity: lockPath is not null ? $"deps.json sha256 {Sha256File(lockPath)}" : "deps.json unavailable",
            SdkBuildIdentity: $"sdk {ReadSdkPin(state)}; runtime {Environment.Version}",
            LoadedAssemblyIdentity: $"{AppDomain.CurrentDomain.GetAssemblies().Length} assemblies loaded; entry {Path.GetFileName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))}",
            SolverIdentity: File.Exists(solverPath) ? $"sha256 {Sha256File(solverPath)}" : "solver-unavailable");
    }

    private static Dictionary<string, string?> BuildProbeOptions(RunState state, string solverPath)
    {
        var options = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["--solver-path"] = solverPath,
            ["--resource-limit"] = "10000000",
            ["--verify-included-files"] = state.VerifyIncludedFiles ?? "false",
            ["--standard-libraries"] = state.StandardLibraries ?? "false",
        };
        return options;
    }

    private static void RunP06(RunState state, string fixture, string fixtureKey, Dictionary<string, string?> options, string effectiveSolver)
    {
        var run = state.Adapter.Verify(new FixtureInput(fixture, options));
        RecordRun(state, fixtureKey, run);

        var frontendErrors = run.Diagnostics.Count(d => d.Stage != VerificationStage.Verification);
        if (frontendErrors > 0)
        {
            state.Results.Add(new ProbeResult(new ProbeKey("P06", state.RouteId), ProbeStatus.Fail,
                $"frontend errors on a provable fixture ({frontendErrors} diagnostics; stage {run.CompletedThroughStage})"));
            return;
        }

        var isCanonical = fixtureKey == "fixtures/ok.dfy";
        if (isCanonical)
        {
            var sidecar = ReadSidecar(state, "ok.sidecar.json");
            var expectedTargets = sidecar.RootElement.GetProperty("expected_verification_targets")
                .EnumerateArray().Select(t => t.GetString()!).OrderBy(t => t, StringComparer.Ordinal).ToList();
            var minTasks = sidecar.RootElement.GetProperty("min_task_count").GetInt32();
            var actualTargets = run.TargetNames.OrderBy(t => t, StringComparer.Ordinal).ToList();
            if (!expectedTargets.SequenceEqual(actualTargets))
            {
                state.Results.Add(new ProbeResult(new ProbeKey("P06", state.RouteId), ProbeStatus.Fail,
                    $"verification-target set mismatch: expected [{string.Join(",", expectedTargets)}], got [{string.Join(",", actualTargets)}] (INV-004)"));
                return;
            }
            if (run.Tasks.Count < minTasks || minTasks <= 0)
            {
                state.Results.Add(new ProbeResult(new ProbeKey("P06", state.RouteId), ProbeStatus.Fail,
                    $"task count {run.Tasks.Count} below committed sidecar floor {minTasks} — vacuous pass denied (AP-010)"));
                return;
            }
        }
        if (run.Tasks.Count == 0)
        {
            state.Results.Add(new ProbeResult(new ProbeKey("P06", state.RouteId), ProbeStatus.Fail,
                "zero verification tasks produced — an empty/filtered program may not pass (INV-004)"));
            return;
        }
        if (run.Tasks.Any(t => t.Outcome != SolverOutcome.Valid))
        {
            state.Results.Add(new ProbeResult(new ProbeKey("P06", state.RouteId), ProbeStatus.Fail,
                "not every task outcome is a solver-produced Valid (INV-004)"));
            return;
        }
        // MA-ID-1: the RS-004 fail-closed observability rule is ENFORCED, not
        // just recorded — if the typed surface stopped exposing solver resource
        // usage, P06 fails and enters INV-013 as the absent-capability variant
        // of UNADJUDICATED_IN_PROCESS_FAILURE (never a silent pass).
        var absentUsage = AbsentResourceUsageCapability(run);
        if (absentUsage is not null)
        {
            var payload = new UnadjudicatedInProcessFailure(
                UnadjudicatedVariant.AbsentRequiredCapability,
                new ProbeKey("P06", state.RouteId),
                Stage: "verification",
                MinimalInputs: fixture,
                TypedDiagnostic: absentUsage,
                IdentityEvidence: BuildIdentityEvidence(state, effectiveSolver));
            _ = AdjudicationStateMachine.EnterInProcessFailure(payload);
            state.AdjudicationRecords.Add(new Dictionary<string, object?>
            {
                ["adjudication_record_id"] = $"unadj-P06-{state.RouteId}-absent-capability",
                ["route"] = state.RouteId,
                ["terminal_state"] = "UnadjudicatedInProcessFailure",
                ["probe"] = "P06",
                ["variant"] = "absent-required-capability",
                ["stage"] = "verification",
                ["minimal_inputs"] = fixture,
                ["typed_diagnostic"] = absentUsage,
                ["identity_evidence_p01_p04"] = new Dictionary<string, object?>
                {
                    ["restore_identity"] = payload.IdentityEvidence!.RestoreIdentity,
                    ["sdk_build_identity"] = payload.IdentityEvidence.SdkBuildIdentity,
                    ["loaded_assembly_identity"] = payload.IdentityEvidence.LoadedAssemblyIdentity,
                    ["solver_identity"] = payload.IdentityEvidence.SolverIdentity,
                },
            });
            state.Results.Add(new ProbeResult(new ProbeKey("P06", state.RouteId), ProbeStatus.Fail,
                $"UNADJUDICATED_IN_PROCESS_FAILURE (absent-capability): {absentUsage}"));
            return;
        }
        // Executed-solver identity: the digest of the binary actually executed
        // is recorded in evidence (executed_solver_sha256); the ARCHIVE pin
        // (SolverLayout.Z3PinnedSha256) is verified by provisioning at intake
        // (BND-002) — an archive digest can never equal its extracted member's.
        state.Results.Add(new ProbeResult(new ProbeKey("P06", state.RouteId), ProbeStatus.Pass));
    }

    /// <summary>
    /// MA-ID-1: P06's required-observable gate, public so a unit test locks the
    /// fail direction (a task whose typed surface exposed no resource usage →
    /// typed absent-capability detail; RunP06 consumes this same method).
    /// </summary>
    public static string? AbsentResourceUsageCapability(VerificationRun run) =>
        run.Tasks.Count > 0 && run.Tasks.Any(t => !t.SolverResourceUsageObserved)
            ? "typed surface did not expose solver resource usage for every task (solver_resource_usage_observed=false) — required observable absent, INV-013 absent-capability variant (INV-004/RS-004/MA-ID-1)"
            : null;

    private static void RunP07(RunState state, string fixture, string fixtureKey, Dictionary<string, string?> options)
    {
        var run = state.Adapter.Verify(new FixtureInput(fixture, options));
        RecordRun(state, fixtureKey, run);

        var sidecar = ReadSidecar(state, "bad.sidecar.json");
        var plantedLine = sidecar.RootElement.GetProperty("planted_assertion").GetProperty("line").GetInt32();

        if (run.Diagnostics.Any(d => d.Stage != VerificationStage.Verification))
        {
            state.Results.Add(new ProbeResult(new ProbeKey("P07", state.RouteId), ProbeStatus.Fail,
                "frontend error misread risk: refutation must come from stage=verification (INV-005)"));
            return;
        }
        // RS-018c: the typed outcome must DISTINGUISH completed refutation from
        // timeout/undetermined — anything but Invalid fails P07.
        var refuted = run.Tasks.Any(t => t.Outcome == SolverOutcome.Invalid);
        var nonCompletion = run.Tasks.Any(t => t.Outcome is SolverOutcome.TimedOut or SolverOutcome.Undetermined or SolverOutcome.OutOfResource);
        var verifierDiag = run.Diagnostics.FirstOrDefault(d => d.Stage == VerificationStage.Verification);
        if (!refuted || nonCompletion || verifierDiag is null)
        {
            state.Results.Add(new ProbeResult(new ProbeKey("P07", state.RouteId), ProbeStatus.Fail,
                $"no completed solver refutation (refuted={refuted}, non-completion={nonCompletion}, diagnostic={verifierDiag is not null}) (INV-005/RS-018c)"));
            return;
        }
        var endLine = verifierDiag.EndLine ?? verifierDiag.Line;
        state.NodeTable.Add(new Dictionary<string, object?>
        {
            ["stage"] = "verification",
            ["outcome"] = "invalid",
            ["file"] = verifierDiag.File,
            ["range_start_line"] = verifierDiag.Line,
            ["range_end_line"] = endLine,
            ["message"] = verifierDiag.Message,
        });
        var containsPlanted = verifierDiag.Line <= plantedLine && plantedLine <= endLine;
        if (!containsPlanted || string.IsNullOrWhiteSpace(verifierDiag.Message))
        {
            state.Results.Add(new ProbeResult(new ProbeKey("P07", state.RouteId), ProbeStatus.Fail,
                $"reported range [{verifierDiag.Line},{endLine}] does not contain planted line {plantedLine}, or message empty (INV-005)"));
            return;
        }
        state.Results.Add(new ProbeResult(new ProbeKey("P07", state.RouteId), ProbeStatus.Pass));
    }

    private static void RunFrontend(RunState state, string probeId, string fixture, string fixtureKey, Dictionary<string, string?> options, VerificationStage expectedStage, string probeTag)
    {
        JoinPendingSentinelLeg(state);
        var ledgerPathBefore = Path.Combine(state.RunRoot, RunLayout.SentinelLedgerRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var (_, before, _, malformedBefore) = ReadLedger(ledgerPathBefore, probeTag);
        var run = state.Adapter.Verify(new FixtureInput(fixture, options));
        RecordRun(state, fixtureKey, run);
        state.TargetSets[fixtureKey] = run.TargetNames.ToList();

        var diag = run.Diagnostics.FirstOrDefault(d => d.Stage == expectedStage);
        if (diag is not null)
        {
            state.NodeTable.Add(new Dictionary<string, object?>
            {
                ["stage"] = expectedStage == VerificationStage.Parse ? "parse" : "resolution",
                ["file"] = diag.File,
                ["line"] = diag.Line,
                ["message"] = diag.Message,
            });
        }

        var ledgerPath = Path.Combine(state.RunRoot, RunLayout.SentinelLedgerRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var (_, after, _, malformedAfter) = ReadLedger(ledgerPath, probeTag);
        var (_, allNonceAfter, _, _) = ReadLedger(ledgerPath, null);
        state.LedgerEntriesAfter = allNonceAfter;
        var delta = after - before;
        // MA-RB-3/MA-HI-2 declared contract: a malformed entry appearing in
        // this window can be a mangled RECORD of a genuine invocation — the
        // zero-invocation assertion must fail loudly, never skip it silently.
        var malformedDelta = malformedAfter - malformedBefore;

        var pass = diag is not null
                   && !string.IsNullOrWhiteSpace(diag.Message)
                   && run.TargetNames.Count == 0
                   && run.Tasks.Count == 0
                   && delta == 0
                   && malformedDelta == 0;
        state.Results.Add(new ProbeResult(new ProbeKey(probeId, state.RouteId),
            pass ? ProbeStatus.Pass : ProbeStatus.Fail,
            pass ? null : $"stage-typed diagnostic present={diag is not null}, targets={run.TargetNames.Count}, tasks={run.Tasks.Count}, sentinel-invocations={delta}, malformed-ledger-entries={malformedDelta} (INV-011)"));
    }

    private static void RunP05(RunState state, string fixture, string fixtureKey, Dictionary<string, string?> options, string probeTag)
    {
        JoinPendingSentinelLeg(state);
        var ledgerPathBefore = Path.Combine(state.RunRoot, RunLayout.SentinelLedgerRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var (_, beforeThisProbe, _, malformedBefore) = ReadLedger(ledgerPathBefore, probeTag);
        // The recording sentinel is not a real solver: the prover pipe dying
        // mid-protocol is an EXPECTED consequence of the probe design, and on
        // some orderings Boogie's completion events never settle after the
        // stub exits. The observable is the nonce-bound ledger FILE, not the
        // verification outcome (RS-004b), so the verification leg is bounded.
        try
        {
            var verifyLeg = Task.Run(() => state.Adapter.Verify(new FixtureInput(fixture, options)));
            if (verifyLeg.Wait(TimeSpan.FromSeconds(45)))
            {
                RecordRun(state, fixtureKey, verifyLeg.Result);
            }
            else
            {
                // QA-011: track the abandoned leg so later sentinel probes
                // join it (bounded) before opening their own ledger window;
                // per-probe sub-nonces make any late entries harmless anyway.
                state.PendingSentinelLeg = verifyLeg;
                Console.Error.WriteLine("P05: sentinel verification leg did not settle within the bound (expected for a recording stub); proceeding to the ledger observable (RS-004b)");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"P05: verification against the sentinel ended with {ex.GetType().Name} (expected for a recording stub): {ex.Message}");
        }

        var ledgerPath = Path.Combine(state.RunRoot, RunLayout.SentinelLedgerRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var (_, after, foreign, malformedAfter) = ReadLedger(ledgerPath, probeTag);
        var (_, allNonceEntries, _, _) = ReadLedger(ledgerPath, null);
        state.LedgerEntriesAfter = allNonceEntries;
        var delta = after - beforeThisProbe;
        var malformedDelta = malformedAfter - malformedBefore;
        // RS-003d: only entries under THIS run's nonce count; a stale recording
        // from a prior run can never satisfy this run. MA-RB-3/MA-HI-2: a
        // malformed entry in the window fails loudly — it is never evidence.
        var pass = delta >= 1 && malformedDelta == 0;
        state.Results.Add(new ProbeResult(new ProbeKey("P05", state.RouteId),
            pass ? ProbeStatus.Pass : ProbeStatus.Fail,
            pass ? null : $"sentinel ledger shows {delta} fresh-nonce invocations (foreign-nonce entries: {foreign}, malformed entries in window: {malformedDelta}) — the solver-path option did not select the executed binary (INV-003)"));
    }

    /// <summary>QA-011: bounded join of an abandoned sentinel verification leg before a new ledger window opens.</summary>
    private static void JoinPendingSentinelLeg(RunState state)
    {
        var pending = state.PendingSentinelLeg;
        if (pending is null || pending.IsCompleted)
        {
            state.PendingSentinelLeg = null;
            return;
        }
        try
        {
            if (!pending.Wait(TimeSpan.FromSeconds(10)))
            {
                Console.Error.WriteLine("sentinel: a prior sentinel leg is still unsettled after the join bound; continuing — per-probe sub-nonces keep its late entries out of this probe's window (QA-011)");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"sentinel: prior leg settled with {ex.GetType().Name} (expected for a recording stub)");
        }
        state.PendingSentinelLeg = null;
    }

    private static void RunP10(RunState state, string fixture, string fixtureKey, Dictionary<string, string?> options)
    {
        var table = state.Adapter.RecoverAst(new FixtureInput(fixture, options));
        var declarations = table.Declarations.Select(d => new Dictionary<string, object?>
        {
            ["name"] = d.Name,
            ["kind"] = d.Kind,
            ["ghost"] = d.Ghost,
            ["inferred_ghost"] = d.InferredGhost,
            ["explicit_ghost_keyword"] = d.ExplicitGhostKeyword,
        }).ToList();
        var nodes = table.Nodes.Select(n => new Dictionary<string, object?>
        {
            ["form"] = n.Form,
            ["classification"] = n.Classification,
            ["count"] = n.Count,
        }).ToList();
        state.NodeTable.Add(new Dictionary<string, object?>
        {
            ["declarations"] = declarations,
            ["nodes"] = nodes,
        });

        var isCanonical = fixtureKey == "fixtures/classify.dfy";
        if (!isCanonical)
        {
            state.Results.Add(new ProbeResult(new ProbeKey("P10", state.RouteId), ProbeStatus.Pass,
                "non-canonical fixture: recovery succeeded; sidecar equality applies to fixtures/classify.dfy only"));
            return;
        }
        var sidecar = ReadSidecar(state, "classify.sidecar.json");
        var expectedDecls = sidecar.RootElement.GetProperty("declarations").EnumerateArray()
            .Select(d => (d.GetProperty("name").GetString()!, d.GetProperty("kind").GetString()!,
                d.GetProperty("ghost").GetBoolean(), d.GetProperty("inferred_ghost").GetBoolean(),
                d.GetProperty("explicit_ghost_keyword").GetBoolean()))
            .OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();
        var actualDecls = table.Declarations
            .Select(d => (d.Name, d.Kind, d.Ghost, d.InferredGhost, d.ExplicitGhostKeyword))
            .OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();
        var expectedForms = sidecar.RootElement.GetProperty("nodes").EnumerateArray()
            .Select(n => (n.GetProperty("form").GetString()!, n.GetProperty("classification").GetString()!, n.GetProperty("count").GetInt32()))
            .OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();
        var actualForms = table.Nodes.Select(n => (n.Form, n.Classification, n.Count))
            .OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();
        var equal = expectedDecls.SequenceEqual(actualDecls) && expectedForms.SequenceEqual(actualForms);
        state.Results.Add(new ProbeResult(new ProbeKey("P10", state.RouteId),
            equal ? ProbeStatus.Pass : ProbeStatus.Fail,
            equal ? null : "resolved-node table does not equal the committed expected-values sidecar (INV-007)"));
    }

    private static void RunP11(RunState state, string fixture, string fixtureKey, Dictionary<string, string?> options)
    {
        var readback = state.Adapter.ReadOptionsPostVerification(new FixtureInput(fixture, options));
        state.OptionReadback = readback.EffectiveOptions
            .ToDictionary(kv => kv.Key, kv => kv.Value is null ? null : CanonicalizeValue(state, kv.Value), StringComparer.Ordinal);
        var requested = state.SolverOverride is null
            ? SolverLayout.RunRootToken + "/" + SolverLayout.SolverRelativePath
            : CanonicalizeValue(state, readback.Canary.RequestedValue);
        state.Canary = new Dictionary<string, object?>
        {
            ["option_id"] = readback.Canary.OptionId,
            ["requested_value"] = requested,
            ["effective_prover_path_entry"] = CanonicalizeValue(state, readback.Canary.EffectiveProverPathEntry),
            ["solver_identifier"] = readback.Canary.SolverIdentifier,
            ["solver_version"] = readback.Canary.SolverVersion,
            ["added_prover_options"] = readback.Canary.AddedProverOptions.ToList(),
        };

        // Pass predicate: the readback matches the frozen Option Manifest's
        // per-route canary row (the FILE is the oracle — RS-018b).
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(state.SpikeRoot, "manifest", "option-manifest.json")));
        var row = manifest.RootElement.GetProperty("normalization_canary").GetProperty("routes").GetProperty(state.RouteId);
        var expectedVersion = row.GetProperty("solver_version").GetString();
        var expectedId = row.GetProperty("solver_identifier").GetString();
        var expectedPrefix = row.GetProperty("prover_path_entry_prefix").GetString()!;
        var expectedAdds = row.GetProperty("added_prover_options_include").EnumerateArray().Select(o => o.GetString()!).ToList();

        var effectiveEntry = (string?)state.Canary["effective_prover_path_entry"] ?? "";
        var adds = readback.Canary.AddedProverOptions;
        var ok = readback.Canary.SolverIdentifier == expectedId
                 && readback.Canary.SolverVersion == expectedVersion
                 && effectiveEntry.StartsWith(expectedPrefix, StringComparison.Ordinal)
                 && expectedAdds.All(adds.Contains);
        state.Results.Add(new ProbeResult(new ProbeKey("P11", state.RouteId),
            ok ? ProbeStatus.Pass : ProbeStatus.Fail,
            ok ? null : $"post-verification readback diverges from the frozen Option Manifest canary row (observed id={readback.Canary.SolverIdentifier}, version={readback.Canary.SolverVersion}) (INV-007/OQ-003)"));
    }

    private static void RunP12(RunState state, string fixture, string fixtureKey, Dictionary<string, string?> options)
    {
        var sidecar = ReadSidecar(state, "incl.sidecar.json");
        var expectedClosure = sidecar.RootElement.GetProperty("expected_closure_repo_relative")
            .EnumerateArray().Select(c => c.GetString()!).OrderBy(c => c, StringComparer.Ordinal).ToList();

        var values = state.VerifyIncludedFiles is null
            ? new[] { false, true }
            : new[] { state.VerifyIncludedFiles == "true" };

        var allOk = true;
        var details = new List<string>();
        foreach (var value in values)
        {
            var closure = state.Adapter.RecoverClosure(new FixtureInput(fixture, options), value);
            var closureSorted = closure.ClosureRepoRelative.OrderBy(c => c, StringComparer.Ordinal).ToList();
            var closureKey = values.Length == 1 || !value ? fixtureKey : fixtureKey + "@verify-included-files=true";
            state.ClosureSets[closureKey] = closureSorted;
            state.TargetSets[closureKey] = closure.VerificationTargets.ToList();

            var expectedKey = value ? "verify_included_files_true" : "verify_included_files_false";
            var expectedTargets = sidecar.RootElement.GetProperty("per_option_value").GetProperty(expectedKey)
                .GetProperty("expected_verification_targets").EnumerateArray().Select(t => t.GetString()!)
                .OrderBy(t => t, StringComparer.Ordinal).ToList();
            var actualTargets = closure.VerificationTargets.OrderBy(t => t, StringComparer.Ordinal).ToList();
            if (!expectedClosure.SequenceEqual(closureSorted))
            {
                allOk = false;
                details.Add($"closure under value={value} is [{string.Join(",", closureSorted)}], expected [{string.Join(",", expectedClosure)}]");
            }
            if (!expectedTargets.SequenceEqual(actualTargets))
            {
                allOk = false;
                details.Add($"target set under value={value} is [{string.Join(",", actualTargets)}], expected [{string.Join(",", expectedTargets)}]");
            }
        }
        state.Results.Add(new ProbeResult(new ProbeKey("P12", state.RouteId),
            allOk ? ProbeStatus.Pass : ProbeStatus.Fail,
            allOk ? null : string.Join("; ", details) + " (INV-012)"));
    }

    private static void RunClosureSingle(RunState state, string fixture, string fixtureKey, Dictionary<string, string?> options)
    {
        var closure = state.Adapter.RecoverClosure(new FixtureInput(fixture, options), verifyIncludedFiles: false);
        var sorted = closure.ClosureRepoRelative.OrderBy(c => c, StringComparer.Ordinal).ToList();
        state.ClosureSets[fixtureKey] = sorted;
        state.TargetSets[fixtureKey] = closure.VerificationTargets.ToList();
        var ok = sorted.Count == 1;
        state.Results.Add(new ProbeResult(new ProbeKey("closure-single", state.RouteId),
            ok ? ProbeStatus.Pass : ProbeStatus.Fail,
            ok ? null : $"single-file fixture reported a {sorted.Count}-element closure (INV-012)"));
    }

    private static void RunHygiene(RunState state, string fixture, string fixtureKey, Dictionary<string, string?> options)
    {
        var report = state.Adapter.WalkFixtureHygiene(new FixtureInput(fixture, options));
        state.NodeTable.Add(new Dictionary<string, object?>
        {
            ["all_declarations_bodied"] = report.AllDeclarationsBodied,
            ["contains_attributes"] = report.ContainsAttributes,
            ["contains_assume_statements"] = report.ContainsAssumeStatements,
            ["contains_verification_suppression"] = report.ContainsVerificationSuppression,
        });
        // For the canonical provable fixture the walk must prove purity
        // (INV-004); for other fixtures the walk simply reports what it found —
        // the negative control's impurity is asserted test-side (TA-B6).
        var pass = fixtureKey != "fixtures/ok.dfy"
                   || (report.AllDeclarationsBodied && !report.ContainsAttributes
                       && !report.ContainsAssumeStatements && !report.ContainsVerificationSuppression);
        state.Results.Add(new ProbeResult(new ProbeKey("hygiene", state.RouteId),
            pass ? ProbeStatus.Pass : ProbeStatus.Fail,
            pass ? null : "fixtures/ok.dfy failed the AST-based structural-purity walk (INV-004/RS-009)"));
    }

    private static void RunAnchorStdlib(RunState state, string fixture, string fixtureKey, Dictionary<string, string?> options)
    {
        VerificationRun run;
        try
        {
            run = state.Adapter.Verify(new FixtureInput(fixture, options));
        }
        catch (Exception ex) when ((state.StandardLibraries ?? "false") == "true")
        {
            // The named consumption is dllresource://DafnyPipeline/… — the CLR
            // reports the load failure with a case-normalized simple name, so
            // the anchor context names the consumption explicitly (OQ-004).
            throw new InvalidOperationException(
                $"DafnyPipeline standard-libraries consumption failed on the verification path (OQ-004 anchor): {ex.GetType().FullName}: {ex.Message}", ex);
        }
        RecordRun(state, fixtureKey, run);
        var stdlibOn = (state.StandardLibraries ?? "false") == "true";
        if (!stdlibOn)
        {
            // Differential leg: without the standard libraries the anchor
            // fixture MUST fail at stage=resolution (OQ-004/codex R2-11).
            var resolutionFailure = run.CompletedThroughStage == VerificationStage.Resolution
                                    && run.Diagnostics.Any(d => d.Stage == VerificationStage.Resolution);
            state.NodeTable.Add(new Dictionary<string, object?>
            {
                ["stage"] = "resolution",
                ["anchor_differential"] = "standard-libraries=false",
                ["message"] = run.Diagnostics.FirstOrDefault()?.Message ?? "",
            });
            state.Results.Add(new ProbeResult(new ProbeKey("anchor-stdlib", state.RouteId), ProbeStatus.Fail,
                resolutionFailure
                    ? "differential leg: resolution failed without standard libraries, as the anchor contract requires (recorded as a failing probe by design)"
                    : "differential leg did not fail at stage=resolution — the DafnyPipeline consumption is not proven to matter (OQ-004)"));
            return;
        }
        var ok = run.Tasks.Count > 0 && run.Tasks.All(t => t.Outcome == SolverOutcome.Valid)
                 && !run.Diagnostics.Any(d => d.Stage != VerificationStage.Verification);
        state.Results.Add(new ProbeResult(new ProbeKey("anchor-stdlib", state.RouteId),
            ok ? ProbeStatus.Pass : ProbeStatus.Fail,
            ok ? null : $"stdlib-anchor verification did not complete cleanly (tasks={run.Tasks.Count}) — the DafnyPipeline consumption path failed (OQ-004)"));
    }

    private static void RunP03(RunState state, string fixture, string fixtureKey, Dictionary<string, string?> options)
    {
        // Canonical exercise: a real verification anchors the loaded set.
        var run = state.Adapter.Verify(new FixtureInput(fixture, options));
        RecordRun(state, fixtureKey, run);
        if (run.Tasks.Count == 0 || run.Tasks.Any(t => t.Outcome != SolverOutcome.Valid))
        {
            state.Results.Add(new ProbeResult(new ProbeKey("P03", state.RouteId), ProbeStatus.Fail,
                "canonical verification exercise did not complete with valid outcomes — the loaded set is not anchored (INV-002)"));
            return;
        }
        // Route anchor exercise (OQ-004): Route B additionally verifies the
        // stdlib anchor so DafnyPipeline is loaded via the NAMED consumption
        // (dllresource://DafnyPipeline/DafnyStandardLibraries.doo), never a
        // bare Assembly.Load.
        if (state.RouteId == "B")
        {
            var anchorOptions = new Dictionary<string, string?>(options) { ["--standard-libraries"] = "true" };
            VerificationRun anchorRun;
            try
            {
                anchorRun = state.Adapter.Verify(new FixtureInput("fixtures/stdlib-anchor.dfy", anchorOptions));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"DafnyPipeline standard-libraries consumption failed on the verification path (OQ-004 anchor): {ex.GetType().FullName}: {ex.Message}", ex);
            }
            if (anchorRun.Tasks.Count == 0 || anchorRun.Tasks.Any(t => t.Outcome != SolverOutcome.Valid))
            {
                state.Results.Add(new ProbeResult(new ProbeKey("P03", state.RouteId), ProbeStatus.Fail,
                    "Route B DafnyPipeline anchor exercise failed — the mandatory P03 anchor cannot be claimed (OQ-004)"));
                return;
            }
        }

        var expectedPath = Path.Combine(state.SpikeRoot, "manifest", "expected-loaded",
            state.RouteId == "A" ? "route-a.json" : "route-b.json");
        var expected = LoadExpectedSet(expectedPath);
        RequireDeclaredUniversePredicate(expected, expectedPath);
        var evidence = CaptureRuntimeIdentity(state);
        // MA-VI-3: the unfiltered family sweep gates FIRST, independent of the
        // declared equality universe — an out-of-universe Dafny*/Boogie* load
        // can never pass vacuously through the filtered set equality.
        if (state.FamilySweepViolations.Count > 0)
        {
            state.Results.Add(new ProbeResult(new ProbeKey("P03", state.RouteId), ProbeStatus.Fail,
                "out-of-universe family-named assembly detected by the unfiltered capture sweep: "
                + string.Join("; ", state.FamilySweepViolations)));
            return;
        }
        var status = P03Evaluator.Evaluate(evidence, expected);

        // deps.json → spike-local package-asset trace with hash comparison
        // (INV-002): a locally substituted binary fails even with a matching
        // version string.
        string? traceFailure = status == ProbeStatus.Pass ? TraceThroughDepsJson(state) : null;
        if (traceFailure is not null)
        {
            status = ProbeStatus.Fail;
        }
        state.Results.Add(new ProbeResult(new ProbeKey("P03", state.RouteId), status,
            status == ProbeStatus.Pass ? null : traceFailure ?? "loaded-assembly/runtime identity did not satisfy the P03 pass predicate (INV-002/codex R4-03)"));
    }

    // ------------------------------------------------------- identity capture

    /// <summary>
    /// QA-004: the trust-relevant universe is the codex-F8 definition — EVERY
    /// non-framework assembly selected through the route's .deps.json (package
    /// runtime assets; project-built assemblies rebuild every run and are
    /// excluded). The predicate is declared machine-readably in the committed
    /// expected-loaded oracle and consumed by BOTH the harness and the test;
    /// narrowing it is a reviewable oracle diff, never a code edit.
    /// </summary>
    private static Dictionary<string, (string PackageId, string Version, string AssetPath)> PackageRuntimeAssets()
    {
        var entry = Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("no entry assembly — cannot locate .deps.json (INV-002)");
        var depsPath = Path.ChangeExtension(entry, ".deps.json");
        using var deps = JsonDocument.Parse(File.ReadAllText(depsPath));
        var packageLibraries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var library in deps.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.TryGetProperty("type", out var type) && type.GetString() == "package")
            {
                packageLibraries.Add(library.Name);
            }
        }
        var map = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal);
        var targets = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value;
        foreach (var library in targets.EnumerateObject())
        {
            if (!packageLibraries.Contains(library.Name)
                || !library.Value.TryGetProperty("runtime", out var runtime))
            {
                continue;
            }
            var parts = library.Name.Split('/');
            foreach (var asset in runtime.EnumerateObject())
            {
                if (asset.Name.EndsWith(".dll", StringComparison.Ordinal))
                {
                    map[Path.GetFileNameWithoutExtension(asset.Name)] = (parts[0], parts[1], asset.Name);
                }
            }
        }
        return map;
    }

    private static void RequireDeclaredUniversePredicate(ExpectedLoadedSet expected, string oraclePath)
    {
        // Fail closed if the oracle's declared predicate is not the one this
        // harness implements (QA-004 class fix: predicate lives in the oracle).
        if (expected.Universe != "deps-json-package-runtime-assets")
        {
            throw new InvalidOperationException(
                $"expected-loaded oracle {oraclePath} declares universe predicate '{expected.Universe}', but this harness implements 'deps-json-package-runtime-assets' (QA-004)");
        }
    }

    private static P03Evidence CaptureRuntimeIdentity(RunState state)
    {
        var packageAssets = PackageRuntimeAssets();

        // MA-VI-3: SECOND, NON-FILTERED sweep — INV-002's "any loaded
        // Dafny/Boogie-family assembly outside that mapping fails P03" runs on
        // the UNFILTERED observation source, independent of the declared
        // equality universe. A family-named assembly with an empty location
        // (byte-array load), a name outside the deps.json asset map, or a file
        // location outside the sanctioned roots (harness output tree /
        // spike-local packages folder) is a typed P03 failure.
        var sanctionedRoots = new List<string> { Path.GetFullPath(AppContext.BaseDirectory) };
        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(packagesRoot) && Directory.Exists(packagesRoot))
        {
            sanctionedRoots.Add(Path.GetFullPath(packagesRoot));
        }
        bool UnderSanctionedRoot(string fullPath) => sanctionedRoots.Any(root =>
            fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        state.FamilySweepViolations = new List<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }
            var sweepName = assembly.GetName().Name ?? "";
            var isFamily = sweepName.StartsWith("Dafny", StringComparison.OrdinalIgnoreCase)
                           || sweepName.StartsWith("Boogie", StringComparison.OrdinalIgnoreCase);
            if (!isFamily)
            {
                continue;
            }
            var sweepLocation = assembly.Location;
            if (string.IsNullOrEmpty(sweepLocation))
            {
                state.FamilySweepViolations.Add(
                    $"{sweepName}: family-named assembly loaded with an EMPTY location (byte-array/dynamic-context load) — outside the deps.json mapping (INV-002/MA-VI-3)");
                continue;
            }
            var sweepFull = Path.GetFullPath(sweepLocation);
            if (!packageAssets.ContainsKey(sweepName) || !UnderSanctionedRoot(sweepFull))
            {
                state.FamilySweepViolations.Add(
                    $"{sweepName}: family-named assembly loaded from '{CanonicalizeValue(state, sweepFull)}' — outside the deps.json package-asset mapping (INV-002/MA-VI-3)");
            }
        }

        var loaded = new List<LoadedAssemblyIdentity>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name ?? "";
            if (assembly.IsDynamic || !packageAssets.ContainsKey(name))
            {
                continue;
            }
            var location = assembly.Location;
            if (string.IsNullOrEmpty(location))
            {
                continue;
            }
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var mapped = informational?.Split('+')[0]; // strip +<sha> build metadata (RS-008)
            loaded.Add(new LoadedAssemblyIdentity(name, mapped, location, Sha256File(location)));
        }
        state.LoadedAssemblies = loaded
            .Select(a => (a.SimpleName, a.FilePath, a.FileSha256, a.MappedPackageVersion))
            .OrderBy(a => a.SimpleName, StringComparer.Ordinal)
            .ToList();
        state.HarnessTfm = Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

        var corelib = typeof(object).Assembly.Location;
        var corelibDir = Path.GetDirectoryName(corelib)!;
        var runtimeVersionDir = Path.GetFileName(corelibDir);
        var dotnetRoot = Path.GetFullPath(Path.Combine(corelibDir, "..", "..", ".."));
        var hostfxr = Path.Combine(dotnetRoot, "host", "fxr", runtimeVersionDir, "libhostfxr.so");
        if (!File.Exists(hostfxr))
        {
            var fxrRoot = Path.Combine(dotnetRoot, "host", "fxr");
            hostfxr = Directory.Exists(fxrRoot)
                ? Directory.EnumerateFiles(fxrRoot, "libhostfxr.so", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).LastOrDefault() ?? hostfxr
                : hostfxr;
        }

        var (frameworkName, rollForward) = ReadRuntimeConfig();
        var hostfxrSha = File.Exists(hostfxr) ? Sha256File(hostfxr) : "";
        var corelibSha = Sha256File(corelib);
        // MA-ED-2: the identities the P03 predicate consumes are PERSISTED into
        // the route report's binding identity (hostfxr_identity_concrete /
        // corelib_identity_concrete) — computed-then-discarded identity is
        // unauditable (codex R4-03).
        if (hostfxrSha.Length == 64)
        {
            state.HostfxrIdentity = (hostfxr, hostfxrSha);
        }
        state.CorelibIdentity = (corelib, corelibSha);
        return new P03Evidence(
            state.RouteId,
            state.HarnessTfm ?? "",
            Environment.Version.Major,
            frameworkName,
            rollForward,
            hostfxr,
            hostfxrSha,
            corelib,
            corelibSha,
            loaded.OrderBy(a => a.SimpleName, StringComparer.Ordinal).ToList());
    }

    private static (string FrameworkName, string RollForward) ReadRuntimeConfig()
    {
        var entry = Assembly.GetEntryAssembly()?.Location;
        if (entry is null)
        {
            return ("", "");
        }
        var configPath = Path.ChangeExtension(entry, ".runtimeconfig.json");
        if (!File.Exists(configPath))
        {
            return ("", "");
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        var runtimeOptions = doc.RootElement.GetProperty("runtimeOptions");
        var rollForward = runtimeOptions.TryGetProperty("rollForward", out var rf) ? rf.GetString() ?? "" : "";
        if (runtimeOptions.TryGetProperty("framework", out var fw))
        {
            return (fw.GetProperty("name").GetString() ?? "", rollForward);
        }
        if (runtimeOptions.TryGetProperty("frameworks", out var fws) && fws.GetArrayLength() == 1)
        {
            return (fws[0].GetProperty("name").GetString() ?? "", rollForward);
        }
        return ("", rollForward);
    }

    private static ExpectedLoadedSet LoadExpectedSet(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var assemblies = root.GetProperty("assemblies").EnumerateArray()
            .Select(a => new LoadedAssemblyIdentity(
                a.GetProperty("simple_name").GetString()!,
                a.TryGetProperty("package_version", out var pv) ? pv.GetString() : null,
                "",
                a.GetProperty("sha256").ValueKind == JsonValueKind.String ? a.GetProperty("sha256").GetString()! : ""))
            .ToList();
        return new ExpectedLoadedSet(
            root.GetProperty("route").GetString()!,
            root.TryGetProperty("universe_predicate", out var predicate)
                ? predicate.GetProperty("id").GetString() ?? ""
                : root.GetProperty("universe").GetString() ?? "",
            root.GetProperty("anchors").EnumerateArray().Select(a => a.GetString()!).ToList(),
            assemblies);
    }

    private static string? TraceThroughDepsJson(RunState state)
    {
        var entry = Assembly.GetEntryAssembly()?.Location;
        var depsPath = entry is null ? null : Path.ChangeExtension(entry, ".deps.json");
        if (depsPath is null || !File.Exists(depsPath))
        {
            return "deps.json missing beside the harness — the loaded set cannot be traced (INV-002)";
        }
        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrEmpty(packagesRoot) || !Directory.Exists(packagesRoot))
        {
            return "NUGET_PACKAGES (spike-local packages folder) is unavailable — package-asset trace impossible (INV-002/EA-008)";
        }
        using var deps = JsonDocument.Parse(File.ReadAllText(depsPath));
        var targets = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value;

        foreach (var (simpleName, loadedPath, loadedSha, _) in state.LoadedAssemblies)
        {
            string? packageId = null, packageVersion = null, assetRelative = null;
            foreach (var library in targets.EnumerateObject())
            {
                if (!library.Value.TryGetProperty("runtime", out var runtime))
                {
                    continue;
                }
                foreach (var asset in runtime.EnumerateObject())
                {
                    if (Path.GetFileNameWithoutExtension(asset.Name) == simpleName)
                    {
                        var parts = library.Name.Split('/');
                        packageId = parts[0];
                        packageVersion = parts[1];
                        assetRelative = asset.Name;
                        break;
                    }
                }
                if (packageId is not null)
                {
                    break;
                }
            }
            if (packageId is null)
            {
                return $"{simpleName} is not traceable through .deps.json (INV-002)";
            }
            state.ResolvedPackageVersions[packageId] = packageVersion!;
            var packageAsset = Path.Combine(packagesRoot, packageId.ToLowerInvariant(), packageVersion!,
                assetRelative!.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(packageAsset))
            {
                return $"{simpleName}: package asset missing at the spike-local folder ({packageId}/{packageVersion}) (INV-002)";
            }
            if (Sha256File(packageAsset) != loadedSha)
            {
                return $"{simpleName}: loaded file hash differs from the spike-local package asset — locally substituted binary (INV-002)";
            }
            _ = loadedPath;
        }
        return null;
    }

    // ----------------------------------------------------------- environment

    /// <summary>Returns null on success, else the planting failure detail (QA-003: never swallowed).</summary>
    private static string? PlantAlwaysOnDecoys(RunState state)
    {
        // INV-003(b): differently-hashed, recording z3 decoys at the
        // assembly-adjacent fallback path and first-on-PATH are EXPECTED
        // fixtures of every run, whitelisted by the gate; the run then asserts
        // zero decoy invocations. Existing decoys (e.g. test-planted ones) are
        // never overwritten.
        var decoyLog = Path.Combine(state.RunRoot, "sentinel", "decoy-invocations.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(decoyLog)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"cannot create the decoy-log directory: {ex.Message}";
        }
        var script = ExpectedDecoyScript(state.RunRoot);
        state.PlantedDecoyScriptSha = Sha256Text(script);
        var pathDecoy = Path.Combine(state.RunRoot, "decoys", "z3");
        var adjacentDecoy = Path.Combine(AppContext.BaseDirectory,
            SolverLayout.ProhibitedAssemblyAdjacentRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var failure = WriteExecutableIfAbsent(pathDecoy, script)
                      ?? WriteExecutableIfAbsent(adjacentDecoy, script);
        // QA-003: presence is a POST-plant existence check, never an assumption.
        state.DecoysPresent = File.Exists(pathDecoy) && File.Exists(adjacentDecoy);
        return failure ?? (state.DecoysPresent ? null : "decoys absent after planting");
    }

    private static string? WriteExecutableIfAbsent(string path, string content)
    {
        try
        {
            if (File.Exists(path))
            {
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A concurrent harness may have planted it first; only a WINNER
            // leaves the file present — anything else is a real failure.
            return File.Exists(path) ? null : $"decoy planting failed at {path}: {ex.Message}";
        }
    }

    private static string Sha256Text(string text)
    {
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>
    /// QA-012: the deterministic sanctioned-decoy script for a run root. Gate
    /// recognition recomputes this for ANY candidate decoy directory and
    /// compares the candidate's digest — so a sanctioned recording decoy is
    /// recognized by EXACT content (not a spoofable marker), across run roots.
    /// </summary>
    private static string ExpectedDecoyScript(string runRoot)
    {
        var decoyLog = Path.Combine(runRoot, "sentinel", "decoy-invocations.log");
        return "#!/bin/sh\n" +
               $"printf 'decoy-invoked %s\\n' \"$*\" >> '{decoyLog}'\n" +
               "echo 'Z3 version 0.0.0 - 64 bit (decoy)'\n";
    }

    /// <summary>
    /// QA-015: the exact sanctioned recording-decoy script for a run root,
    /// public so tests planting decoys into decoys/-named directories can plant
    /// the digest-matching content (the gate sanctions decoys/-dir candidates
    /// by EXACT digest only — location alone is never sufficient there).
    /// </summary>
    public static string SanctionedDecoyScript(string runRoot) => ExpectedDecoyScript(runRoot);

    private static bool IsSanctionedDecoyByDigest(string candidatePath)
    {
        // QA-015 (closing QA-012's dead-code regression): for a candidate in a
        // decoys/-named directory the ONLY sanctioning predicate is the exact
        // digest — the candidate's content must EXACTLY equal the deterministic
        // decoy script this harness would plant for the candidate's own
        // decoy-dir run root (cross-root safe, unspoofable). A directory merely
        // NAMED "decoys" sanctions nothing: a wrong-digest z3 there fails the
        // gate closed. The assembly-adjacent {output}/z3/bin/z3-4.12.1 fallback
        // location remains location-sanctioned (it sits inside the
        // controller-built output tree; the zero-invocation decoy-log assertion
        // is the behavioral backstop there — recorded residual, QA-015).
        // A z3 anywhere else (e.g. a hostile PATH dir) is NEITHER and fails.
        try
        {
            var full = Path.GetFullPath(candidatePath);
            var dir = Path.GetDirectoryName(full);
            if (dir is null)
            {
                return false;
            }
            if (Path.GetFileName(dir) == "decoys")
            {
                var candidateRunRoot = Path.GetDirectoryName(dir);
                // QA-015: digest predicate ONLY — no location fallthrough.
                return candidateRunRoot is not null
                       && Sha256File(full) == Sha256Text(ExpectedDecoyScript(candidateRunRoot));
            }
            // Assembly-adjacent sanctioned fallback location.
            var adjacent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                SolverLayout.ProhibitedAssemblyAdjacentRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            return full == adjacent;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? StartupGate(RunState state)
    {
        // QA-012 (class fix): the gate's check list is GENERATED from Dafny
        // 4.11.0's source-verified ambient-discovery probe sequence:
        //   (0) explicit PROVER_PATH prover option — single-entry/conflict
        //       check enforced in the seam (EA-005);
        //   (1) assembly-adjacent {DafnyCore dir}/z3/bin/z3-4.12.1
        //       (Source/DafnyCore/DafnyOptions.cs:1206-1217);
        //   (2) the FIRST existing <PATH dir>/z3
        //       (Source/DafnyCore/DafnyOptions.cs:1219-1227).
        // Each mechanism's RESOLVED OUTCOME must be a sanctioned decoy:
        // sanctioned means the exact canonical decoy location, and when this
        // harness planted the file itself its digest must equal the planted
        // script's digest (spoofable content markers are no longer trusted).
        // Additionally EVERY PATH dir is scanned for z3*-named executables;
        // matches unreachable by the cited sequence (masked by an earlier
        // sanctioned decoy, or versioned names Dafny never probes on PATH) are
        // recorded as observations, never silently ignored.
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = pathValue.Split(':', StringSplitOptions.RemoveEmptyEntries);

        // Mechanism (2): the discovery outcome — first existing <dir>/z3 — must
        // be a sanctioned recording decoy, recognized BY EXACT SCRIPT DIGEST
        // (QA-012), which works across run roots (the launcher may construct a
        // decoy-first PATH pointing at a different run root's decoys than the
        // harness's own --run-root).
        string? reachable = null;
        foreach (var dir in dirs)
        {
            var candidate = Path.Combine(dir, "z3");
            if (File.Exists(candidate))
            {
                reachable = Path.GetFullPath(candidate);
                break;
            }
        }
        if (reachable is not null && !IsSanctionedDecoyByDigest(reachable))
        {
            return $"unsanctioned z3-named executable is the ambient PATH discovery outcome: {reachable} (Dafny 4.11.0 DafnyOptions.cs:1219-1227)";
        }

        // Full PATH sweep (QA-012): every dir, every z3*-named executable.
        // Anything that is NOT a sanctioned recording decoy and is NOT the
        // (already-vetted) discovery outcome is recorded; exact-name entries
        // shadowed by the sanctioned first match and versioned names Dafny
        // never probes on PATH are observations, not gate failures.
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }
            IEnumerable<string> matches;
            try
            {
                matches = Directory.EnumerateFiles(dir, "z3*");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var file in matches)
            {
                var full = Path.GetFullPath(file);
                if (full == reachable || IsSanctionedDecoyByDigest(full))
                {
                    continue;
                }
                Console.Error.WriteLine($"startup-gate observation: z3-named executable on PATH but unreachable by Dafny's cited discovery sequence: {full}");
            }
        }

        // Mechanism (1): no z3-named executable under the route output tree
        // except the sanctioned assembly-adjacent decoy location (RS-003a).
        var outputTree = AppContext.BaseDirectory;
        var sanctionedAdjacent = Path.GetFullPath(Path.Combine(outputTree,
            SolverLayout.ProhibitedAssemblyAdjacentRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        foreach (var file in Directory.EnumerateFiles(outputTree, "z3*", SearchOption.AllDirectories))
        {
            if (Path.GetFullPath(file) != sanctionedAdjacent)
            {
                return $"z3-named file inside the route output tree: {file} (Dafny 4.11.0 DafnyOptions.cs:1206-1217)";
            }
        }
        return null;
    }

    private static string WriteSentinelStub(RunState state, string ledgerPath, string nonce, string probeTag)
    {
        // QA-011: every sentinel probe gets its OWN stub carrying a per-probe
        // sub-nonce, so each probe's ledger-delta assertion is scoped to its
        // own window — an abandoned earlier leg (e.g. a timed-out P05 prover)
        // can never leak entries into another probe's count.
        var stubPath = Path.Combine(state.RunRoot, "sentinel", $"sentinel-z3-{probeTag}");
        File.WriteAllText(stubPath, SentinelStubScript(ledgerPath, nonce, probeTag));
        File.SetUnixFileMode(stubPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return stubPath;
    }

    /// <summary>
    /// MA-RB-3/MA-HI-2: the sentinel ledger writer is APPEND-ONLY with
    /// per-writer-unique entry files and byte-safe (base64) field encoding —
    /// never a shared-tmp read-modify-write (whose last-mv-wins race silently
    /// dropped entries, a nondeterministic fail-open of the INV-011
    /// zero-invocation observable) and never tr/sed string-interpolated JSON
    /// (whose &amp;, |, ], and newline handling corrupted the ledger). Each
    /// invocation writes ONE mktemp-named file under sentinel/entries/ holding
    /// a single line of space-separated base64 fields:
    ///   base64(nonce) base64("probe:&lt;tag&gt;") base64(argv1) base64(argv2)…
    /// base64 round-trips arbitrary argv bytes; concurrent writers can never
    /// clobber each other. Public so tests can race two real stub invocations
    /// and prove both entries survive.
    /// </summary>
    public static string SentinelStubScript(string ledgerPath, string nonce, string probeTag)
    {
        var entriesDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ledgerPath))!, "entries");
        var nonceB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(nonce));
        var tagB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("probe:" + probeTag));
        return
            "#!/bin/sh\n" +
            "# Nonce-recording sentinel solver stub (INV-003/RS-003d). Version probes\n" +
            "# (options processing) are answered without recording; every SOLVER\n" +
            "# invocation writes its OWN append-only mktemp-named entry file under\n" +
            "# sentinel/entries/ with base64-encoded fields (MA-RB-3/MA-HI-2):\n" +
            "#   b64(nonce) b64(probe:<tag>) b64(argv...)\n" +
            "if [ \"$1\" = \"-version\" ] || [ \"$1\" = \"--version\" ]; then\n" +
            "  echo 'Z3 version 4.12.1 - 64 bit'\n" +
            "  exit 0\n" +
            "fi\n" +
            $"ENTRIES_DIR='{entriesDir}'\n" +
            "mkdir -p \"$ENTRIES_DIR\" || exit 0\n" +
            // Write to a temp name the reader's `entry-*` glob IGNORES, then
            // atomically rename into place (below): a concurrent reader — or a
            // shim descheduled/killed mid-write under heavy contention — can then
            // NEVER observe a partial `entry-*` file, which would otherwise be
            // counted as a malformed entry and fail P05/P08 (INV-003/RS-003d).
            "ENTRY_TMP=$(mktemp \"$ENTRIES_DIR/.wip-XXXXXXXX\") || exit 0\n" +
            "{\n" +
            $"  printf '%s' '{nonceB64}'\n" +
            $"  printf ' %s' '{tagB64}'\n" +
            "  for a in \"$@\"; do\n" +
            "    printf ' %s' \"$(printf '%s' \"$a\" | base64 | tr -d '\\n')\"\n" +
            "  done\n" +
            "  printf '\\n'\n" +
            "} > \"$ENTRY_TMP\" && mv -f \"$ENTRY_TMP\" \"$ENTRIES_DIR/entry-${ENTRY_TMP##*/.wip-}\" 2>/dev/null || rm -f \"$ENTRY_TMP\" 2>/dev/null\n" +
            "exit 0\n";
    }

    /// <summary>
    /// QA-022(1): the QA-011 per-probe sub-nonce tag filter, exposed as the
    /// public ledger reader so a unit test can prove the filter DISCRIMINATES —
    /// deleting the tag filter makes that test fail (reversion-failing armor).
    /// The production chain (P05/P08/P09 ledger windows) calls this same method.
    /// </summary>
    public static (string Nonce, int EntriesForNonce, int ForeignEntries) ReadSentinelLedger(string ledgerPath, string? probeTag)
    {
        var (nonce, forNonce, foreign, _) = ReadLedger(ledgerPath, probeTag);
        return (nonce, forNonce, foreign);
    }

    /// <summary>MA-RB-3/MA-HI-2: the full reader including the malformed-entry count (declared contract: skip-and-count; the sentinel pass predicates fail loudly on any malformed entry).</summary>
    public static (string Nonce, int EntriesForNonce, int ForeignEntries, int MalformedEntries) ReadSentinelLedgerDetailed(string ledgerPath, string? probeTag)
        => ReadLedger(ledgerPath, probeTag);

    /// <summary>Decodes every well-formed entry (nonce, probe tag, argv) — the round-trip observable for the MA-HI-2 byte-safety tests.</summary>
    public static IReadOnlyList<SentinelLedgerEntry> ReadSentinelEntries(string ledgerPath)
    {
        var entries = new List<SentinelLedgerEntry>();
        foreach (var line in EnumerateEntryLines(ledgerPath))
        {
            if (TryDecodeEntryLine(line, out var entry))
            {
                entries.Add(entry!);
            }
        }
        return entries;
    }

    private static (string Nonce, int EntriesForNonce, int ForeignEntries, int MalformedEntries) ReadLedger(string ledgerPath, string? probeTag)
    {
        // The pre-created ledger FILE (single-writer: controller/test mint)
        // declares the run nonce; entries live as append-only per-writer files
        // under sentinel/entries/ (MA-RB-3). Legacy embedded "entries" arrays
        // (test fixtures) still count — the multi-writer stub no longer writes
        // them. QA-011: with a probeTag, only entries carrying that probe's
        // sub-nonce tag count — every ledger-delta assertion is scoped per
        // probe, never per run. Malformed entry files are counted, never
        // silently dropped and never allowed to zero the whole ledger.
        string nonce;
        var forNonce = 0;
        var foreign = 0;
        var malformed = 0;
        using (var doc = JsonDocument.Parse(File.ReadAllText(ledgerPath)))
        {
            nonce = doc.RootElement.GetProperty("nonce").GetString()!;
            if (doc.RootElement.TryGetProperty("entries", out var embedded) && embedded.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in embedded.EnumerateArray())
                {
                    if (entry.GetProperty("nonce").GetString() != nonce)
                    {
                        foreign++;
                        continue;
                    }
                    if (probeTag is not null)
                    {
                        var argv = entry.GetProperty("argv");
                        var first = argv.GetArrayLength() > 0 ? argv[0].GetString() : null;
                        if (first != $"probe:{probeTag}")
                        {
                            continue;
                        }
                    }
                    forNonce++;
                }
            }
        }
        foreach (var line in EnumerateEntryLines(ledgerPath))
        {
            if (!TryDecodeEntryLine(line, out var entry))
            {
                malformed++;
                continue;
            }
            if (entry!.Nonce != nonce)
            {
                foreign++;
                continue;
            }
            if (probeTag is not null && entry.ProbeTag != $"probe:{probeTag}")
            {
                continue;
            }
            forNonce++;
        }
        return (nonce, forNonce, foreign, malformed);
    }

    private static IEnumerable<string> EnumerateEntryLines(string ledgerPath)
    {
        var entriesDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ledgerPath))!, "entries");
        if (!Directory.Exists(entriesDir))
        {
            yield break;
        }
        foreach (var file in Directory.EnumerateFiles(entriesDir, "entry-*").OrderBy(f => f, StringComparer.Ordinal))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                text = "";
            }
            catch (UnauthorizedAccessException)
            {
                text = "";
            }
            yield return text.TrimEnd('\n');
        }
    }

    private static bool TryDecodeEntryLine(string line, out SentinelLedgerEntry? entry)
    {
        entry = null;
        if (line.Length == 0)
        {
            return false; // truncated/unreadable write — malformed by contract
        }
        var fields = line.Split(' ');
        if (fields.Length < 2)
        {
            return false;
        }
        try
        {
            string Decode(string b64) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var nonce = Decode(fields[0]);
            var tag = Decode(fields[1]);
            var argv = fields.Skip(2).Select(Decode).ToList();
            entry = new SentinelLedgerEntry(nonce, tag, argv);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // -------------------------------------------------------------- evidence

    private static void RecordRun(RunState state, string fixtureKey, VerificationRun run)
    {
        state.TargetSets[fixtureKey] = run.TargetNames.ToList();
        state.OutcomeEnums[fixtureKey] = run.Tasks
            .OrderBy(t => t.TargetName, StringComparer.Ordinal)
            .Select(t => new Dictionary<string, object?>
            {
                ["target"] = t.TargetName,
                ["outcome"] = t.Outcome.ToString().ToLowerInvariant(),
            })
            .ToList();
        state.ResourceUsage[fixtureKey] = run.Tasks.Count > 0 && run.Tasks.All(t => t.SolverResourceUsageObserved);
        state.OptionReadback["--solver-path"] = CanonicalizeValue(state, run.EffectiveSolverPath);
        foreach (var diag in run.Diagnostics.Where(d => d.Stage == VerificationStage.Verification))
        {
            _ = diag; // verifier diagnostics are surfaced per probe (P07)
        }
    }

    private static void RecordFixtureDigest(RunState state, string fixture, string fixtureKey)
    {
        var path = Path.IsPathRooted(fixture) ? fixture : Path.Combine(Directory.GetCurrentDirectory(), fixture);
        try
        {
            if (File.Exists(path))
            {
                state.FixtureDigests[fixtureKey] = Sha256File(path);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Unreadable fixture: no digest; the probe itself fails closed (BND-003).
        }
        catch (IOException)
        {
        }
    }

    private static JsonDocument ReadSidecar(RunState state, string name)
    {
        var path = Path.Combine(state.SpikeRoot, "fixtures", "expected", name);
        state.SidecarDigests[$"fixtures/expected/{name}"] = Sha256File(path);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// QA-002: the executed-solver identity is recomputed AT EMISSION from the
    /// file at the option-manifest solver path (the post-run readback value),
    /// and the binding path records that same file — never a stand-in asset.
    /// </summary>
    private static void RefreshExecutedSolverIdentity(RunState state)
    {
        var readback = state.OptionReadback.TryGetValue("--solver-path", out var v) ? v : null;
        string? concrete = null;
        if (!string.IsNullOrEmpty(readback))
        {
            var expanded = readback.Replace(SolverLayout.RunRootToken, Path.GetFullPath(state.RunRoot), StringComparison.Ordinal);
            concrete = Path.IsPathRooted(expanded) ? expanded : Path.Combine(state.SpikeRoot, expanded);
        }
        if (concrete is not null && File.Exists(concrete))
        {
            state.ExecutedSolverSha = Sha256File(concrete);
            state.SolverConcreteRel = Path.GetRelativePath(state.RunRoot, concrete).Replace('\\', '/');
        }
        else if (state.SolverConcreteRel is not null)
        {
            var fallback = Path.GetFullPath(Path.Combine(state.RunRoot, state.SolverConcreteRel));
            state.ExecutedSolverSha = File.Exists(fallback) ? Sha256File(fallback) : null;
        }
    }

    /// <summary>
    /// MA-ED-2: path-bearing generated files (the harness's own .deps.json)
    /// contribute a NORMALIZED semantic digest to class 2 (CRLF→LF, run-root
    /// absolute path → &lt;run-root&gt; token) with the raw digest in the
    /// binding class (codex R3-6 field pair, previously declared but never
    /// produced).
    /// </summary>
    private static void RecordDepsJsonDigests(RunState state)
    {
        var entry = Assembly.GetEntryAssembly()?.Location;
        var depsPath = entry is null ? null : Path.ChangeExtension(entry, ".deps.json");
        if (depsPath is null || !File.Exists(depsPath) || state.RunRoot.Length == 0)
        {
            return;
        }
        var name = Path.GetFileName(depsPath);
        state.RawGeneratedFileDigests[name] = Sha256File(depsPath);
        var text = File.ReadAllText(depsPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        text = text.Replace(Path.GetFullPath(state.RunRoot), SolverLayout.RunRootToken, StringComparison.Ordinal);
        state.NormalizedDepsJsonDigests[name] = Sha256Text(text);
    }

    private static void EmitReport(RunState state, string schemaPath)
    {
        RefreshExecutedSolverIdentity(state);
        RecordDepsJsonDigests(state);
        var spikeRelativeRunDir = Path.GetRelativePath(state.SpikeRoot, state.RunRoot).Replace('\\', '/');
        var (gitCommit, gitDirty) = ReadGitState(state);

        var report = new Dictionary<string, object?>
        {
            ["evidence_schema_version"] = 2,
            ["evidence_schema_sha256"] = Sha256File(schemaPath),
            ["probe_manifest_sha256"] = Sha256File(Path.Combine(state.SpikeRoot, "manifest", "probe-manifest.json")),
            ["run_id"] = state.RunId,
            ["kind"] = "route-report",
            ["binding_identity"] = BuildBinding(state, spikeRelativeRunDir, gitCommit, gitDirty),
            ["deterministic"] = BuildDeterministic(state),
            ["volatile"] = new Dictionary<string, object?>
            {
                ["timestamps"] = new Dictionary<string, object?>
                {
                    ["started_utc"] = state.Started.ToString("o", CultureInfo.InvariantCulture),
                    ["finished_utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                },
                ["durations"] = new Dictionary<string, object?>
                {
                    ["total_ms"] = (DateTime.UtcNow - state.Started).TotalMilliseconds,
                },
                ["raw_solver_resource_counts"] = state.RawResourceCounts.Count == 0
                    ? null
                    : state.RawResourceCounts.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            },
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        // Self-check against the committed closed schema (INV-009): an
        // unemittable report is a harness bug, surfaced loudly.
        EvidenceSchema.ValidateReport(json, schemaPath);

        // Atomic emission: write-temp-then-rename (RS-017f); consumers treat a
        // missing/malformed report as its own state, never as pass.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(state.ReportPath))!);
        var temp = state.ReportPath + ".tmp-" + Environment.ProcessId;
        File.WriteAllText(temp, json);
        File.Move(temp, state.ReportPath, overwrite: true);
    }

    private static Dictionary<string, object?> BuildBinding(RunState state, string runDirectory, string gitCommit, bool gitDirty)
    {
        var envValues = new List<Dictionary<string, object?>>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            var value = (string?)entry.Value ?? "";
            string root, path;
            var runRootFull = Path.GetFullPath(state.RunRoot);
            if (value.StartsWith(runRootFull, StringComparison.Ordinal))
            {
                root = "run-root";
                path = value[runRootFull.Length..].TrimStart('/');
            }
            else if (value.StartsWith("/", StringComparison.Ordinal))
            {
                root = "system";
                path = value.TrimStart('/');
            }
            else
            {
                root = "system";
                path = value;
            }
            envValues.Add(new Dictionary<string, object?> { ["key"] = key, ["root"] = root, ["path"] = path });
        }

        var binding = new Dictionary<string, object?>
        {
            ["run_directory"] = runDirectory,
            ["sentinel_nonce"] = state.SentinelNonce,
            ["git_commit_id"] = gitCommit,
            ["git_dirty_flag"] = gitDirty,
            ["host_rid"] = RuntimeInformation.RuntimeIdentifier,
            ["sdk_version"] = ReadSdkPin(state),
            ["actual_runtime_version"] = Environment.Version.ToString(),
            ["harness_target_framework"] = state.HarnessTfm ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? "",
            ["environment_profile_values"] = envValues.OrderBy(e => (string?)e["key"], StringComparer.Ordinal).ToList(),
            ["solver_path_concrete"] = state.SolverConcreteRel ?? Path.GetRelativePath(state.RunRoot,
                state.SolverOverride ?? Path.Combine(state.RunRoot, SolverLayout.SolverRelativePath.Replace('/', Path.DirectorySeparatorChar))).Replace('\\', '/'),
        };
        // MA-ED-2: previously schema-declared but producer-less binding fields.
        var runProducts = new List<string> { Path.GetFullPath(state.ReportPath) };
        if (state.SentinelLedgerPath is not null)
        {
            runProducts.Add(Path.GetFullPath(state.SentinelLedgerPath));
        }
        var decoyLogConcrete = Path.Combine(state.RunRoot, "sentinel", "decoy-invocations.log");
        if (File.Exists(decoyLogConcrete))
        {
            runProducts.Add(Path.GetFullPath(decoyLogConcrete));
        }
        binding["run_product_paths_concrete"] = runProducts;
        if (state.RawGeneratedFileDigests.Count > 0)
        {
            binding["raw_digests_of_path_bearing_generated_files"] =
                state.RawGeneratedFileDigests.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        }
        if (state.HostfxrIdentity is { } hostfxrIdentity)
        {
            binding["hostfxr_identity_concrete"] = new Dictionary<string, object?>
            {
                ["path"] = hostfxrIdentity.Path,
                ["sha256"] = hostfxrIdentity.Sha,
            };
        }
        if (state.CorelibIdentity is { } corelibIdentity)
        {
            binding["corelib_identity_concrete"] = new Dictionary<string, object?>
            {
                ["path"] = corelibIdentity.Path,
                ["sha256"] = corelibIdentity.Sha,
            };
        }
        if (state.LoadedAssemblies.Count > 0)
        {
            binding["loaded_assembly_file_paths"] = state.LoadedAssemblies
                .Select(a => new Dictionary<string, object?>
                {
                    ["simple_name"] = a.SimpleName,
                    ["path"] = Path.GetRelativePath(state.RunRoot, a.Path).Replace('\\', '/'),
                })
                .ToList();
        }
        return binding;
    }

    private static Dictionary<string, object?> BuildDeterministic(RunState state)
    {
        var det = new Dictionary<string, object?>
        {
            ["per_probe_results"] = state.Results.Select(r => new Dictionary<string, object?>
            {
                ["probe"] = r.Key.ProbeId,
                ["route"] = r.Key.Route,
                ["status"] = r.Status.ToString().ToLowerInvariant(),
                ["detail"] = r.Detail,
            }).ToList(),
            ["final_suite_status"] = "unknown",
            ["node_table"] = state.NodeTable.Count == 0 ? null : state.NodeTable,
            ["option_manifest_readback"] = state.OptionReadback.Count == 0 ? null : state.OptionReadback.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            ["normalization_canary_observations"] = state.Canary,
            ["fixture_digests"] = state.FixtureDigests.Count == 0 ? null : state.FixtureDigests.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            ["sidecar_digests"] = state.SidecarDigests.Count == 0 ? null : state.SidecarDigests.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            ["oracle_file_digests"] = new Dictionary<string, object?>
            {
                ["option_manifest"] = Sha256File(Path.Combine(state.SpikeRoot, "manifest", "option-manifest.json")),
                ["probe_manifest"] = Sha256File(Path.Combine(state.SpikeRoot, "manifest", "probe-manifest.json")),
            },
            ["nuget_config_digest"] = Sha256File(Path.Combine(state.SpikeRoot, "NuGet.Config")),
            // MA-ED-2: normalized semantic digests of path-bearing generated
            // files (class 2; raw digests sit in binding identity).
            ["deps_json_digests_normalized"] = state.NormalizedDepsJsonDigests.Count == 0
                ? null
                : state.NormalizedDepsJsonDigests.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            ["executed_solver_sha256"] = state.ExecutedSolverSha,
            ["solver_archive_sha256"] = state.SolverArchiveSha,
            ["closure_sets"] = state.ClosureSets.Count == 0 ? null : state.ClosureSets.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            ["verification_target_sets"] = state.TargetSets.Count == 0 ? null : state.TargetSets.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            ["solver_outcome_enums"] = state.OutcomeEnums.Count == 0 ? null : state.OutcomeEnums.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            ["solver_resource_usage_observed"] = state.ResourceUsage.Count == 0 ? null : state.ResourceUsage.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            ["adjudication_records"] = state.AdjudicationRecords.Count == 0 ? null : state.AdjudicationRecords,
            ["resolved_package_versions"] = state.ResolvedPackageVersions.Count == 0 ? null : state.ResolvedPackageVersions.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
        };
        if (state.LoadedAssemblies.Count > 0)
        {
            det["loaded_assembly_identities"] = state.LoadedAssemblies.Select(a => new Dictionary<string, object?>
            {
                ["simple_name"] = a.SimpleName,
                ["mapped_package_version"] = a.Version,
                ["file_path"] = CanonicalizeValue(state, a.Path),
                ["file_sha256"] = a.Sha,
            }).ToList();
        }
        // QA-003: decoy fields come from the actually-read log and the
        // post-plant existence check — never emitted as literals.
        var ledgerOutcomes = new Dictionary<string, object?>
        {
            ["decoy_invocations"] = state.DecoyInvocations,
            ["decoys_present"] = state.DecoysPresent,
        };
        if (state.SentinelNonce is not null)
        {
            ledgerOutcomes["nonce"] = state.SentinelNonce;
            ledgerOutcomes["invocations_for_this_nonce"] = state.LedgerEntriesAfter - state.LedgerEntriesBefore;
            ledgerOutcomes["ledger_pre_created_at_zero"] = state.LedgerEntriesBefore == 0;
            ledgerOutcomes["invocations_for_foreign_nonces_counted"] = state.ForeignNonceEntries;
        }
        det["sentinel_ledger_outcomes"] = ledgerOutcomes;
        return det;
    }

    private static (string Commit, bool Dirty) ReadGitState(RunState state)
    {
        // Controller runs stamp <run-root>/git-state.json (computed via the
        // allowlisted git launcher); test-owned run roots fall back to a direct
        // textual .git read (commit only; dirtiness unknown → false, binding
        // class, never in the equality domain).
        var marker = Path.Combine(state.RunRoot, "git-state.json");
        if (File.Exists(marker))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(marker));
            return (doc.RootElement.GetProperty("commit").GetString() ?? "",
                doc.RootElement.GetProperty("dirty").GetBoolean());
        }
        try
        {
            // Gitfile-aware shared resolver (PR-001/MA-VI-5): a linked
            // worktree's .git FILE resolves like a .git directory.
            return (GitResolver.ReadHeadCommit(state.RepoRoot), false);
        }
        catch (Exception)
        {
            return ("unknown", false);
        }
    }

    private static string ReadSdkPin(RunState state)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(state.SpikeRoot, "global.json")));
        return doc.RootElement.GetProperty("sdk").GetProperty("version").GetString() ?? "";
    }

    private static void Summarize(RunState state)
    {
        var failed = state.Results.Where(r => r.Status != ProbeStatus.Pass).ToList();
        var verdict = failed.Count == 0 ? "route-probes-passed"
            : state.Results.Any(r => r.Status == ProbeStatus.Fail) ? "probe-failure" : "INCOMPLETE";
        var failedText = failed.Count == 0
            ? "-"
            : string.Join(", ", failed.Select(f => $"{f.Key.ProbeId} ({Truncate(f.Detail ?? f.Status.ToString(), 120)})"));
        Console.WriteLine($"route {state.RouteId} verdict: {verdict}; failed probes: {failedText}; report: {state.ReportPath}");
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";

    // ------------------------------------------------------------- utilities

    private static void ParseArgs(RunState state, string[] args)
    {
        var probes = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length
                ? args[++i]
                : throw new ArgumentException($"missing value for {args[i]}");
            switch (args[i])
            {
                case "--probe":
                    probes.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--fixture":
                    state.FixtureOverride = Next();
                    break;
                case "--out":
                    state.ReportPath = Next();
                    break;
                case "--run-root":
                    state.RunRoot = Path.GetFullPath(Next());
                    state.RunRootOverridden = true;
                    break;
                case "--solver":
                    state.SolverOverride = Path.GetFullPath(Next());
                    break;
                case "--standard-libraries":
                    state.StandardLibraries = Next();
                    break;
                case "--verify-included-files":
                    state.VerifyIncludedFiles = Next();
                    break;
                case "--force-probe-exception":
                    var forced = Next();
                    state.ForcedExceptionProbe = forced;
                    probes.Add(forced);
                    break;
                case "--load-extra-assembly":
                    // MA-VI-3 induced-failure hook: load a family-named assembly
                    // from OUTSIDE the deps.json universe before P03 capture.
                    state.LoadExtraAssembly = Next();
                    break;
                case "--run-id":
                    // The bootstrap controller mints the run_id and binds every
                    // report to it (codex F1); direct test launches mint their own.
                    state.RunId = Next();
                    break;
                default:
                    throw new ArgumentException($"unknown argument '{args[i]}'");
            }
        }
        if (probes.Count == 0)
        {
            throw new ArgumentException("no --probe requested");
        }
        state.Probes = probes.Distinct().ToList();

        if (!state.RunRootOverridden)
        {
            state.RunRoot = FindRunRootFromArtifacts()
                ?? FindRunRootFromProfileEnvironment()
                ?? throw new ArgumentException(
                    "no run root: the harness was launched outside a controller run root and no --run-root was given (DD-008)");
        }
        Directory.CreateDirectory(state.RunRoot);
        if (state.ReportPath.Length == 0)
        {
            state.ReportPath = Path.Combine(state.RunRoot, "reports", $"route-{state.RouteId.ToLowerInvariant()}.json");
        }
    }

    /// <summary>
    /// Fallback for launcher-launched artifact COPIES (TA-B4): a copy sits
    /// outside any run root, but its EA-008-constructed environment still
    /// points at one (NUGET_PACKAGES=&lt;run-root&gt;/packages,
    /// TMPDIR=&lt;run-root&gt;/tmp). Direct launches with no constructed
    /// environment still fail loudly.
    /// </summary>
    private static string? FindRunRootFromProfileEnvironment()
    {
        foreach (var (key, suffix) in new[] { ("NUGET_PACKAGES", "packages"), ("TMPDIR", "tmp") })
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }
            var full = Path.GetFullPath(value.TrimEnd('/'));
            if (Path.GetFileName(full) == suffix)
            {
                var parent = Path.GetDirectoryName(full);
                if (parent is not null && File.Exists(Path.Combine(parent, RunLayout.RunContextFileName)))
                {
                    return parent;
                }
            }
        }
        return null;
    }

    private static string? FindRunRootFromArtifacts()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, RunLayout.RunContextFileName)))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static string FindSpikeRoot()
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
        // Fall back to walking up from the artifact location (controller runs
        // place artifacts under <spike-root>/out/<run_id>/...).
        dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DafnyCompatSpike.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("cannot locate the spike root (DafnyCompatSpike.sln)");
    }

    private static string FixtureKey(RunState state, string fixture)
    {
        if (!Path.IsPathRooted(fixture))
        {
            return fixture.Replace('\\', '/');
        }
        var full = Path.GetFullPath(fixture);
        var spike = Path.GetFullPath(state.SpikeRoot) + Path.DirectorySeparatorChar;
        return full.StartsWith(spike, StringComparison.Ordinal)
            ? Path.GetRelativePath(state.SpikeRoot, full).Replace('\\', '/')
            : full.Replace('\\', '/');
    }

    /// <summary>Canonicalizes run-root-dependent path values to the committed token (codex R2-1).</summary>
    private static string CanonicalizeValue(RunState state, string value)
    {
        var runRootFull = Path.GetFullPath(state.RunRoot);
        var spikeFull = Path.GetFullPath(state.SpikeRoot);
        var result = value.Replace(runRootFull + "/", SolverLayout.RunRootToken + "/", StringComparison.Ordinal)
            .Replace(runRootFull, SolverLayout.RunRootToken, StringComparison.Ordinal);
        if (result == value && value.Contains(spikeFull, StringComparison.Ordinal))
        {
            result = value.Replace(spikeFull + "/", "", StringComparison.Ordinal);
        }
        return result;
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
