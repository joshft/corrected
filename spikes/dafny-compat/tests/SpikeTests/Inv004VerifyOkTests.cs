// Tests INV-004: provable fixture verifies end-to-end, non-vacuously.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Inv004VerifyOkTests
{
    // Tests INV-004 [unit]: the committed sidecar itself commits positive,
    // non-vacuous expectations (a zero-task sidecar would let an empty program pass).
    [Fact]
    public void OkSidecar_CommitsPositiveExpectations()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("fixtures", "expected", "ok.sidecar.json"));
        var root = doc.RootElement;
        Assert.Equal("fixtures/ok.dfy", root.GetProperty("sidecar_for").GetString());
        Assert.True(root.GetProperty("min_task_count").GetInt32() > 0, "sidecar task floor must be > 0 (AP-010)");
        var targets = root.GetProperty("expected_verification_targets").EnumerateArray().Select(t => t.GetString()).ToList();
        Assert.NotEmpty(targets);
        Assert.Equal(root.GetProperty("expected_target_count").GetInt32(), targets.Count);
        Assert.Equal(0, root.GetProperty("expected_frontend_error_count").GetInt32());
    }

    // Tests INV-004 [integration] (P06):
    // Entry: launch the built route harness executable as a child process via
    //   the allowlisted launcher (its own .deps.json/.runtimeconfig.json, built
    //   with locked restore then --no-restore) — never in-test-host invocation.
    // Through: real DafnyCore parse/resolve, real Boogie ExecutionEngine, real
    //   provisioned Z3 process; none of DafnyCore, Boogie, or Z3 mocked,
    //   stubbed, or replaced.
    // Exit: emitted evidence shows P06 pass with the exact expected target
    //   names/count, a task count >= the committed sidecar floor > 0, EVERY
    //   task outcome a solver-produced outcome enum recovered from typed result
    //   objects, zero frontend errors, and the executed-solver identity of INV-003.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void P06_OkDfy_VerifiesEndToEnd_NonVacuously(string route)
    {
        var scratch = SpikePaths.TestScratch($"inv004-p06-{route}");
        var reportPath = Path.Combine(scratch, "p06-report.json");
        var result = Launch.Harness(route, "--probe", "P06", "--fixture", "fixtures/ok.dfy", "--out", reportPath);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);

        using var sidecar = SpikePaths.Json(SpikePaths.P("fixtures", "expected", "ok.sidecar.json"));
        var expectedTargets = sidecar.RootElement.GetProperty("expected_verification_targets")
            .EnumerateArray().Select(t => t.GetString()!).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var minTasks = sidecar.RootElement.GetProperty("min_task_count").GetInt32();

        using var doc = Launch.Report(reportPath);
        var det = doc.RootElement.GetProperty("deterministic");

        // MA-ID-2: the Exit contract ("emitted evidence shows P06 pass, zero
        // frontend errors") is asserted FROM THE EMITTED EVIDENCE (mirroring
        // the Inv005 pattern) — the exit code above is a second channel, never
        // the sole one. A harness that records P06 non-pass yet exits 0 fails here.
        var p06 = det.GetProperty("per_probe_results").EnumerateArray()
            .Single(p => p.GetProperty("probe").GetString() == "P06");
        Assert.Equal("pass", p06.GetProperty("status").GetString());
        // Zero non-verification (frontend) diagnostics for ok.dfy: the node
        // table carries frontend diagnostics as stage=parse/resolution rows —
        // none may exist on the provable fixture's P06 run.
        if (det.TryGetProperty("node_table", out var nodeTable) && nodeTable.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in nodeTable.EnumerateArray())
            {
                if (row.TryGetProperty("stage", out var stage))
                {
                    Assert.Equal("verification", stage.GetString());
                }
            }
        }

        // MA-ID-1: the spec-named in-run Z3-execution observable is ASSERTED —
        // solver_resource_usage_observed must be true for fixtures/ok.dfy
        // (RS-004 fail-closed observability; the harness-side predicate enters
        // INV-013 as absent-capability when false).
        Assert.True(det.GetProperty("solver_resource_usage_observed").GetProperty("fixtures/ok.dfy").GetBoolean(),
            $"route {route}: solver_resource_usage_observed is not true for fixtures/ok.dfy — the typed surface did not prove in-run Z3 execution (INV-004/MA-ID-1)");

        var targetSets = det.GetProperty("verification_target_sets").GetProperty("fixtures/ok.dfy");
        var actualTargets = targetSets.EnumerateArray().Select(t => t.GetString()!).OrderBy(t => t, StringComparer.Ordinal).ToList();
        Assert.Equal(expectedTargets, actualTargets);

        var outcomes = det.GetProperty("solver_outcome_enums").GetProperty("fixtures/ok.dfy").EnumerateArray().ToList();
        Assert.True(outcomes.Count >= minTasks, $"task count {outcomes.Count} below committed sidecar floor {minTasks}");
        var validEnumNames = Enum.GetNames(typeof(SolverOutcome)).Select(n => n.ToLowerInvariant()).ToHashSet();
        foreach (var o in outcomes)
        {
            Assert.Contains(o.GetProperty("outcome").GetString()!.ToLowerInvariant(), validEnumNames);
            Assert.Equal("valid", o.GetProperty("outcome").GetString()!.ToLowerInvariant());
        }

        // QA-002 retarget + strengthen: executed_solver_sha256 is the recomputed
        // digest of the file at the option-manifest solver path (the executed
        // binary); the BND-002 release-asset pin lives in its own field.
        Assert.Equal(SpecConstants.Z3BinarySha256, det.GetProperty("executed_solver_sha256").GetString());
        Assert.Equal(SpecConstants.Z3Sha256, det.GetProperty("solver_archive_sha256").GetString());

        // TA-A11: emitted reports embed the manifest + schema digests (INV-009 wiring).
        Assert.Equal(SpecConstants.ProbeManifestSha256, doc.RootElement.GetProperty("probe_manifest_sha256").GetString());
        Assert.Equal(SpecConstants.EvidenceSchemaSha256, doc.RootElement.GetProperty("evidence_schema_sha256").GetString());
    }

    // NOTE (TA-B6): the AST-based fixture-hygiene test (RS-009) lives in
    // Inv007AstRecoveryTests.FixtureHygiene_PureOkDfy_AndImpureNegativeControl —
    // it pairs ok.dfy with the committed IMPURE negative-control fixture so a
    // hardcoded "pure" verdict cannot pass.

    // Tests INV-004 [unit] (RS-004 fail-closed observability): if the typed
    // surface cannot supply a required observable, the probe fails and enters
    // INV-013's state machine as UNADJUDICATED_IN_PROCESS_FAILURE
    // (absent-capability variant) — never a downgrade to console scraping.
    [Fact]
    public void AbsentRequiredObservable_EntersStateMachine_AsAbsentCapabilityVariant()
    {
        var payload = new UnadjudicatedInProcessFailure(
            UnadjudicatedVariant.AbsentRequiredCapability,
            new ProbeKey("P06", "A"),
            Stage: "verification",
            MinimalInputs: "fixtures/ok.dfy",
            TypedDiagnostic: "typed result surface lacks per-task outcome enum",
            IdentityEvidence: new IdentityEvidence("run-x", "p01", "p02", "p03", "p04"));
        var outcome = AdjudicationStateMachine.EnterInProcessFailure(payload);
        Assert.Equal(RouteState.UnadjudicatedInProcessFailure, outcome.State);
        var reason = Assert.IsType<VerdictReason.UnadjudicatedFailure>(outcome.Reason);
        Assert.Equal(UnadjudicatedVariant.AbsentRequiredCapability, reason.Payload.Variant);
    }
}
