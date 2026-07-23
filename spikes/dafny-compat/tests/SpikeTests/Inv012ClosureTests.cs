// Tests INV-012: closure recovery reports reality, not the input argument.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Inv012ClosureTests
{
    // Tests INV-012 [unit] (codex R3-10): the committed sidecar pins the EXACT
    // expected target set per option value and the precise delta — merely "the
    // two sets differ" would accept fabricated sets.
    [Fact]
    public void InclSidecar_CommitsExactPerOptionTargetSets_AndPreciseDelta()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("fixtures", "expected", "incl.sidecar.json"));
        var root = doc.RootElement;

        var closure = root.GetProperty("expected_closure_repo_relative").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(2, closure.Count);
        Assert.Contains(closure, c => c.EndsWith("incl-root.dfy"));
        Assert.Contains(closure, c => c.EndsWith("incl-leaf.dfy"));

        var offTargets = root.GetProperty("per_option_value").GetProperty("verify_included_files_false")
            .GetProperty("expected_verification_targets").EnumerateArray().Select(t => t.GetString()!).ToHashSet();
        var onTargets = root.GetProperty("per_option_value").GetProperty("verify_included_files_true")
            .GetProperty("expected_verification_targets").EnumerateArray().Select(t => t.GetString()!).ToHashSet();
        var delta = root.GetProperty("expected_target_set_delta").EnumerateArray().Select(t => t.GetString()!).ToHashSet();

        Assert.NotEmpty(offTargets);
        // TEST_BUG fix #4: xUnit semantics are (expectedSuperset, actual) —
        // this asserts off ⊊ on, the R3-10 intent.
        Assert.ProperSubset(onTargets, offTargets);
        Assert.Equal(delta, onTargets.Except(offTargets).ToHashSet());
        Assert.NotEmpty(delta);
    }

    // Tests INV-012 [integration] (P12):
    // Entry: child-process harness invocation with fixtures/incl-root.dfy, once
    //   per included-files option value.
    // Through: real DafnyCore parse/resolution of the include closure.
    // Exit: reported closure = {incl-root.dfy, incl-leaf.dfy} (repo-relative)
    //   under BOTH values; verification-target sets EQUAL the committed exact
    //   expected set per option value (equality + non-emptiness + precise delta).
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void P12_ClosureConstant_TargetSetsDifferByCommittedDelta(string route)
    {
        using var sidecar = SpikePaths.Json(SpikePaths.P("fixtures", "expected", "incl.sidecar.json"));
        var expectedClosure = sidecar.RootElement.GetProperty("expected_closure_repo_relative")
            .EnumerateArray().Select(c => c.GetString()!).OrderBy(c => c, StringComparer.Ordinal).ToList();

        var actualTargetsByValue = new Dictionary<bool, List<string>>();
        foreach (var optionValue in new[] { false, true })
        {
            var scratch = SpikePaths.TestScratch($"inv012-p12-{route}-{optionValue}");
            var reportPath = Path.Combine(scratch, "report.json");
            var result = Launch.Harness(route, "--probe", "P12", "--fixture", "fixtures/incl-root.dfy",
                "--verify-included-files", optionValue.ToString().ToLowerInvariant(), "--out", reportPath);
            Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);

            using var doc = Launch.Report(reportPath);
            var det = doc.RootElement.GetProperty("deterministic");

            var closure = det.GetProperty("closure_sets").GetProperty("fixtures/incl-root.dfy")
                .EnumerateArray().Select(c => c.GetString()!).OrderBy(c => c, StringComparer.Ordinal).ToList();
            Assert.Equal(expectedClosure, closure); // identical under BOTH option values

            var targets = det.GetProperty("verification_target_sets").GetProperty("fixtures/incl-root.dfy")
                .EnumerateArray().Select(t => t.GetString()!).ToList();
            actualTargetsByValue[optionValue] = targets;

            var expectedKey = optionValue ? "verify_included_files_true" : "verify_included_files_false";
            var expectedTargets = sidecar.RootElement.GetProperty("per_option_value").GetProperty(expectedKey)
                .GetProperty("expected_verification_targets").EnumerateArray().Select(t => t.GetString()!)
                .OrderBy(t => t, StringComparer.Ordinal).ToList();
            Assert.Equal(expectedTargets, targets.OrderBy(t => t, StringComparer.Ordinal).ToList());
        }

        var expectedDelta = sidecar.RootElement.GetProperty("expected_target_set_delta")
            .EnumerateArray().Select(t => t.GetString()!).ToHashSet();
        Assert.Equal(expectedDelta, actualTargetsByValue[true].Except(actualTargetsByValue[false]).ToHashSet());
    }

    // Tests INV-012 [integration]: single-file fixtures report a ONE-element
    // closure derived from the resolver, not an echo of the CLI argument.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void SingleFileFixture_OneElementClosure_FromResolver(string route)
    {
        var scratch = SpikePaths.TestScratch($"inv012-single-{route}");
        var reportPath = Path.Combine(scratch, "report.json");
        var result = Launch.Harness(route, "--probe", "closure-single", "--fixture", "fixtures/ok.dfy", "--out", reportPath);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);
        using var doc = Launch.Report(reportPath);
        var closure = doc.RootElement.GetProperty("deterministic").GetProperty("closure_sets")
            .GetProperty("fixtures/ok.dfy").EnumerateArray().Select(c => c.GetString()!).ToList();
        var single = Assert.Single(closure);
        Assert.EndsWith("fixtures/ok.dfy", single);
        // TA-A6: no self-labeled origin=="resolver" assertion — a self-label is
        // dead-code fakeable. The proof that closure recovery is resolver-derived
        // (not argument echo) is the include-pair differential above: an echo of
        // the CLI argument could never produce the two-element closure for
        // incl-root.dfy under both option values.
    }
}
