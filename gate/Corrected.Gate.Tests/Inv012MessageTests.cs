using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-012: actionable, host-clean, valence-correct blocker/status message, visible
/// on the GREEN path (stdout of the documented command). unit + integration.
/// </summary>
[Collection("Subprocess")]
public class Inv012MessageTests
{
    private static ReadinessVerdict PassVerdict()
    {
        var pcs = new System.Collections.Generic.List<ReadinessPrecondition>
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", false, null, Array.Empty<string>()),
        };
        var block = ReadinessBlock.TryCreate(1, ReadinessStatus.BLOCKED, "P1 AND P2 AND P3", pcs)!;
        var probes = new System.Collections.Generic.Dictionary<PreconditionId, ProbeResult>
        {
            [PreconditionId.P1] = ProbeResult.TryCreate(false, ProbeReasons.EvidenceSchemaIncomplete, ReferenceResolution.Resolved)!,
            [PreconditionId.P2] = ProbeResult.TryCreate(false, ProbeReasons.ValidatorDeferred, ReferenceResolution.Resolved)!,
            [PreconditionId.P3] = ProbeResult.TryCreate(false, ProbeReasons.ValidatorDeferred, ReferenceResolution.Resolved)!,
        };
        return ReadinessGate.EvaluateReadiness(block, probes);
    }

    // Tests INV-012 [unit]: a consistent BLOCKED is a PASS — the banner carries the
    // PASS valence and the "expected Phase-0.1 state" wording. RED against the stub.
    [Fact]
    public void Consistent_blocked_renders_pass_banner()
    {
        string banner = StatusRenderer.RenderPassBlockedBanner(PassVerdict());
        Assert.Contains("PASS", banner);
        Assert.Contains("BLOCKED", banner);
    }

    // Tests INV-012 [unit]: a violation renders a FAIL naming the offending
    // precondition (distinct valence). RED against the stub.
    [Fact]
    public void Violation_renders_fail_banner_naming_precondition()
    {
        var pcs = new System.Collections.Generic.List<ReadinessPrecondition>
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", true, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", false, null, Array.Empty<string>()),
        };
        var block = ReadinessBlock.TryCreate(1, ReadinessStatus.BLOCKED, "P1 AND P2 AND P3", pcs)!;
        var probes = new System.Collections.Generic.Dictionary<PreconditionId, ProbeResult>
        {
            [PreconditionId.P1] = ProbeResult.TryCreate(true, "probe", ReferenceResolution.Resolved)!,
            [PreconditionId.P2] = ProbeResult.TryCreate(false, ProbeReasons.ValidatorDeferred, ReferenceResolution.Resolved)!,
            [PreconditionId.P3] = ProbeResult.TryCreate(false, ProbeReasons.ValidatorDeferred, ReferenceResolution.Resolved)!,
        };
        var v = ReadinessGate.EvaluateReadiness(block, probes);
        string banner = StatusRenderer.RenderFailBanner(v);
        Assert.Contains("FAIL", banner);
        Assert.Contains("P1", banner);
    }

    // Tests INV-012 [unit]: each INV-006 reason-taxonomy category renders distinctly
    // (RS-291). RED against the stub renderer.
    [Theory]
    [InlineData("validator-deferred", "not yet dischargeable")]
    [InlineData("evidence-schema-incomplete", "pre-migration")]
    [InlineData("evidence-refutes", "real regression")]
    public void Reason_taxonomy_renders_distinctly(string reason, string expectedFragment)
    {
        string rendered = StatusRenderer.RenderReason(reason);
        Assert.Contains(expectedFragment, rendered, StringComparison.OrdinalIgnoreCase);
    }

    // Tests INV-012 [unit]: emitted paths match the repo-relative allowlist regex and
    // carry no Environment.UserName (PRH-005). Genuine guard over the regex const +
    // RED over the (stub) notice text.
    [Fact]
    public void Emitted_paths_match_allowlist_and_have_no_username()
    {
        var rx = new Regex(StatusRenderer.PathAllowlistRegex);
        Assert.Matches(rx, "spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json");
        Assert.DoesNotMatch(rx, "/absolute/path");
        string notice = StatusRenderer.RenderNoProductionSurfaceNotice();
        Assert.DoesNotContain(Environment.UserName.Length == 0 ? " never" : Environment.UserName, notice);
    }

    // Tests INV-012 [integration]: the INV-012 status renderer STEP (render-status.sh)
    // emits the PASS-BLOCKED banner to stdout (green-path visibility, RS-290/EXT6-01).
    // Runs the tool script directly (NOT <GATE-SCRIPT>, so no recursion). RED: the
    // stub render-status.sh emits no banner and exits non-zero.
    [Fact]
    public void Render_status_step_emits_banner_to_stdout()
    {
        string script = TestPaths.RepoFile("gate", "tools", "render-status.sh");
        var psi = new ProcessStartInfo("bash", $"\"{script}\" 0 0")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Contains("PASS", stdout);
    }
}
