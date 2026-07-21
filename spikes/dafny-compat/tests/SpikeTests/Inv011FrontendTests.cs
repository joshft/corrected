// Tests INV-011: frontend failures are staged, typed, and solver-free.
//
// TA-B1 discipline: "no solver launch" is asserted from the TEST-owned ledger
// FILE (pre-created by the test at count zero), never from harness-reported
// counters; the stale-nonce case is constructed by the TEST writing the stale
// entry itself.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Inv011FrontendTests
{
    // Tests INV-011 [unit]: the stage-typing derivation rule is committed
    // beside the schema so the assertion is reviewable.
    [Fact]
    public void StageTypingDerivationRule_CommittedBesideSchema()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("schema", "evidence-schema.json"));
        var rule = doc.RootElement.GetProperty("stage_typing_derivation_rule").GetString()!;
        Assert.Contains("parse", rule);
        Assert.Contains("resolution", rule);
        Assert.Contains("verification", rule);
    }

    // Tests INV-011 [integration] (P08):
    // Entry: child-process harness with fixtures/syntax-error.dfy, sentinel-
    //   configured, --run-root = test-owned root with a TEST-created ledger.
    // Through: real DafnyCore parse; typed diagnostic recovery.
    // Exit: stage=parse typed diagnostic with exact location; zero verification
    //   targets in the report; ZERO entries for this test's nonce in the ledger
    //   FILE read by the test.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void P08_SyntaxError_StageParse_ZeroTargets_ZeroSolverInvocations(string route)
    {
        AssertFrontendProbe(route, "P08", "fixtures/syntax-error.dfy", "parse", expectedLine: 4);
    }

    // Tests INV-011 [integration] (P09): stage=resolution analogue.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void P09_ResolveError_StageResolution_ZeroTargets_ZeroSolverInvocations(string route)
    {
        AssertFrontendProbe(route, "P09", "fixtures/resolve-error.dfy", "resolution", expectedLine: 5);
    }

    private static void AssertFrontendProbe(string route, string probeId, string fixture, string expectedStage, int expectedLine)
    {
        var runRoot = SpikePaths.TestScratch($"inv011-{probeId}-{route}");
        var nonce = $"nonce-{Guid.NewGuid():N}";
        Inv003SolverIdentityTests.WriteLedger(runRoot, nonce);
        var reportPath = Path.Combine(runRoot, "report.json");

        var result = Launch.Harness(route, "--probe", probeId, "--fixture", fixture, "--run-root", runRoot, "--out", reportPath);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);

        using var doc = Launch.Report(reportPath);
        var det = doc.RootElement.GetProperty("deterministic");

        var diag = det.GetProperty("node_table").EnumerateArray().First();
        Assert.Equal(expectedStage, diag.GetProperty("stage").GetString());
        Assert.Equal(fixture, diag.GetProperty("file").GetString());
        Assert.Equal(expectedLine, diag.GetProperty("line").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(diag.GetProperty("message").GetString()));

        // Zero verification targets — a parse error must not surface as an empty program that "verifies".
        Assert.Equal(0, det.GetProperty("verification_target_sets").GetProperty(fixture).GetArrayLength());

        // "No solver launch" defined observably (RS-004b, TA-B1): the TEST reads
        // the ledger FILE it pre-created and counts entries for its own nonce.
        using var ledger = Launch.Ledger(runRoot);
        Assert.Equal(nonce, ledger.RootElement.GetProperty("nonce").GetString());
        var invocations = ledger.RootElement.GetProperty("entries").EnumerateArray()
            .Count(e => e.GetProperty("nonce").GetString() == nonce);
        Assert.True(invocations == 0, $"sentinel LEDGER FILE records {invocations} solver invocations despite a frontend failure (INV-011)");

        // The effective solver path recorded is the sentinel stub — the variance
        // from the Option Manifest is explicit and reviewable.
        Assert.Contains("sentinel", det.GetProperty("option_manifest_readback").GetProperty("--solver-path").GetString());
    }

    // Tests INV-011/INV-003 [integration] (RS-003d, TA-B1): stale-nonce — the
    // TEST writes a stale foreign-nonce entry into the ledger BEFORE the run.
    // P05's pass must rest on fresh entries under THIS test's nonce; the test
    // verifies from the FILE that the stale entry alone would not satisfy it.
    [Fact]
    public void SentinelLedger_TestWrittenStaleEntry_CannotSatisfyThisRun()
    {
        var runRoot = SpikePaths.TestScratch("inv011-stale-nonce");
        var nonce = $"nonce-{Guid.NewGuid():N}";
        var ledgerPath = Path.Combine(runRoot, RunLayout.SentinelLedgerRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);
        // The TEST constructs the stale state: one pre-existing entry under a foreign nonce.
        File.WriteAllText(ledgerPath,
            $"{{ \"nonce\": \"{nonce}\", \"entries\": [ {{ \"nonce\": \"nonce-STALE-PRIOR-RUN\", \"argv\": [\"stale\"] }} ] }}\n");

        var reportPath = Path.Combine(runRoot, "report.json");
        var result = Launch.Harness("A", "--probe", "P05", "--run-root", runRoot, "--out", reportPath);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);

        using var ledger = Launch.Ledger(runRoot);
        var fresh = ledger.RootElement.GetProperty("entries").EnumerateArray()
            .Count(e => e.GetProperty("nonce").GetString() == nonce);
        Assert.True(fresh >= 1,
            "P05 passed with zero fresh-nonce ledger entries — it was satisfied by the TEST-planted stale recording (RS-003d violation)");
    }
}
