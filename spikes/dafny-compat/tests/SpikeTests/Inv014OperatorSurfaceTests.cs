// Tests INV-014: operator surface — the spike is runnable, readable, and
// recoverable by a stranger.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

// TA-A13: the stale-artifact test writes markers into repo-local bin dirs.
[Collection(SharedStateMutatingCollection.Name)]
public class Inv014OperatorSurfaceTests
{
    // Tests INV-014 [unit]: README names the canonical entry point, the phase
    // ordering, the pinned SDK, and its install source.
    [Fact]
    public void Readme_NamesEntryPoint_PhaseOrdering_PinnedSdkAndInstallSource()
    {
        var readmePath = SpikePaths.P("README.md");
        Assert.True(File.Exists(readmePath), "missing spikes/dafny-compat/README.md (INV-014)");
        var readme = File.ReadAllText(readmePath);
        Assert.Contains("scripts/run-spike.sh", readme);
        Assert.Contains("provision → locked restore → build → test", readme);
        Assert.Contains(SpecConstants.SdkPin, readme);
        Assert.Contains("dotnet.microsoft.com", readme); // install source
        Assert.Contains("scripts/regen-sample.sh", readme); // DD-008 regeneration procedure named
    }

    // Tests INV-014 [unit]: a required .gitignore covers the untracked out/
    // area — a public repo must never accidentally receive the z3 binary or
    // package cache.
    [Fact]
    public void GitIgnore_CoversOutArea()
    {
        var gitignorePath = SpikePaths.P(".gitignore");
        Assert.True(File.Exists(gitignorePath), "missing spikes/dafny-compat/.gitignore (INV-014)");
        var lines = File.ReadAllLines(gitignorePath).Select(l => l.Trim()).ToList();
        Assert.Contains("out/", lines);
    }

    // Tests INV-014 [unit]: committed-samples location is distinct from the
    // untracked out/ area.
    [Fact]
    public void CommittedSamplesLocation_DistinctFromOutArea()
    {
        Assert.True(Directory.Exists(SpikePaths.P("evidence", "samples")));
        Assert.False(SpikePaths.P("evidence", "samples").Contains($"{Path.DirectorySeparatorChar}out{Path.DirectorySeparatorChar}"));
    }

    // Tests INV-014/AP-016 [integration]: provisioning is idempotent and
    // partial-state-safe — tested from CLEAN, PARTIAL (truncated/corrupt file
    // present), and FULL states, plus a double-run identical-state check.
    [Fact]
    public void Provisioning_ThreeStates_PlusDoubleRun_Idempotent()
    {
        var scratch = SpikePaths.TestScratch("inv014-provision");

        // State 1 — CLEAN: provision from nothing must succeed.
        var clean = Launch.Script("scripts/provision-z3.sh", null, "--run-root", Path.Combine(scratch, "clean"));
        Assert.True(clean.ExitCode == 0, $"clean-state provisioning failed: {clean.StdErr}");
        var solverPath = Path.Combine(scratch, "clean", "solver", "z3-4.12.1", "bin", "z3");
        Assert.True(File.Exists(solverPath), "provisioned solver missing after clean-state run");
        var digestAfterClean = SpikePaths.Sha256File(solverPath);

        // State 2 — PARTIAL: a truncated/corrupt download present; re-run must recover.
        var partialRoot = Path.Combine(scratch, "partial");
        Directory.CreateDirectory(partialRoot);
        File.WriteAllText(Path.Combine(partialRoot, "z3-4.12.1-x64-glibc-2.35.zip"), "TRUNCATED GARBAGE");
        var partial = Launch.Script("scripts/provision-z3.sh", null, "--run-root", partialRoot);
        Assert.True(partial.ExitCode == 0, $"partial-state provisioning failed to recover: {partial.StdErr}");
        Assert.Equal(digestAfterClean, SpikePaths.Sha256File(Path.Combine(partialRoot, "solver", "z3-4.12.1", "bin", "z3")));

        // State 3 — FULL + double-run: second run over a complete install
        // produces identical state (AP-016).
        var second = Launch.Script("scripts/provision-z3.sh", null, "--run-root", Path.Combine(scratch, "clean"));
        Assert.True(second.ExitCode == 0, $"double-run provisioning failed: {second.StdErr}");
        Assert.Equal(digestAfterClean, SpikePaths.Sha256File(solverPath));
    }

    // Tests INV-014/BND-002 [integration]: fail-closed unsupported-RID behavior
    // with the EXACT message, before any network fetch.
    [Fact]
    public void UnsupportedRid_FailsClosed_WithExactMessage()
    {
        var scratch = SpikePaths.TestScratch("inv014-rid");
        var env = new Dictionary<string, string> { ["SPIKE_RID_OVERRIDE"] = "win-x64" };
        var result = Launch.Script("scripts/provision-z3.sh", env, "--run-root", scratch);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(SpecConstants.RidFailMessage, result.StdOut + result.StdErr);
        Assert.DoesNotContain("curl", result.StdOut); // never an unverified fetch on the failure path
    }

    // Tests INV-014 [integration]: prerequisite-failure messages name the
    // remediation step.
    [Fact]
    public void PrerequisiteFailure_NamesRemediationStep()
    {
        var scratch = SpikePaths.TestScratch("inv014-prereq");
        var report = Path.Combine(scratch, "report.json");
        // Run a verification probe with no provisioned solver available.
        var result = Launch.Harness("A", "--probe", "P06", "--run-root", Path.Combine(scratch, "empty-run-root"), "--out", report);
        Assert.Equal(ExitCodes.Incomplete, result.ExitCode);
        Assert.Contains("run provisioning first: scripts/provision-z3.sh", result.StdOut + result.StdErr);
    }

