// Tests INV-003 / BND-002: the executed solver is the provisioned Z3 4.12.1 —
// proven, not presumed.
// Enforces ARCHITECTURE.md TB-004 (inbound toolchain supply chain): this suite
// is a listed Test for that boundary (solver digest + executed-solver identity).
//
// TA-B1/TA-B2 discipline: the TESTS own the run roots, pre-create and read the
// nonce LEDGER FILE directly, construct decoys/faults themselves (deleting the
// provisioned binary, planting PATH entries), and never trust harness-reported
// counters as the sole observable.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

// TA-A13: the decoy test writes into the shared run-context artifact directory.
[Collection(SharedStateMutatingCollection.Name)]
public class Inv003SolverIdentityTests
{
    // Tests INV-003/BND-002 [unit]: pinned URL + pinned SHA-256 committed in repo.
    [Fact]
    public void Z3Pin_CommittedDigestUrlVersionAndRidMessage()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("config", "z3-pin.json"));
        var root = doc.RootElement;
        Assert.Equal("4.12.1", root.GetProperty("version").GetString());
        Assert.Equal(SpecConstants.Z3Sha256, root.GetProperty("sha256").GetString());
        Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("sha256").GetString());
        Assert.StartsWith("https://github.com/Z3Prover/z3/releases/download/z3-4.12.1/", root.GetProperty("url").GetString());
        Assert.Equal(SpecConstants.RidFailMessage, root.GetProperty("unsupported_rid_message").GetString());

        var script = File.ReadAllText(SpikePaths.P("scripts", "provision-z3.sh"));
        Assert.Contains(SpecConstants.Z3Sha256, script);
        Assert.Contains(SpecConstants.RidFailMessage, script);
    }

    // Tests INV-003 [unit] (RS-003a): install location outside every ambient
    // discovery location — never the assembly-adjacent fallback path.
    [Fact]
    public void InstallLocation_OutsideAmbientDiscovery_NotAssemblyAdjacent()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("config", "z3-pin.json"));
        var install = doc.RootElement.GetProperty("install_relative_to_run_root").GetString()!;
        Assert.Equal(SolverLayout.SolverRelativePath, install);
        Assert.DoesNotContain(SolverLayout.ProhibitedAssemblyAdjacentRelativePath, install);
        Assert.False(install.StartsWith("z3/bin/"), "install path collides with Dafny's assembly-adjacent discovery layout");
    }

    // Tests INV-003 [unit] (RS-003c): ONE shared, unit-tested function builds
    // the solver-path option for every probe (sentinel and real runs alike).
    [Fact]
    public void SolverPathOptionBuilder_BuildsExplicitSolverPathOption()
    {
        var opt = SolverPathOptionBuilder.Build("/run-root/solver/z3-4.12.1/bin/z3");
        Assert.Equal(2, opt.Count);
        Assert.Equal("--solver-path", opt[0]);
        Assert.Equal("/run-root/solver/z3-4.12.1/bin/z3", opt[1]);
    }

    // Tests INV-003 [integration] (RS-003d, codex F6, TA-B1): P05 — the TEST
    // mints the nonce, pre-creates the ledger FILE at count zero in a
    // test-owned run root, and reads the LEDGER FILE (not the report) after the
    // run; the report's binding nonce is cross-checked against the ledger file.
    // Entry: child-process harness (run-context artifact), sentinel-configured,
    //   --run-root pointing at the test-owned root.
    // Through: the real solver-path option wiring (the shared builder); the
    //   sentinel stub is the sanctioned recording double.
    // Exit: ledger FILE shows >=1 invocation bound to THIS test's nonce.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void P05_SentinelNonce_ProvesSolverPathSelectsExecutedBinary(string route)
    {
        var runRoot = SpikePaths.TestScratch($"inv003-p05-{route}");
        var nonce = $"nonce-{Guid.NewGuid():N}";
        WriteLedger(runRoot, nonce);
        var report = Path.Combine(runRoot, "p05-report.json");

        var result = Launch.Harness(route, "--probe", "P05", "--run-root", runRoot, "--out", report);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);

        // TEST-SIDE observable: the ledger file + append-only entry files
        // (MA-RB-3 layout), decoded independently of the production reader.
        using var ledger = Launch.Ledger(runRoot);
        Assert.Equal(nonce, ledger.RootElement.GetProperty("nonce").GetString());
        var entries = Launch.LedgerFileEntries(runRoot).Where(e => e.Nonce == nonce).ToList();
        Assert.True(entries.Count >= 1, "no sentinel invocation recorded in the LEDGER entry files for this test's nonce");

        // Cross-check: the report's binding identity carries the same nonce.
        using var doc = Launch.Report(report);
        Assert.Equal(nonce, doc.RootElement.GetProperty("binding_identity").GetProperty("sentinel_nonce").GetString());
    }

    // Tests INV-003 [integration] (RS-003b, TA-B2 — the wrong-reason pass of
    // AP-010): removal test. The TEST provisions into a test-owned run root,
    // then the TEST ITSELF deletes the provisioned binary, then launches
    // P06/P07 normally (no fault flags). P06 and P07 must INDIVIDUALLY record
    // solver-unavailable failure — not merely a non-COMPATIBLE overall verdict.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void RemovalTest_TestDeletesProvisionedZ3_P06AndP07IndividuallyRecordSolverUnavailable(string route)
    {
        var runRoot = SpikePaths.TestScratch($"inv003-removal-{route}");
        var provision = Launch.Script("scripts/provision-z3.sh", null, "--run-root", runRoot);
        Assert.True(provision.ExitCode == 0, $"provisioning into the test-owned run root failed: {provision.StdErr}");
        var solver = Path.Combine(runRoot, SolverLayout.SolverRelativePath);
        Assert.True(File.Exists(solver), "provisioned solver missing — cannot construct the removal fault");

        File.Delete(solver); // the TEST constructs the fault (TA-B2)
        Assert.False(File.Exists(solver));

        WriteLedger(runRoot, $"nonce-{Guid.NewGuid():N}");
        var report = Path.Combine(runRoot, "removal-report.json");
        var result = Launch.Harness(route, "--probe", "P06,P07", "--run-root", runRoot, "--out", report);
        Assert.NotEqual(ExitCodes.RouteProbesPassed, result.ExitCode);
        using var doc = Launch.Report(report);
        foreach (var probeId in new[] { "P06", "P07" })
        {
            var probe = doc.RootElement.GetProperty("deterministic").GetProperty("per_probe_results").EnumerateArray()
                .Single(p => p.GetProperty("probe").GetString() == probeId);
            Assert.NotEqual("pass", probe.GetProperty("status").GetString());
            Assert.Contains("solver-unavailable", probe.GetProperty("detail").GetString());
        }
    }

    // Tests INV-003 [integration] (codex F6, TA-B1): always-on decoys — the
    // TEST writes differently-hashed, recording decoys at the assembly-adjacent
    // path and first-on-PATH; after a real P06 run the TEST reads the DECOY
    // ledger files it created and asserts zero invocations.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void Decoys_TestPlanted_ZeroDecoyInvocations_OnRealRun(string route)
    {
        var runRoot = SpikePaths.TestScratch($"inv003-decoy-{route}");
        var provision = Launch.Script("scripts/provision-z3.sh", null, "--run-root", runRoot);
        Assert.True(provision.ExitCode == 0, $"provisioning failed: {provision.StdErr}");
        WriteLedger(runRoot, $"nonce-{Guid.NewGuid():N}");

        // TEST-constructed decoys. The assembly-adjacent decoy records to its
        // own TEST-owned ledger file (that location is sanctioned by location,
        // with this zero-invocation check as the behavioral backstop). The
        // first-on-PATH decoy sits in a decoys/ dir, which QA-015 sanctions by
        // EXACT DIGEST only — so the test plants the sanctioned recording
        // script itself (recording to the run root's decoy-invocations log,
        // the test-owned observable for that leg).
        var artifact = RunContext.Resolve(route == "A" ? "RouteAHarness" : "RouteBHarness");
        var artifactDir = Path.GetDirectoryName(artifact.AbsolutePath)!;
        var adjacentLedger = Path.Combine(runRoot, "decoy-adjacent.log");
        var pathLedger = Path.Combine(runRoot, "sentinel", "decoy-invocations.log");
        var adjacentDecoy = SpikePaths.WriteExecutable(Path.Combine(artifactDir, "z3", "bin", "z3-4.12.1"),
            $"echo \"decoy-invoked $*\" >> '{adjacentLedger}'; echo 'Z3 version 0.0.0 - 64 bit'");
        var decoyDir = Path.Combine(runRoot, "decoys");
        var sanctioned = HarnessCore.SanctionedDecoyScript(runRoot);
        Directory.CreateDirectory(decoyDir);
        File.WriteAllText(Path.Combine(decoyDir, "z3"), sanctioned);
        File.SetUnixFileMode(Path.Combine(decoyDir, "z3"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        try
        {
            var report = Path.Combine(runRoot, "decoy-report.json");
            var result = Launch.Harness(route, "--probe", "P06", "--run-root", runRoot, "--out", report);
            Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);

            // TEST-SIDE observables: the decoy ledger files the test created.
            Assert.False(File.Exists(adjacentLedger), "assembly-adjacent decoy was EXECUTED during a real run (INV-003)");
            Assert.False(File.Exists(pathLedger), "first-on-PATH decoy was EXECUTED during a real run (INV-003)");
            Assert.True(File.Exists(adjacentDecoy), "decoy fixture vanished — the startup gate must whitelist, not delete, sanctioned decoys");
        }
        finally
        {
            File.Delete(adjacentDecoy);
        }
    }

    // Tests INV-003/EA-005 [integration] (TA-B2): startup gate fail-closed —
    // the TEST constructs the child PATH with an UNsanctioned z3-named
    // executable first-on-PATH; the harness must abort INCOMPLETE at the gate.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void StartupGate_TestPlantedUnsanctionedZ3OnPath_FailsClosed(string route)
    {
        var runRoot = SpikePaths.TestScratch($"inv003-gate-{route}");
        var provision = Launch.Script("scripts/provision-z3.sh", null, "--run-root", runRoot);
        Assert.True(provision.ExitCode == 0, $"provisioning failed: {provision.StdErr}");
        WriteLedger(runRoot, $"nonce-{Guid.NewGuid():N}");

        // TEST-constructed environment: hostile z3 first on PATH, not at a sanctioned decoy location.
        var badDir = Path.Combine(runRoot, "unsanctioned");
        SpikePaths.WriteExecutable(Path.Combine(badDir, "z3"), "echo 'Z3 version 6.6.6 - 64 bit'");
        var env = new Dictionary<string, string>(EnvProfiles.For("harness", runRoot).ToDictionary(kv => kv.Key, kv => kv.Value))
        {
            ["PATH"] = badDir + Path.PathSeparator + "/usr/bin:/bin",
        };

        var report = Path.Combine(runRoot, "gate-report.json");
        var result = Launch.HarnessWithEnv(route, env, "--probe", "P06", "--run-root", runRoot, "--out", report);
        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
        using var doc = Launch.Report(report);
        Assert.Contains("startup-gate", doc.RootElement.GetRawText());
    }

    /// <summary>TA-B1: the TEST pre-creates the nonce-bound ledger at count zero (RS-003d).</summary>
    internal static void WriteLedger(string runRoot, string nonce)
    {
        var path = Path.Combine(runRoot, RunLayout.SentinelLedgerRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"{{ \"nonce\": \"{nonce}\", \"entries\": [] }}\n");
    }
}
