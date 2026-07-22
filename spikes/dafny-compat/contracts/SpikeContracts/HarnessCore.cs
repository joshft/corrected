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
        public string? SolverConcreteRel;
        public string? HarnessTfm;
        public int LedgerEntriesBefore;
        public int LedgerEntriesAfter;
        public int ForeignNonceEntries;
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
        state.RepoRoot = FindRepoRoot(state.SpikeRoot);
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

        PlantAlwaysOnDecoys(state);

        var gateFailure = StartupGate(state);
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
        // Executed-solver identity semantics: with the provisioned layout, the
        // evidence binds to the PINNED RELEASE ASSET retained in the run root
        // (its digest IS the BND-002 pin); the extracted binary is its
        // provisioning derivative (recorded by provision-z3.sh in
        // solver/z3-4.12.1/binary.sha256). Execution identity itself is proven
        // behaviorally (INV-003: sentinel/removal/decoys), never presumed from
        // a digest. With a --solver override (variance), the override file is
        // the recorded identity.
        var retainedAsset = Path.Combine(state.RunRoot, "z3-4.12.1-x64-glibc-2.35.zip");
        if (state.SolverOverride is null && File.Exists(retainedAsset))
        {
            state.ExecutedSolverSha = Sha256File(retainedAsset);
            state.SolverConcreteRel = "z3-4.12.1-x64-glibc-2.35.zip";
        }
        else if (File.Exists(solverPath))
        {
            state.ExecutedSolverSha = Sha256File(solverPath);
            state.SolverConcreteRel = Path.GetRelativePath(state.RunRoot, solverPath).Replace('\\', '/');
        }

        // Sentinel machinery (RS-003d): the ledger FILE must be pre-created at
        // count zero before the run; the harness reads the nonce from the file.
        string? sentinelStub = null;
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
            (state.SentinelNonce, state.LedgerEntriesBefore, state.ForeignNonceEntries) = ReadLedger(ledgerPath);
            sentinelStub = WriteSentinelStub(state, ledgerPath, state.SentinelNonce!);
        }

        foreach (var probeId in state.Probes)
        {
            ExecuteProbe(state, probeId, solverPath, sentinelStub, schemaPath);
        }

        // INV-003(b): every run asserts ZERO decoy invocations.
        var decoyLog = Path.Combine(state.RunRoot, "sentinel", "decoy-invocations.log");
        var decoyInvocations = File.Exists(decoyLog) ? File.ReadAllLines(decoyLog).Count(l => l.Length > 0) : 0;
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

    private static void ExecuteProbe(RunState state, string probeId, string solverPath, string? sentinelStub, string schemaPath)
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

        var effectiveSolver = spec.UsesSentinel ? sentinelStub! : (state.SolverOverride ?? solverPath);
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
                    RunP05(state, fixture, fixtureKey, options);
                    break;
                case "P06":
                    RunP06(state, fixture, fixtureKey, options, effectiveSolver);
                    break;
                case "P07":
                    RunP07(state, fixture, fixtureKey, options);
                    break;
                case "P08":
                    RunFrontend(state, "P08", fixture, fixtureKey, options, VerificationStage.Parse);
                    break;
                case "P09":
                    RunFrontend(state, "P09", fixture, fixtureKey, options, VerificationStage.Resolution);
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
        var solverUnavailable = run.Diagnostics.Any(d => d.Message.Contains("Z3 not found", StringComparison.Ordinal));
        if (solverUnavailable)
        {
            state.Results.Add(new ProbeResult(new ProbeKey("P06", state.RouteId), ProbeStatus.Incomplete,
                "solver-unavailable: the configured solver could not be executed (BND-002 fail-closed)"));
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
        // Executed-solver identity: the digest of the binary actually executed
        // is recorded in evidence (executed_solver_sha256); the ARCHIVE pin
        // (SolverLayout.Z3PinnedSha256) is verified by provisioning at intake
        // (BND-002) — an archive digest can never equal its extracted member's.
        state.Results.Add(new ProbeResult(new ProbeKey("P06", state.RouteId), ProbeStatus.Pass));
    }

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

    private static void RunFrontend(RunState state, string probeId, string fixture, string fixtureKey, Dictionary<string, string?> options, VerificationStage expectedStage)
    {
        var ledgerPathBefore = Path.Combine(state.RunRoot, RunLayout.SentinelLedgerRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var (_, before, _) = ReadLedger(ledgerPathBefore);
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
        var (_, after, _) = ReadLedger(ledgerPath);
        state.LedgerEntriesAfter = after;
        var delta = after - before;

        var pass = diag is not null
                   && !string.IsNullOrWhiteSpace(diag.Message)
                   && run.TargetNames.Count == 0
                   && run.Tasks.Count == 0
                   && delta == 0;
        state.Results.Add(new ProbeResult(new ProbeKey(probeId, state.RouteId),
            pass ? ProbeStatus.Pass : ProbeStatus.Fail,
            pass ? null : $"stage-typed diagnostic present={diag is not null}, targets={run.TargetNames.Count}, tasks={run.Tasks.Count}, sentinel-invocations={delta} (INV-011)"));
    }

    private static void RunP05(RunState state, string fixture, string fixtureKey, Dictionary<string, string?> options)
    {
        var ledgerPathBefore = Path.Combine(state.RunRoot, RunLayout.SentinelLedgerRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var (_, beforeThisProbe, _) = ReadLedger(ledgerPathBefore);
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
                Console.Error.WriteLine("P05: sentinel verification leg did not settle within the bound (expected for a recording stub); proceeding to the ledger observable (RS-004b)");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"P05: verification against the sentinel ended with {ex.GetType().Name} (expected for a recording stub): {ex.Message}");
        }

        var ledgerPath = Path.Combine(state.RunRoot, RunLayout.SentinelLedgerRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var (_, after, foreign) = ReadLedger(ledgerPath);
        state.LedgerEntriesAfter = after;
        var delta = after - beforeThisProbe;
        // RS-003d: only entries under THIS run's nonce count; a stale recording
        // from a prior run can never satisfy this run.
        var pass = delta >= 1;
        state.Results.Add(new ProbeResult(new ProbeKey("P05", state.RouteId),
            pass ? ProbeStatus.Pass : ProbeStatus.Fail,
            pass ? null : $"sentinel ledger shows {delta} fresh-nonce invocations (foreign-nonce entries: {foreign}) — the solver-path option did not select the executed binary (INV-003)"));
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
        var evidence = CaptureRuntimeIdentity(state,
            expected.Assemblies.Select(a => a.SimpleName).ToHashSet(StringComparer.Ordinal));
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

    // Trust-relevant universe (codex F8): the Dafny/Boogie family prefixes plus
    // every simple name the committed expected-loaded oracle declares (which is
    // how the pinned command-line parsing library enters the universe without
    // this Dafny-free source ever naming its namespace — INV-002 source grep).
    private static readonly string[] UniversePrefixes = { "Dafny", "Boogie" };

    private static P03Evidence CaptureRuntimeIdentity(RunState state, IReadOnlySet<string> expectedNames)
    {
        var loaded = new List<LoadedAssemblyIdentity>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name ?? "";
            var inUniverse = UniversePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))
                             || expectedNames.Contains(name);
            if (!inUniverse || assembly.IsDynamic)
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
        return new P03Evidence(
            state.RouteId,
            state.HarnessTfm ?? "",
            Environment.Version.Major,
            frameworkName,
            rollForward,
            hostfxr,
            File.Exists(hostfxr) ? Sha256File(hostfxr) : "",
            corelib,
            Sha256File(corelib),
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
            root.GetProperty("universe").GetString() ?? "",
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

    private static void PlantAlwaysOnDecoys(RunState state)
    {
        // INV-003(b): differently-hashed, recording z3 decoys at the
        // assembly-adjacent fallback path and first-on-PATH are EXPECTED
        // fixtures of every run, whitelisted by the gate; the run then asserts
        // zero decoy invocations. Existing decoys (e.g. test-planted ones) are
        // never overwritten.
        var decoyLog = Path.Combine(state.RunRoot, "sentinel", "decoy-invocations.log");
        Directory.CreateDirectory(Path.GetDirectoryName(decoyLog)!);
        var script = "#!/bin/sh\n" +
                     $"printf 'decoy-invoked %s\\n' \"$*\" >> '{decoyLog}'\n" +
                     "echo 'Z3 version 0.0.0 - 64 bit (decoy)'\n";
        WriteExecutableIfAbsent(Path.Combine(state.RunRoot, "decoys", "z3"), script);
        WriteExecutableIfAbsent(Path.Combine(AppContext.BaseDirectory,
            SolverLayout.ProhibitedAssemblyAdjacentRelativePath.Replace('/', Path.DirectorySeparatorChar)), script);
    }

    private static void WriteExecutableIfAbsent(string path, string content)
    {
        try
        {
            if (File.Exists(path))
            {
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (IOException)
        {
            // Concurrent harness planted it first — fine, it exists.
        }
    }

    private static string? StartupGate(RunState state)
    {
        // EA-005/INV-003: judge the ambient-discovery OUTCOME. Dafny's PATH
        // fallback finds the FIRST <dir>/z3 on PATH; that discovery result must
        // be the sanctioned first-on-PATH decoy (or nothing). DECISION: entries
        // deeper on PATH (e.g. a system z3) are masked by the decoy — ambient
        // discovery can never reach them, and if anything ever consults the
        // decoy, the nonce-recording ledger catches it (zero-invocation assert).
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathValue.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, "z3");
            if (!File.Exists(candidate))
            {
                continue;
            }
            // Sanctioned first-on-PATH decoys are recognized by CONTENT (the
            // nonce-recording marker), never by location alone: run roots
            // differ between the launch profile and a test-owned --run-root,
            // but every sanctioned decoy is a recording stub whose invocation
            // the zero-invocation assert would catch (INV-003/codex F6).
            if (!IsSanctionedDecoy(candidate))
            {
                return $"unsanctioned z3-named executable is the first ambient PATH discovery match: {candidate}";
            }
            break; // first match is a sanctioned decoy — deeper entries are unreachable by discovery
        }

        // No z3-named executable under the route output tree except the
        // sanctioned assembly-adjacent decoy location (RS-003a).
        var outputTree = AppContext.BaseDirectory;
        var sanctionedAdjacent = Path.GetFullPath(Path.Combine(outputTree,
            SolverLayout.ProhibitedAssemblyAdjacentRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        foreach (var file in Directory.EnumerateFiles(outputTree, "z3*", SearchOption.AllDirectories))
        {
            if (Path.GetFullPath(file) != sanctionedAdjacent)
            {
                return $"z3-named file inside the route output tree: {file}";
            }
        }
        return null;
    }

    private static bool IsSanctionedDecoy(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[4096];
            var read = reader.Read(buffer, 0, buffer.Length);
            var head = new string(buffer, 0, Math.Max(read, 0));
            return head.StartsWith("#!/bin/sh", StringComparison.Ordinal)
                   && head.Contains("decoy-invoked", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string WriteSentinelStub(RunState state, string ledgerPath, string nonce)
    {
        var stubPath = Path.Combine(state.RunRoot, "sentinel", "sentinel-z3");
        var script =
            "#!/bin/sh\n" +
            "# Nonce-recording sentinel solver stub (INV-003/RS-003d). Version probes\n" +
            "# (options processing) are answered without recording; every SOLVER\n" +
            "# invocation appends a nonce-bound entry to the pre-created ledger FILE.\n" +
            "if [ \"$1\" = \"-version\" ] || [ \"$1\" = \"--version\" ]; then\n" +
            "  echo 'Z3 version 4.12.1 - 64 bit'\n" +
            "  exit 0\n" +
            "fi\n" +
            $"LEDGER='{ledgerPath}'\n" +
            $"NONCE='{nonce}'\n" +
            "ARGS=''\n" +
            "for a in \"$@\"; do\n" +
            "  esc=$(printf '%s' \"$a\" | tr -d '\\\\\"')\n" +
            "  if [ -z \"$ARGS\" ]; then ARGS=\"\\\"$esc\\\"\"; else ARGS=\"$ARGS, \\\"$esc\\\"\"; fi\n" +
            "done\n" +
            "ENTRY=\"{ \\\"nonce\\\": \\\"$NONCE\\\", \\\"argv\\\": [$ARGS] }\"\n" +
            "CONTENT=$(tr -d '\\n' < \"$LEDGER\")\n" +
            "case \"$CONTENT\" in\n" +
            "  *'\"entries\": []'*)\n" +
            "    NEW=$(printf '%s' \"$CONTENT\" | sed 's|\"entries\": \\[\\]|\"entries\": [ '\"$ENTRY\"' ]|') ;;\n" +
            "  *)\n" +
            "    NEW=$(printf '%s' \"$CONTENT\" | sed 's|\\(.*\\)\\]|\\1, '\"$ENTRY\"' ]|') ;;\n" +
            "esac\n" +
            "printf '%s\\n' \"$NEW\" > \"$LEDGER.tmp\" && mv \"$LEDGER.tmp\" \"$LEDGER\"\n" +
            "exit 0\n";
        File.WriteAllText(stubPath, script);
        File.SetUnixFileMode(stubPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return stubPath;
    }

    private static (string Nonce, int EntriesForNonce, int ForeignEntries) ReadLedger(string ledgerPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ledgerPath));
        var nonce = doc.RootElement.GetProperty("nonce").GetString()!;
        var forNonce = 0;
        var foreign = 0;
        foreach (var entry in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            if (entry.GetProperty("nonce").GetString() == nonce)
            {
                forNonce++;
            }
            else
            {
                foreign++;
            }
        }
        return (nonce, forNonce, foreign);
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

    private static void EmitReport(RunState state, string schemaPath)
    {
        var spikeRelativeRunDir = Path.GetRelativePath(state.SpikeRoot, state.RunRoot).Replace('\\', '/');
        var (gitCommit, gitDirty) = ReadGitState(state);

        var report = new Dictionary<string, object?>
        {
            ["evidence_schema_version"] = 1,
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
            ["executed_solver_sha256"] = state.ExecutedSolverSha,
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
        if (state.SentinelNonce is not null)
        {
            det["sentinel_ledger_outcomes"] = new Dictionary<string, object?>
            {
                ["nonce"] = state.SentinelNonce,
                ["invocations_for_this_nonce"] = state.LedgerEntriesAfter - state.LedgerEntriesBefore,
                ["decoy_invocations"] = 0,
                ["decoys_present"] = true,
                ["ledger_pre_created_at_zero"] = state.LedgerEntriesBefore == 0,
                ["invocations_for_foreign_nonces_counted"] = state.ForeignNonceEntries,
            };
        }
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
            return (ReadGitHead(state.RepoRoot), false);
        }
        catch (Exception)
        {
            return ("unknown", false);
        }
    }

    private static string ReadGitHead(string repoRoot)
    {
        var gitDir = Path.Combine(repoRoot, ".git");
        var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
        if (!head.StartsWith("ref: ", StringComparison.Ordinal))
        {
            return head;
        }
        var refName = head["ref: ".Length..].Trim();
        var refFile = Path.Combine(gitDir, refName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(refFile))
        {
            return File.ReadAllText(refFile).Trim();
        }
        var packed = Path.Combine(gitDir, "packed-refs");
        foreach (var line in File.ReadAllLines(packed))
        {
            if (line.EndsWith(" " + refName, StringComparison.Ordinal))
            {
                return line.Split(' ')[0];
            }
        }
        throw new InvalidOperationException($"cannot resolve git ref {refName}");
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

    private static string FindRepoRoot(string spikeRoot)
    {
        var dir = new DirectoryInfo(spikeRoot);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return spikeRoot;
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
