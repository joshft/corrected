using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-014: the documented command provably runs a non-zero test set with an
/// out-of-suite executed-count guard; the &lt;AGGREGATOR&gt; + &lt;GATE-SCRIPT&gt;
/// constants; the TRX guard self-tests; the FIVE wrapper self-test fixtures (incl.
/// the render_rc case); the doc-home verbatim byte-compare. [integration].
///
/// DECISION: the five wrapper self-tests drive the EXTRACTED gate/tools/gate-wrapper.sh
/// (identical combined-exit logic), not the enclosing gate/run-readiness-gate.sh,
/// reconciling EXT7-02 (script-level wrapper self-tests) with INV-017 ("no in-suite
/// xUnit test ever executes <GATE-SCRIPT>"). The doc-home tests COMPARE BYTES only.
/// </summary>
[Collection("Subprocess")]
public class Inv014DocumentedCommandTests
{
    private static (int Code, string Stdout, string Stderr) RunBash(string script, string args, IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo("bash", $"\"{script}\" {args}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (env is not null)
        {
            foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
        }
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        string e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, o, e);
    }

    // Tests INV-014 [integration]: <GATE-SCRIPT> is gate/run-readiness-gate.sh and it
    // bakes in the <AGGREGATOR> + the trx logger argv (so the counted-execution
    // assertion sees the same argv). Genuine guard over the committed script.
    [Fact]
    public void Gate_script_bakes_in_aggregator_and_trx_logger()
    {
        string script = File.ReadAllText(TestPaths.RepoFile("gate", "run-readiness-gate.sh"));
        Assert.Contains("gate/Corrected.Gate.slnx", script);
        Assert.Contains("--logger", script);
        Assert.Contains("gate.trx", script);
    }

    // Tests INV-014 [integration]: TRX guard self-test — zero-discovery -> guard exits
    // NON-ZERO. Runs the committed synthetic TRX fixture through the out-of-suite guard.
    [Fact]
    public void Trx_guard_fails_on_zero_discovery()
    {
        string guard = TestPaths.RepoFile("gate", "tools", "trx-guard.sh");
        string trx = TestPaths.Fixture("trx", "zero-discovery.trx");
        var (code, _, _) = RunBash(guard, $"\"{trx}\"");
        Assert.NotEqual(0, code);
    }

    // Tests INV-014 [integration]: TRX guard self-test — below-floor -> NON-ZERO.
    [Fact]
    public void Trx_guard_fails_on_below_floor()
    {
        string guard = TestPaths.RepoFile("gate", "tools", "trx-guard.sh");
        string trx = TestPaths.Fixture("trx", "below-floor.trx");
        var (code, _, _) = RunBash(guard, $"\"{trx}\"");
        Assert.NotEqual(0, code);
    }

    // Tests INV-014 [integration]: TRX guard self-test — happy -> guard exits ZERO.
    // RED: the stub guard always exits non-zero.
    [Fact]
    public void Trx_guard_passes_on_happy()
    {
        string guard = TestPaths.RepoFile("gate", "tools", "trx-guard.sh");
        string trx = TestPaths.Fixture("trx", "happy.trx");
        var (code, _, _) = RunBash(guard, $"\"{trx}\"");
        Assert.Equal(0, code);
    }

    // ---- The FIVE wrapper self-test fixtures (EXT7-02 + EXT8-01) ----
    // Each drives the extracted gate-wrapper.sh over a stubbed dotnet test + TRX.

    private static IDictionary<string, string> WrapperEnv(int testRc, string trxFixture, bool renderNonzero = false)
        => new Dictionary<string, string>
        {
            ["GATE_TEST_RC"] = testRc.ToString(),
            ["GATE_TRX_PATH"] = TestPaths.Fixture("trx", trxFixture),
            // B2 fix: the two arms are now DISTINCT. renderNonzero:true points at the
            // permanent always-non-zero double (render-status.fail.sh) so fixture 4
            // forces render_rc!=0 with test_rc==trx_rc==0; the false arm uses the REAL
            // renderer (render-status.sh) which GREEN makes exit 0 on the green path.
            // Previously BOTH arms named render-status.sh, so fixtures 4 and 5 passed
            // identical env with contradictory assertions — unsatisfiable (EXT8-01).
            ["GATE_RENDERER"] = renderNonzero
                ? TestPaths.RepoFile("gate", "tools", "render-status.fail.sh")
                : TestPaths.RepoFile("gate", "tools", "render-status.sh"),
        };

    // Tests INV-014 [integration]: wrapper fixture 1 — nonzero-test + valid-TRX ->
    // script exits NON-ZERO AND renders the FAIL text.
    [Fact]
    public void Wrapper_nonzero_test_valid_trx_fails_and_renders_fail()
    {
        string w = TestPaths.RepoFile("gate", "tools", "gate-wrapper.sh");
        var (code, stdout, _) = RunBash(w, "", WrapperEnv(1, "happy.trx"));
        Assert.NotEqual(0, code);
        Assert.Contains("FAIL", stdout);
    }

    // Tests INV-014 [integration]: wrapper fixture 2 — zero-test + bad-TRX
    // (zero-discovery) -> NON-ZERO AND renders FAIL.
    [Fact]
    public void Wrapper_zero_test_bad_trx_fails_and_renders_fail()
    {
        string w = TestPaths.RepoFile("gate", "tools", "gate-wrapper.sh");
        var (code, stdout, _) = RunBash(w, "", WrapperEnv(0, "zero-discovery.trx"));
        Assert.NotEqual(0, code);
        Assert.Contains("FAIL", stdout);
    }