    // Tests INV-014 [integration]: per-route terminal verdict summary — a human
    // has an entry point without parsing JSON.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void TerminalVerdictSummary_PresentInChildOutput(string route)
    {
        var scratch = SpikePaths.TestScratch($"inv014-summary-{route}");
        var report = Path.Combine(scratch, "report.json");
        var result = Launch.Harness(route, "--probe", "P06", "--out", report);
        var summaryLine = (result.StdOut + result.StdErr).Split('\n')
            .FirstOrDefault(l => l.Contains($"route {route}", StringComparison.OrdinalIgnoreCase) && l.Contains("verdict", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrEmpty(summaryLine),
            "no per-route terminal verdict summary line (verdict, failed probes with reasons, report path) in child output (INV-014)");
        Assert.Contains(Path.GetFileName(report), summaryLine);
    }

    // Tests INV-014/DD-008 [integration] (TA-A9): stale repo-local bin/obj
    // artifacts are neither consumed nor launched. The TEST plants uniquely
    // hashed marker files into the repo-local bin dirs ITSELF, runs the
    // controller, then reads the BUILD RECEIPT file and asserts every recorded
    // artifact path lies beneath the run root and no recorded digest equals a
    // planted marker's digest.
    [Fact]
    public void StaleRepoLocalArtifacts_TestPlanted_NeitherConsumedNorLaunched()
    {
        using var scope = SpikePaths.TransientScratch("inv014-stale-artifacts");
        var runRoot = scope.Root;
        var marker = $"STALE-{Guid.NewGuid():N}";
        var planted = new List<string>();
        foreach (var project in new[] { "harness/RouteAHarness", "harness/RouteBHarness" })
        {
            var binDir = Path.Combine(SpikePaths.SpikeRoot, project.Replace('/', Path.DirectorySeparatorChar), "bin", "Debug", "net10.0");
            Directory.CreateDirectory(binDir);
            var file = Path.Combine(binDir, marker + ".dll");
            File.WriteAllText(file, marker); // the TEST constructs the stale state
            planted.Add(file);
        }
        var plantedDigests = planted.Select(SpikePaths.Sha256File).ToHashSet();

        try
        {
            var result = Launch.Script("scripts/run-spike.sh", null,
                "--run-root", runRoot, "--out", Path.Combine(runRoot, "run-report.json"));
            Assert.True(result.ExitCode == 0, $"controller run failed: {result.StdErr}");

            using var receipt = Launch.Receipt(runRoot, RunLayout.BuildReceiptRelativePath);
            var artifacts = receipt.RootElement.GetProperty("artifacts").EnumerateArray().ToList();
            Assert.NotEmpty(artifacts);
            var runRootFull = Path.GetFullPath(runRoot) + Path.DirectorySeparatorChar;
            foreach (var artifact in artifacts)
            {
                var path = artifact.GetProperty("path").GetString()!;
                var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(runRoot, path));
                Assert.StartsWith(runRootFull, full, StringComparison.Ordinal); // artifacts live beneath the run root ONLY
                Assert.DoesNotContain(artifact.GetProperty("sha256").GetString(), plantedDigests);
            }

            scope.Commit(); // full run passed — reclaim the ~550MB run root
        }
        finally
        {
            foreach (var file in planted.Where(File.Exists)) { File.Delete(file); }
        }
    }

    // Tests DD-005/DF-001 [unit]: the spike's CI is REALIZED, not stubbed. Two
    // committed artifacts must exist and encode the FROM-CLEAN gate (PMB-002/
    // AP-021) — the single net that would have caught BOTH post-merge bugs:
    //   (1) the co-located REQUIREMENTS CHARTER spikes/dafny-compat/ci/spike-ci.yml
    //       (the authoritative spec, with the linux-x64 RID pin), and
    //   (2) the LIVE GitHub Actions workflow .github/workflows/dafny-compat-spike.yml
    //       (GitHub requires workflows there, so it cannot live under the spike),
    //       which checks out full history, installs the pinned SDK, runs
    //       FROM-CLEAN, and invokes the documented root command verbatim.
    [Fact]
    public void CiWorkflow_Realized_LiveJobAndCharter_EncodeFromCleanGate()
    {
        // (1) Co-located requirements charter.
        var charter = SpikePaths.P("ci", "spike-ci.yml");
        Assert.True(File.Exists(charter), "missing CI requirements charter (DD-005)");
        var charterText = File.ReadAllText(charter);
        Assert.Contains("DF-001", charterText);
        Assert.Contains("linux-x64", charterText);
        Assert.Contains("FROM-CLEAN", charterText);
        Assert.Contains("rm -rf", charterText);

        // (2) Live GitHub Actions realization at the repo root (DF-001 discharged).
        var workflow = SpikePaths.Repo(".github", "workflows", "dafny-compat-spike.yml");
        Assert.True(File.Exists(workflow),
            "missing the LIVE CI workflow .github/workflows/dafny-compat-spike.yml (DF-001 realization)");
        var wf = File.ReadAllText(workflow);
        Assert.Contains("fetch-depth: 0", wf);                 // QA-023 note 1 (QA001 ancestor check)
        Assert.Contains("global.json", wf);                    // pinned-SDK install
        Assert.Contains("rm -rf spikes/dafny-compat/out", wf); // FROM-CLEAN (PMB-002/AP-021)
        Assert.Contains("env -i HOME=\"$HOME\" bash -p spikes/dafny-compat/scripts/run-spike.sh", wf); // documented root command, verbatim
    }
}
