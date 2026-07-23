// Tests INV-005: failing fixture is refuted by a COMPLETED solver result.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Inv005VerifyBadTests
{
    // Tests INV-005 [unit]: the committed sidecar pins the planted location.
    [Fact]
    public void BadSidecar_PlantedLocationCommitted()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("fixtures", "expected", "bad.sidecar.json"));
        var planted = doc.RootElement.GetProperty("planted_assertion");
        Assert.Equal("fixtures/bad.dfy", planted.GetProperty("file").GetString());
        Assert.Equal(8, planted.GetProperty("line").GetInt32());
        Assert.Equal("verification", doc.RootElement.GetProperty("expected_stage").GetString());
    }

    // Tests INV-005 [unit]: fixture-shape guard — bad.dfy contains exactly one
    // assert statement (the planted one), so the refutation cannot come from
    // anywhere else. (Fixture hygiene, not semantic derivation — PRH-004 governs
    // semantic facts, which come from the typed API in the integration test.)
    [Fact]
    public void BadFixture_ContainsExactlyOnePlantedAssert()
    {
        var lines = File.ReadAllLines(SpikePaths.P("fixtures", "bad.dfy"));
        var asserts = lines.Select((l, i) => (Line: i + 1, Text: l))
            .Where(t => t.Text.TrimStart().StartsWith("assert ")).ToList();
        var single = Assert.Single(asserts);
        Assert.Equal(8, single.Line);
    }

    // Tests INV-005 [integration] (P07):
    // Entry: same child-process harness invocation as INV-004, with fixtures/bad.dfy.
    // Through: real DafnyCore/Boogie/Z3; diagnostic recovery through the typed
    //   compilation-event/result API — never console text.
    // Exit: P07 records a stage=verification refutation DISTINGUISHABLE from
    //   non-completion (timeout/undetermined fails P07 — RS-018c), a recovered
    //   range CONTAINING the planted assertion's line (exact range recorded
    //   observationally — codex R2-12), and a non-empty message.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void P07_BadDfy_CompletedRefutation_AtPlantedLocation(string route)
    {
        var scratch = SpikePaths.TestScratch($"inv005-p07-{route}");
        var reportPath = Path.Combine(scratch, "p07-report.json");
        var result = Launch.Harness(route, "--probe", "P07", "--fixture", "fixtures/bad.dfy", "--out", reportPath);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);

        using var doc = Launch.Report(reportPath);
        var det = doc.RootElement.GetProperty("deterministic");
        var probe = det.GetProperty("per_probe_results").EnumerateArray()
            .Single(p => p.GetProperty("probe").GetString() == "P07");
        Assert.Equal("pass", probe.GetProperty("status").GetString());

        var refutation = det.GetProperty("node_table").EnumerateArray().First();
        Assert.Equal("verification", refutation.GetProperty("stage").GetString());
        Assert.Equal("invalid", refutation.GetProperty("outcome").GetString()); // completed solver refutation, not timeout/undetermined
        Assert.Equal("fixtures/bad.dfy", refutation.GetProperty("file").GetString());
        var startLine = refutation.GetProperty("range_start_line").GetInt32();
        var endLine = refutation.GetProperty("range_end_line").GetInt32();
        Assert.True(startLine <= 8 && 8 <= endLine,
            $"reported range [{startLine},{endLine}] does not contain the planted line 8");
        Assert.False(string.IsNullOrWhiteSpace(refutation.GetProperty("message").GetString()));
    }

    // Tests INV-005 [unit] (RS-018c): a timeout/undetermined typed outcome must
    // not satisfy the refutation predicate — encoded against the contracts enum
    // so GREEN's P07 predicate has the distinction available.
    [Fact]
    public void SolverOutcomeEnum_DistinguishesRefutationFromNonCompletion()
    {
        Assert.NotEqual(SolverOutcome.Invalid, SolverOutcome.TimedOut);
        Assert.NotEqual(SolverOutcome.Invalid, SolverOutcome.Undetermined);
        Assert.NotEqual(SolverOutcome.Invalid, SolverOutcome.OutOfResource);
    }
}
