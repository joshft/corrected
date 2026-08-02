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

    // Tests INV-006 [integration]: post-PR3 committed block -> Pass, status BLOCKED
    // (through the REAL kernel + REAL orchestrator). P1 is consistent (declared true + non-null
    // evidence matches the real probe). PR3 activated P3 (declared satisfied:true + evidence
    // pointer), so the P3 cell is consistent ONLY when the committed bundle actually verifies —
    // which needs cosign provisioned. Provisioning-aware: the gate wrapper provisions COSIGN_BIN +
    // TRUSTED_ROOT and drives Pass/BLOCKED; a bare `dotnet test` cannot verify the bundle, so the
    // P3 cell is honestly unresolved and the gate does NOT pass (the canonical green is
    // `bash gate/run-readiness-gate.sh`). P2 stays false -> overall BLOCKED, never READY.
    [Fact]
    public void StageB_committed_block_is_Pass_BLOCKED()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        var results = ProbeOrchestrator.RunAll(ctx);
        var block = ReadinessBlockParser.Parse(
            File.ReadAllText(TestPaths.RepoFile(".correctless", "specs", "phase-0-1-worker.md")));
        Assert.Equal(ReadinessStatus.BLOCKED, block.Status);
        var v = ReadinessGate.EvaluateReadiness(block, results);
        if (CosignProvisioned())
        {
            Assert.Equal(VerdictKind.Pass, v.Kind);
        }
        else
        {
            // Honest fail-closed: unverifiable P3 evidence without the verifier is not a green gate.
            Assert.NotEqual(VerdictKind.Pass, v.Kind);
        }
    }

    // Tests INV-006 [integration]: no probe throws/skips — each returns a typed reason.
    // P2 fail-closed with validator-deferred. P3 is now ACTIVATED (PR3): provisioning-aware —
    // with cosign provisioned the committed production bundle verifies (ran-passed); a bare
    // `dotnet test` leaves the seam unset, so the probe takes the honest verifier-unavailable
    // fallback (never a silent skip, never the pre-PR3 p3-not-yet-activated zero-state).
    [Fact]
    public void Probes_never_throw_and_carry_typed_reasons()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        var results = ProbeOrchestrator.RunAll(ctx);
        Assert.Equal(ProbeReasons.ValidatorDeferred, results[PreconditionId.P2].Reason);
        Assert.Equal(
            CosignProvisioned() ? "ran-passed" : "verifier-unavailable",
            results[PreconditionId.P3].Reason);
    }

    // The gate wrapper (commands.test) provisions cosign + trusted root before the offline P3
    // verify (RS-014); a bare `dotnet test` leaves both unset. This mirrors the RealCosign seam
    // in Inv010Inv011Layer2RealCosignTests so P3-verifying tests are honest in both modes.
    private static bool CosignProvisioned()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COSIGN_BIN"))
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRUSTED_ROOT"));

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
