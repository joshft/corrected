// Tests INV-007: resolved-AST recovery demonstrates inference,
// proof-vs-executable forms, and options (P10/P11) — plus the OQ-003 canary.
//
// TA-B5/TA-B6 discipline: differentials are TEST-ORCHESTRATED — the test
// writes the mutated fixture copy and the random-bannered stub solver itself
// and runs ORDINARY probes; no probe name telegraphs an expected answer, and
// no expected value travels through SUT argv.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Inv007AstRecoveryTests
{
    // Tests INV-007/RS-009 [unit]: sidecar SHAPE tests, independent of fixture
    // equality — >=1 entry per required proof form, >=1 inferred-ghost=true
    // entry, exactly one expect entry classified compiled.
    [Fact]
    public void SidecarShape_ProofForms_InferredGhost_SingleCompiledExpect()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("fixtures", "expected", "classify.sidecar.json"));
        var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToList();

        foreach (var form in new[] { "assert", "calc", "loop-invariant", "decreases" })
        {
            var row = nodes.Single(n => n.GetProperty("form").GetString() == form);
            Assert.Equal("proof", row.GetProperty("classification").GetString());
            Assert.True(row.GetProperty("count").GetInt32() >= 1, $"sidecar must commit >=1 {form} proof node");
        }

        var expects = nodes.Where(n => n.GetProperty("form").GetString() == "expect").ToList();
        var expectRow = Assert.Single(expects);
        Assert.Equal("compiled", expectRow.GetProperty("classification").GetString());
        Assert.Equal(1, expectRow.GetProperty("count").GetInt32());

        var decls = doc.RootElement.GetProperty("declarations").EnumerateArray().ToList();
        Assert.Contains(decls, d => d.GetProperty("inferred_ghost").GetBoolean());
        var inferred = decls.Single(d => d.GetProperty("inferred_ghost").GetBoolean());
        Assert.False(inferred.GetProperty("explicit_ghost_keyword").GetBoolean());
        Assert.Contains(decls, d => d.GetProperty("ghost").GetBoolean() && d.GetProperty("explicit_ghost_keyword").GetBoolean());
        Assert.Contains(decls, d => !d.GetProperty("ghost").GetBoolean());
    }

    // Tests INV-007 [integration] (P10, TA-B7):
    // Entry: child-process harness invocation (as INV-004) with
    //   fixtures/classify.dfy in AST-recovery mode.
    // Through: real DafnyCore resolution; classification read from resolved-AST
    //   properties; no text/regex analysis of fixture source.
    // Exit: emitted node table equals the committed sidecar over BOTH the
    //   declaration rows (name, kind, ghost, inferred) AND the node-form rows
    //   (form, classification, count) — the four editable-proof forms and the
    //   expect contrast case are integration-asserted, not sidecar-vs-sidecar.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void P10_NodeTable_EqualsCommittedSidecar_DeclarationsAndForms(string route)
    {
        var scratch = SpikePaths.TestScratch($"inv007-p10-{route}");
        var reportPath = Path.Combine(scratch, "p10-report.json");
        var result = Launch.Harness(route, "--probe", "P10", "--fixture", "fixtures/classify.dfy", "--out", reportPath);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);

        using var report = Launch.Report(reportPath);
        using var sidecar = SpikePaths.Json(SpikePaths.P("fixtures", "expected", "classify.sidecar.json"));
        var table = ReportNodeTable(report);

        var actualDecls = DeclSet(table.GetProperty("declarations"));
        var expectedDecls = DeclSet(sidecar.RootElement.GetProperty("declarations"));
        Assert.Equal(expectedDecls, actualDecls);

        // TA-B7: form/classification/count rows integration-asserted too.
        var actualForms = FormSet(table.GetProperty("nodes"));
        var expectedForms = FormSet(sidecar.RootElement.GetProperty("nodes"));
        Assert.Equal(expectedForms, actualForms);
    }

    private static JsonElement ReportNodeTable(JsonDocument report) =>
        report.RootElement.GetProperty("deterministic").GetProperty("node_table")
            .EnumerateArray().Single(n => n.TryGetProperty("declarations", out _));

    private static List<(string, string, bool, bool)> DeclSet(JsonElement declarations) =>
        declarations.EnumerateArray()
            .Select(d => (d.GetProperty("name").GetString()!, d.GetProperty("kind").GetString()!,
                d.GetProperty("ghost").GetBoolean(), d.GetProperty("inferred_ghost").GetBoolean()))
            .OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();

    private static List<(string, string, int)> FormSet(JsonElement nodes) =>
        nodes.EnumerateArray()
            .Select(n => (n.GetProperty("form").GetString()!, n.GetProperty("classification").GetString()!,
                n.GetProperty("count").GetInt32()))
            .OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();

    // Tests INV-007 [integration] (codex F10, TA-B6): the INDEPENDENT AST
    // check runs on the ORDINARY P10 report — explicit-modifier presence from
    // the parser AST and ghostness from the resolved AST, asserted WITHOUT
    // consulting the sidecar. No dedicated telegraphing probe exists.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void IndependentAstCheck_InferredGhost_NoExplicitKeyword_ResolvedGhostTrue(string route)
    {
        var scratch = SpikePaths.TestScratch($"inv007-indep-{route}");
        var reportPath = Path.Combine(scratch, "indep-report.json");
        var result = Launch.Harness(route, "--probe", "P10", "--fixture", "fixtures/classify.dfy", "--out", reportPath);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);
        using var report = Launch.Report(reportPath);
        var rows = ReportNodeTable(report).GetProperty("declarations").EnumerateArray().ToList();
        // Constants only — deliberately NOT read from the sidecar (codex F10).
        var row = rows.Single(d => d.GetProperty("name").GetString() == "inferredGhost");
        Assert.False(row.GetProperty("explicit_ghost_keyword").GetBoolean(),
            "parser AST reports an explicit ghost keyword on the inferred declaration — the fixture premise is broken");
        Assert.True(row.GetProperty("ghost").GetBoolean(), "resolved AST does not report inferred ghostness");
        Assert.True(row.GetProperty("inferred_ghost").GetBoolean());
    }

    // Tests INV-007 [integration] (TA-B6): TEST-ORCHESTRATED mutation
    // differential — the TEST writes the ghost-keyword-mutated fixture copy and
    // runs the ORDINARY P10 probe on original and copy, comparing reported
    // flags across the two launches. A harness hardcoding per-probe expected
    // booleans cannot satisfy both runs.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void MutationDifferential_TestWrittenGhostKeywordCopy_InferredFlipsFalse(string route)
    {
        var scratch = SpikePaths.TestScratch($"inv007-mutation-{route}");

        // The TEST constructs the mutation: add `ghost ` to the inferred declaration.
        var original = File.ReadAllText(SpikePaths.P("fixtures", "classify.dfy"));
        Assert.Contains("var inferredGhost :=", original);
        var mutatedSource = original.Replace("var inferredGhost :=", "ghost var inferredGhost :=");
        var mutatedFixture = Path.Combine(scratch, "classify-mutated.dfy");
        File.WriteAllText(mutatedFixture, mutatedSource);

        var originalReport = Path.Combine(scratch, "p10-original.json");
        var mutatedReport = Path.Combine(scratch, "p10-mutated.json");
        var r1 = Launch.Harness(route, "--probe", "P10", "--fixture", "fixtures/classify.dfy", "--out", originalReport);
        Assert.Equal(ExitCodes.RouteProbesPassed, r1.ExitCode);
        var r2 = Launch.Harness(route, "--probe", "P10", "--fixture", mutatedFixture, "--out", mutatedReport);
        Assert.Equal(ExitCodes.RouteProbesPassed, r2.ExitCode);

        using var d1 = Launch.Report(originalReport);
        using var d2 = Launch.Report(mutatedReport);
        var before = ReportNodeTable(d1).GetProperty("declarations").EnumerateArray()
            .Single(d => d.GetProperty("name").GetString() == "inferredGhost");
        var after = ReportNodeTable(d2).GetProperty("declarations").EnumerateArray()
            .Single(d => d.GetProperty("name").GetString() == "inferredGhost");

        Assert.True(before.GetProperty("ghost").GetBoolean());
        Assert.True(before.GetProperty("inferred_ghost").GetBoolean());
        Assert.True(after.GetProperty("ghost").GetBoolean(), "resolved ghostness must stay true after adding the keyword");
        Assert.False(after.GetProperty("inferred_ghost").GetBoolean(),
            "inferred-ghost must flip to false when the keyword is explicit — a frozen sidecar cannot self-certify (codex F10)");
        Assert.True(after.GetProperty("explicit_ghost_keyword").GetBoolean());
    }

    // Tests INV-004/RS-009 [integration] (TA-B6): fixture-hygiene walk with a
    // NEGATIVE CONTROL — the committed impure fixture must be reported impure;
    // a probe hardcoding "pure" fails the differential.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void FixtureHygiene_PureOkDfy_AndImpureNegativeControl(string route)
    {
        var scratch = SpikePaths.TestScratch($"inv007-hygiene-{route}");

        var pureReport = Path.Combine(scratch, "hygiene-ok.json");
        var r1 = Launch.Harness(route, "--probe", "hygiene", "--fixture", "fixtures/ok.dfy", "--out", pureReport);
        Assert.Equal(ExitCodes.RouteProbesPassed, r1.ExitCode);
        using var d1 = Launch.Report(pureReport);
        var pure = d1.RootElement.GetProperty("deterministic").GetProperty("node_table").EnumerateArray().First();
        Assert.True(pure.GetProperty("all_declarations_bodied").GetBoolean());
        Assert.False(pure.GetProperty("contains_attributes").GetBoolean());
        Assert.False(pure.GetProperty("contains_assume_statements").GetBoolean());
        Assert.False(pure.GetProperty("contains_verification_suppression").GetBoolean());

        // Negative control: fixtures/impure-control.dfy has an attribute AND an assume.
        var impureReport = Path.Combine(scratch, "hygiene-impure.json");
        var r2 = Launch.Harness(route, "--probe", "hygiene", "--fixture", "fixtures/impure-control.dfy", "--out", impureReport);
        using var d2 = Launch.Report(impureReport);
        var impure = d2.RootElement.GetProperty("deterministic").GetProperty("node_table").EnumerateArray().First();
        Assert.True(impure.GetProperty("contains_attributes").GetBoolean(),
            "hygiene walk failed to detect the {:verify false} attribute in the negative control (TA-B6)");
        Assert.True(impure.GetProperty("contains_assume_statements").GetBoolean(),
            "hygiene walk failed to detect the assume statement in the negative control (TA-B6)");
    }

    // Tests INV-007/RS-018b [unit]: the frozen Option Manifest FILE (not prose)
    // is the oracle and covers every spec-named option, digest-anchored.
    [Fact]
    public void OptionManifest_FrozenFile_CoversRequiredOptions_AndIsDigestAnchored()
    {
        Assert.Equal(SpecConstants.OptionManifestSha256,
            SpikePaths.Sha256File(SpikePaths.P("manifest", "option-manifest.json")));

        using var doc = SpikePaths.Json(SpikePaths.P("manifest", "option-manifest.json"));
        var ids = doc.RootElement.GetProperty("options").EnumerateArray()
            .Select(o => o.GetProperty("id").GetString()).ToHashSet();
        foreach (var required in new[]
        {
            "--no-verify", "--filter-symbol", "--verify-included-files", "--library", "--cores",
            "--solver-path", "--verification-time-limit", "--resource-limit", "--function-syntax",
            "--random-seed", "--boogie",
        })
        {
            Assert.Contains(required, ids);
        }

        var solverPath = doc.RootElement.GetProperty("options").EnumerateArray()
            .Single(o => o.GetProperty("id").GetString() == "--solver-path").GetProperty("value").GetString();
        Assert.StartsWith("<run-root>/", solverPath);
        Assert.Equal("1", doc.RootElement.GetProperty("options").EnumerateArray()
            .Single(o => o.GetProperty("id").GetString() == "--cores").GetProperty("value").GetString());
    }

    // Tests INV-007/OQ-003 [unit] (TA-A8): the canary CONTRACT is committed
    // PER ROUTE (Route B divergence is a data change, not a test edit) with
    // source citations including the Compilation.Options alias.
    [Fact]
    public void CanaryContract_PerRouteRows_SourceVerifiedCitationsCommitted()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("manifest", "option-manifest.json"));
        var canary = doc.RootElement.GetProperty("normalization_canary");
        Assert.Equal("--solver-path", canary.GetProperty("option_id").GetString());
        var routes = canary.GetProperty("routes");
        foreach (var route in new[] { "A", "B" })
        {
            Assert.True(routes.TryGetProperty(route, out var row), $"canary contract missing per-route row for {route} (TA-A8)");
            Assert.False(string.IsNullOrEmpty(row.GetProperty("solver_version").GetString()));
            Assert.NotEmpty(row.GetProperty("added_prover_options_include").EnumerateArray().ToList());
        }
        var citations = canary.GetProperty("source_citations").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Contains(citations, c => c.Contains("Compilation.cs:56"));
        Assert.Contains(citations, c => c.Contains("TextDocumentLoader.cs:58-59"));
        Assert.Contains(citations, c => c.Contains("DafnyOptions.cs:941-946"));
        Assert.Contains(citations, c => c.Contains("BoogieOptionBag.cs:121-126"));
    }

    // Tests INV-007 [integration] (P11 + OQ-003 canary, TA-A8):
    // Entry: child-process harness after a real verification of fixtures/ok.dfy.
    // Through: post-verification readback of the SAME aliased options object;
    //   ProcessSolverOptions EXECUTES the configured binary and mutates typed
    //   state (SolverIdentifier/SolverVersion/added O: options).
    // Exit: readback matches the frozen Option Manifest's PER-ROUTE canary row.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void P11_OptionsReadback_MatchesFrozenManifest_PerRouteCanary(string route)
    {
        var scratch = SpikePaths.TestScratch($"inv007-p11-{route}");
        var reportPath = Path.Combine(scratch, "p11-report.json");
        var result = Launch.Harness(route, "--probe", "P11", "--fixture", "fixtures/ok.dfy", "--out", reportPath);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);

        using var manifest = SpikePaths.Json(SpikePaths.P("manifest", "option-manifest.json"));
        var expectedRow = manifest.RootElement.GetProperty("normalization_canary").GetProperty("routes").GetProperty(route);

        using var doc = Launch.Report(reportPath);
        var det = doc.RootElement.GetProperty("deterministic");
        var canary = det.GetProperty("normalization_canary_observations");
        Assert.Equal("--solver-path", canary.GetProperty("option_id").GetString());
        Assert.Equal("<run-root>/solver/z3-4.12.1/bin/z3", canary.GetProperty("requested_value").GetString());
        Assert.Equal(expectedRow.GetProperty("solver_identifier").GetString(), canary.GetProperty("solver_identifier").GetString());
        Assert.Equal(expectedRow.GetProperty("solver_version").GetString(), canary.GetProperty("solver_version").GetString());
        var added = canary.GetProperty("added_prover_options").EnumerateArray().Select(o => o.GetString()!).ToList();
        foreach (var expectedOpt in expectedRow.GetProperty("added_prover_options_include").EnumerateArray())
        {
            Assert.Contains(expectedOpt.GetString(), added);
        }
        Assert.StartsWith(expectedRow.GetProperty("prover_path_entry_prefix").GetString(),
            canary.GetProperty("effective_prover_path_entry").GetString());
        // Oracle files are digest-bound in class 2 (TA-B15: named partition field, not a generic bag).
        Assert.Equal(SpecConstants.OptionManifestSha256,
            det.GetProperty("oracle_file_digests").GetProperty("option_manifest").GetString());
    }

    // Tests INV-007/OQ-003 [integration] (TA-B5): the canary's BEHAVIORAL
    // DIFFERENTIAL with a TEST-GENERATED unpredictable expected value — the
    // TEST writes a stub solver whose banner reports a random version the
    // harness never sees in argv; only --solver <stub> is passed. An echoing
    // harness cannot know the value.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void CanaryDifferential_TestWrittenStubBanner_ChangesEffectiveSolverVersion(string route)
    {
        var scratch = SpikePaths.TestScratch($"inv007-canary-diff-{route}");
        var secretVersion = $"9.{Random.Shared.Next(100, 999)}.{Random.Shared.Next(100, 999)}";
        var stub = SpikePaths.WriteExecutable(Path.Combine(scratch, "stub-solver"),
            $"echo 'Z3 version {secretVersion} - 64 bit'");

        var reportPath = Path.Combine(scratch, "canary-diff-report.json");
        var result = Launch.Harness(route, "--probe", "P11", "--fixture", "fixtures/ok.dfy",
            "--solver", stub, "--out", reportPath);
        // The stub cannot verify anything, but options processing (the canary
        // observable) happens before solving; the readback must carry the
        // TEST'S secret banner version regardless of the verification outcome.
        using var doc = Launch.Report(reportPath);
        var canary = doc.RootElement.GetProperty("deterministic").GetProperty("normalization_canary_observations");
        Assert.Equal(secretVersion, canary.GetProperty("solver_version").GetString());
    }
}
