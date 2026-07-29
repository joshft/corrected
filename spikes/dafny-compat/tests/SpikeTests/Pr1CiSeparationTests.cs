// PR1 (Group A) — QA-001/QA-008 class-fix: the "general gate excludes the
// determinism-lane category" claim is proven BY EXECUTION (PMB-001/AP-020), not by
// parse alone. The heavyweight determinism-lane xUnit tests throw a LOUD TYPED skip
// below the >= 8-core floor; on the 4-vCPU general conformance gate they cannot pass,
// so the general gate must filter them out by test Category while the dedicated
// floor-capable lane runs them for real — and local run-spike.sh / commands.test
// (>= 8 cores, no arg) still runs every category.
//
// The earlier guard asserted only BY PARSE that run-spike.sh maps the arg to a filter;
// it was structurally blind to the QA-008 bug where the OUTER watchdog parsed
// --exclude-category but spawned the INNER controller (which runs the suite `dotnet
// test`) with a reconstructed argv that DROPPED it — so the filter never applied and
// the gate would have gone red. This class now EXECUTES run-spike.sh's dry-run
// (--print-inner-filter) to prove the exclusion actually reaches the inner filter.
using System.Text.RegularExpressions;
using Xunit;

namespace Corrected.Spike.Tests;

public class Pr1CiSeparationTests
{
    private const string ExcludedCategory = "determinism-lane";

    private static string GeneralGateWorkflow =>
        File.ReadAllText(SpikePaths.Repo(".github", "workflows", "dafny-compat-spike.yml"));

    private static string LaneWorkflow =>
        File.ReadAllText(SpikePaths.Repo(".github", "workflows", "p3-determinism-lane.yml"));

    // Tests QA-001 [unit]: the 4-vCPU general gate OPTS IN to the exclusion — it
    // invokes run-spike.sh WITH --exclude-category determinism-lane (env -i strips
    // env, so it must be an ARG). A gate that dropped the opt-in fails here.
    [Fact]
    public void GeneralGate_PassesTheExclusionArg()
    {
        Assert.Matches(new Regex($@"run-spike\.sh\s+--exclude-category\s+{Regex.Escape(ExcludedCategory)}\b"), GeneralGateWorkflow);
    }

    // Tests QA-008/QA-012/QA-013 [integration] (EXECUTION — PMB-001/AP-020): run-spike.sh
    // actually FORWARDS --exclude-category from the outer watchdog into the INNER
    // controller argv AND constructs the REAL inner suite command with the filter. The
    // dry-run (--print-inner-filter) reconstructs the inner argv via the SAME
    // build_inner_args the real spawn uses, re-parses it, and prints the SAME SUITE_CMD
    // the real suite phase runs via build_suite_cmd — so this test is bound to the REAL
    // CONSUMED `dotnet test` argv (filter splice included), NOT a parallel copy (QA-013:
    // dropping the filter splice on a separate real line would otherwise stay green).
    // Inherent residual (documented at the real suite phase): a dry-run proves the
    // command is CONSTRUCTED, not that run_cmd spawns it — that last link is netted
    // fail-closed by the live general gate. A no-arg run must carry NO filter.
    [Fact]
    public void RunSpike_ForwardsExclusion_ToInnerSuiteFilter_ByExecution()
    {
        // WITH the arg: inner argv carries the exclusion; the SHARED suite command
        // (the exact argv the real phase runs) is a `dotnet test` with the filter.
        var withArg = Launch.Script("scripts/run-spike.sh", null, "--exclude-category", ExcludedCategory, "--print-inner-filter");
        Assert.True(withArg.ExitCode == 0, $"dry-run failed (exit {withArg.ExitCode}): {withArg.StdErr}");
        Assert.Contains($"--exclude-category {ExcludedCategory}", withArg.StdOut); // forwarded into the inner argv (QA-008)
        var suiteCmdWith = SuiteCmdLine(withArg.StdOut);
        Assert.Contains("dotnet test DafnyCompatSpike.sln", suiteCmdWith);          // the REAL consumed suite command...
        Assert.Contains($"--filter Category!={ExcludedCategory}", suiteCmdWith);    // ...carries the filter (QA-012/QA-013)

        // WITHOUT the arg: the suite command is a `dotnet test` with NO filter.
        var noArg = Launch.Script("scripts/run-spike.sh", null, "--print-inner-filter");
        Assert.True(noArg.ExitCode == 0, $"dry-run (no arg) failed (exit {noArg.ExitCode}): {noArg.StdErr}");
        Assert.DoesNotContain("--exclude-category", noArg.StdOut);
        var suiteCmdNo = SuiteCmdLine(noArg.StdOut);
        Assert.Contains("dotnet test DafnyCompatSpike.sln", suiteCmdNo); // still the suite command...
        Assert.DoesNotContain("--filter", suiteCmdNo);                  // ...but no filter (every category runs)
    }