    // Tests INV-014 [integration]: wrapper fixture 3 — missing-TRX -> NON-ZERO AND FAIL.
    [Fact]
    public void Wrapper_missing_trx_fails_and_renders_fail()
    {
        string w = TestPaths.RepoFile("gate", "tools", "gate-wrapper.sh");
        var env = WrapperEnv(0, "does-not-exist.trx");
        var (code, stdout, _) = RunBash(w, "", env);
        Assert.NotEqual(0, code);
        Assert.Contains("FAIL", stdout);
    }

    // Tests INV-014 [integration]: wrapper fixture 4 — RENDERER-NONZERO (test_rc==0 +
    // valid-TRX but the renderer step exits non-zero, via render-status.fail.sh) ->
    // script exits NON-ZERO AND a SHELL-OWNED fallback FAIL line is emitted even though
    // the renderer could not (EXT8-01 — forces the render_rc term, which fixtures 1-3
    // left unenforced). The fallback FAIL is the WRAPPER's own: the fail-renderer emits
    // no "FAIL"/"PASS", so a PASS banner must NOT appear and a FAIL line MUST.
    [Fact]
    public void Wrapper_renderer_nonzero_fails_with_shell_owned_fallback_fail()
    {
        string w = TestPaths.RepoFile("gate", "tools", "gate-wrapper.sh");
        var (code, stdout, _) = RunBash(w, "", WrapperEnv(0, "happy.trx", renderNonzero: true));
        Assert.NotEqual(0, code);
        Assert.Contains("FAIL", stdout);        // shell-owned fallback (renderer could not)
        Assert.DoesNotContain("PASS", stdout);  // the green banner must NOT be emitted
    }

    // Tests INV-014 [integration]: wrapper fixture 5 — happy (zero-test + valid-TRX +
    // renderer zero) -> exits ZERO AND renders the PASS-BLOCKED banner. RED against
    // the stub wrapper (exits non-zero, no banner).
    [Fact]
    public void Wrapper_happy_exits_zero_and_renders_pass_banner()
    {
        string w = TestPaths.RepoFile("gate", "tools", "gate-wrapper.sh");
        var (code, stdout, _) = RunBash(w, "", WrapperEnv(0, "happy.trx"));
        Assert.Equal(0, code);
        Assert.Contains("PASS", stdout);
    }

    // DECISION: the canonical operator/CI command byte-string is `bash
    // gate/run-readiness-gate.sh` — the exact argv the reference-CI lane and the
    // task's test command both run from the repo root (AP-020 verbatim form). GREEN's
    // doc-home fenced block must contain EXACTLY this (spec: the fenced command is
    // <GATE-SCRIPT>, "NOT bare dotnet test"). Basis: INV-014/INV-017 (EXT6-01) +
    // run-readiness-gate.sh header (the runnable canonical gate command).
    private const string CanonicalGateCommand = "bash gate/run-readiness-gate.sh";
    private const string RunningSectionHeading = "## Running the readiness gate";

    // Extracts the inner text of the FIRST fenced code block that follows the given
    // heading, up to (but not including) the next `## ` heading. Returns null if the
    // section or a fenced block is absent. This is a real PARSE — not a header-only
    // grep, not a Contains() over the whole file (B3/AP-020).
    private static string? ExtractFencedCommand(string markdown, string heading)
    {
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        int h = Array.FindIndex(lines, l => l.TrimEnd() == heading);
        if (h < 0) return null;
        int open = -1;
        for (int i = h + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal)) return null; // next section, no fence
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)) { open = i; break; }
        }
        if (open < 0) return null;
        var sb = new StringBuilder();
        for (int i = open + 1; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                return sb.ToString().Trim();
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(lines[i]);
        }
        return null; // unterminated fence
    }

    // Tests INV-014 [integration]: the doc-home fenced `## Running the readiness gate`
    // section documents <GATE-SCRIPT> as the command. B3 fix: PARSE the fenced command
    // and BYTE-COMPARE it to the canonical `bash gate/run-readiness-gate.sh` (never a
    // header-only Contains grep, never bare `dotnet test` — the PMB-001/AP-020 verbatim
    // requirement). Each present doc home is compared; at least one must carry the
    // section. The AP-020 test COMPARES BYTES (never executes — INV-017). RED at Stage
    // A: no doc home carries the section yet.
    [Fact]
    public void Doc_home_running_section_holds_the_gate_script_command()
    {
        string[] docHomeRelPaths =
        {
            "README.md",
            ".correctless/AGENT_CONTEXT.md",
        };

        var carriedCommands = new List<string>();
        foreach (string rel in docHomeRelPaths)
        {
            string[] parts = rel.Split('/');
            if (!TestPaths.RepoFileExists(parts)) continue;
            string text = File.ReadAllText(TestPaths.RepoFile(parts));
            string? cmd = ExtractFencedCommand(text, RunningSectionHeading);
            if (cmd is null) continue;
            carriedCommands.Add(cmd);
            // Verbatim byte-compare to <GATE-SCRIPT>; reject bare dotnet test.
            Assert.Equal(CanonicalGateCommand, cmd);
            Assert.DoesNotContain("dotnet test", cmd);
        }

        Assert.True(carriedCommands.Count > 0,
            "INV-014: a doc home (README or AGENT_CONTEXT) must carry a fenced `## Running the readiness gate` section whose command byte-equals `bash gate/run-readiness-gate.sh`");
    }
}
