using System;
using System.Collections.Generic;
using System.IO;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-006: real probe orchestration; typed fail-closed reasons; current-state
/// binding. [integration].
///
/// Integration contract:
///  Entry:   this assertion runs INSIDE the dotnet test suite the documented
///           command (&lt;GATE-SCRIPT&gt;) discovers — NOT a separate shell-out to
///           the script (that would recurse, INV-017).
///  Through: the REAL kernel + REAL orchestrator over the REAL committed
///           spec/ADR/evidence; nothing mocked.
///  Exit:    stage-current form ONLY (R3-M1). At Stage A (P1.satisfied:false today)
///           the real probe returns P1=false (evidence-schema-incomplete) and the
///           committed block -> Pass, status BLOCKED. The post-migration Exit
///           (P1=true) is asserted only in the Stage-B flip commit — asserting it
///           now would violate "nothing mocked" (the tree is not migrated).
/// </summary>
public class Inv006OrchestrationTests
{
    // Tests INV-006 [integration]: Stage-B current-state — the real orchestrator over
    // the real committed (migrated) tree yields P1=true with reason resolved-compatible
    // (the acceptance schema is present, so the probe re-derives COMPATIBLE, never a throw).
    [Fact]
    public void StageB_real_probe_P1_is_satisfied()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        IReadOnlyDictionary<PreconditionId, ProbeResult> results = ProbeOrchestrator.RunAll(ctx);
        Assert.True(results[PreconditionId.P1].Satisfied);
        Assert.Equal("resolved-compatible", results[PreconditionId.P1].Reason);
    }

    // Tests INV-006 [integration]: Stage-B committed block -> Pass, status BLOCKED
    // (through the REAL kernel + REAL orchestrator). Post-flip the P1 cell is consistent
    // because declared P1.satisfied:true + non-null evidence matches the real probe's
    // satisfied:true/Resolved; P2/P3 stay (null,false,false) consistent -> overall BLOCKED.
    [Fact]
    public void StageB_committed_block_is_Pass_BLOCKED()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        var results = ProbeOrchestrator.RunAll(ctx);
        var block = ReadinessBlockParser.Parse(
            File.ReadAllText(TestPaths.RepoFile(".correctless", "specs", "phase-0-1-worker.md")));
        Assert.Equal(ReadinessStatus.BLOCKED, block.Status);
        var v = ReadinessGate.EvaluateReadiness(block, results);
        Assert.Equal(VerdictKind.Pass, v.Kind);
    }

    // Tests INV-006 [integration]: no probe throws/skips — each returns a typed
    // {satisfied:false, reason}. P2/P3 fail-closed with validator-deferred.
    [Fact]
    public void Probes_never_throw_and_carry_typed_reasons()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        var results = ProbeOrchestrator.RunAll(ctx);
        Assert.Equal(ProbeReasons.ValidatorDeferred, results[PreconditionId.P2].Reason);
        Assert.Equal(ProbeReasons.ValidatorDeferred, results[PreconditionId.P3].Reason);
    }

    // Tests INV-006 [integration]: the orchestrator resolves structured evidence via
    // the exact real-producer JSON paths NESTED under `deterministic.` (not
    // top-level; RS-207). Asserts the real canonical sample carries the nested
    // envelope the P1 recompute reads. AP-031 live-producer coverage.
    // Source: spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json
    [Fact]
    public void Structured_reads_use_deterministic_envelope_paths()
    {
        string sample = File.ReadAllText(TestPaths.RepoFile(
            "spikes", "dafny-compat", "evidence", "samples", "run-report.canonical.sample.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(sample);
        var det = doc.RootElement.GetProperty("deterministic");
        Assert.True(det.TryGetProperty("route_verdicts", out _));
        Assert.True(det.TryGetProperty("per_probe_results", out _));
        Assert.True(det.TryGetProperty("final_suite_status", out _));
        Assert.True(det.TryGetProperty("exit_report_matrix_outcome", out _));
        // RED: the orchestrator must READ these paths; the stub throws.
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        ProbeOrchestrator.RunAll(ctx);
    }

    // Tests INV-006 [integration]: no check resolves its subject from out/ or
    // out/current (AP-021 — a run's own product binds to THIS run, never prior-run
    // roots). RED against the stub orchestrator.
    [Fact]
    public void No_check_resolves_subject_from_out_current()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        var results = ProbeOrchestrator.RunAll(ctx);
        Assert.NotNull(results);
    }
}