    // Extracts the single `inner-suite-cmd:` line (the shared SUITE_CMD the dry-run prints).
    private static string SuiteCmdLine(string stdout)
    {
        var line = stdout.Split('\n').SingleOrDefault(l => l.StartsWith("inner-suite-cmd:", StringComparison.Ordinal));
        Assert.False(line is null, "dry-run printed no `inner-suite-cmd:` line (the shared build_suite_cmd output)");
        return line!;
    }

    // Tests QA-001 [unit]: the DEDICATED floor-capable lane is the real home — it RUNS
    // the excluded category (Category=determinism-lane) and NEVER applies the exclusion,
    // so the throw-based tests are actually exercised somewhere on >= 8 cores.
    [Fact]
    public void DedicatedLane_RunsTheCategory_AndDoesNotExcludeIt()
    {
        var lane = LaneWorkflow;
        Assert.Contains($"Category={ExcludedCategory}", lane);
        Assert.DoesNotContain("--exclude-category", lane);
    }

    // Tests DF-013 (MA-RB-2) [unit]: the lane fixture's launcher cap must accommodate TWO nested
    // run-spike runs (each self-budgeting 1800s) — a too-small cap SIGKILLs a WORKING lane and reds
    // every lane test on the pinned >= 8-core runner. It must exceed 2×1800s and stay under the
    // 90-min (5400s) CI job backstop the lane workflow actually commits.
    [Fact]
    public void LaneLauncherTimeout_FitsTwoNestedRuns_UnderTheJobBackstop()
    {
        Assert.True(Launch.LaneTimeoutSeconds >= 2 * 1800,
            $"lane launcher cap {Launch.LaneTimeoutSeconds}s must exceed two nested 1800s run budgets (DF-013)");
        Assert.True(Launch.LaneTimeoutSeconds < 90 * 60,
            $"lane launcher cap {Launch.LaneTimeoutSeconds}s must stay under the 90-min CI job backstop (DF-013)");
        Assert.Contains("timeout-minutes: 90", LaneWorkflow); // the backstop the cap is coordinated with
    }

    // Tests DF-014 (MA-ID-001) [unit] (structural CI-config guard — a CI-only job step cannot be
    // executed here): the lane workflow LOUD-FAILS if it lands on a sub-floor runner
    // (nproc < core_floor -> exit 1) rather than reading green on a runner that flaps.
    [Fact]
    public void LaneWorkflow_LoudFailsOnASubFloorRunner()
    {
        var lane = LaneWorkflow;
        Assert.Contains("-lt \"$floor\"", lane);     // the floor comparison condition
        Assert.Contains("SUB-FLOOR runner", lane);   // the loud-fail diagnostic (DF-014)
    }

    // Tests DF-014 (MA-ID-001) [unit] (structural CI-config guard): the lane workflow asserts the
    // extracted-script step actually EMITTED a receipt (content-existence, not just exit code) —
    // fail-closed on a silent no-receipt.
    [Fact]
    public void LaneWorkflow_AssertsReceiptWasEmitted()
    {
        var lane = LaneWorkflow;
        Assert.Contains("determinism-receipt.json", lane);
        Assert.Contains("receipt missing or empty", lane); // the fail-closed existence guard (DF-014)
    }

    // Tests QA-001/QA-011 [unit] (class-fix, keyed off the SHARED mechanism — not
    // free-text): every test class that consumes the DeterminismLaneFixture floor-gate
    // (IClassFixture<DeterminismLaneFixture>, or reads its BelowFloor loud-throw gate)
    // carries the excluded CI-separation trait. Keying off the actual fixture TYPE
    // (rather than a "resource floor" message fragment) means a future floor-gated test
    // in a separate file that uses the sanctioned shared gate cannot silently evade the
    // 4-vCPU exclusion. The fixture is the single floor-gate mechanism (INV-004/N2).
    [Fact]
    public void EveryDeterminismLaneFixtureConsumer_CarriesTheExcludedTrait()
    {
        var self = Path.GetFileName(GetSelfPath());
        var consumers = Directory
            .EnumerateFiles(SpikePaths.P("tests", "SpikeTests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != self)
            .Where(f =>
            {
                var t = File.ReadAllText(f);
                return t.Contains("IClassFixture<DeterminismLaneFixture>", StringComparison.Ordinal)
                       || t.Contains(".BelowFloor", StringComparison.Ordinal);
            })
            .ToList();

        // The floor-gate mechanism is actually consumed (the guard is not vacuous).
        Assert.NotEmpty(consumers);

        foreach (var file in consumers)
        {
            Assert.True(Regex.IsMatch(File.ReadAllText(file), @"\[Trait\(\s*""Category""\s*,\s*""determinism-lane""\s*\)\]"),
                $"{Path.GetFileName(file)} consumes the DeterminismLaneFixture floor-gate (a >= 8-core loud-throw skip) "
                + "but does NOT carry [Trait(\"Category\", \"determinism-lane\")] — it would go RED on the 4-vCPU "
                + "general gate. Pair the shared floor gate with the excluded CI-separation trait (QA-001/QA-011).");
        }
    }

    private static string GetSelfPath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
