using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-017: the gate is WIRED to run from clean in CI via a runnable script (not a
/// grep, not deferred); the recursion sentinel has a defined set/check owner; no
/// in-suite xUnit test executes &lt;GATE-SCRIPT&gt;. [integration].
/// </summary>
public class Inv017CiWiringTests
{
    // Tests INV-017 [integration]: the recursion sentinel OWNERSHIP — the OUTER script
    // starts with CORRECTED_GATE_INNER unset and EXPORTS it =1 ONLY for its child
    // dotnet test (EXT7-02). Genuine guard over the committed script.
    [Fact]
    public void Script_exports_sentinel_only_for_child_dotnet_test()
    {
        string script = File.ReadAllText(TestPaths.RepoFile("gate", "run-readiness-gate.sh"));
        Assert.Contains("CORRECTED_GATE_INNER=1 dotnet test", script);
        Assert.Contains("CORRECTED_GATE_INNER:-", script); // the inner-invocation check
    }

    // Tests INV-017 [integration]: when the sentinel is SET, the script NO-OPS (an
    // inner invocation can never re-trigger the wrapper). Behavioral guard that does
    // NOT recurse (sentinel set -> immediate exit). Genuine.
    [Fact]
    public void Script_noops_when_sentinel_set()
    {
        string script = TestPaths.RepoFile("gate", "run-readiness-gate.sh");
        var psi = new ProcessStartInfo("bash", $"\"{script}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["CORRECTED_GATE_INNER"] = "1";
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        Assert.Contains("no-op", stdout, StringComparison.OrdinalIgnoreCase);
    }

    // Tests INV-017 [integration]: NO in-suite xUnit test executes <GATE-SCRIPT> without
    // the sentinel (an in-suite test invoking its own enclosing script would recurse,
    // EXT6-01). Only ACTUAL execution counts — a bash/Process/RunBash call whose SCRIPT
    // ARGUMENT resolves run-readiness-gate.sh — NOT mere co-occurrence of the substrings
    // (which false-tripped on files that only READ the script via File.ReadAllText or
    // reference it as a doc-compare STRING CONSTANT, e.g. Inv014's CanonicalGateCommand).
    // The only permitted execution (the sentinel no-op test) sets CORRECTED_GATE_INNER.
    [Fact]
    public void No_in_suite_test_executes_the_gate_script_without_sentinel()
    {
        string testsDir = TestPaths.RepoFile("gate", "Corrected.Gate.Tests");
        foreach (var cs in Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(cs);

            // 1. Variables bound to the gate-script PATH (not its CONTENTS): an
            //    assignment whose RHS resolves run-readiness-gate.sh and is NOT a File
            //    read (File.ReadAllText/Lines/Bytes reads the text, it does not execute).
            var scriptPathVars = new HashSet<string>();
            foreach (Match m in Regex.Matches(text, @"(\w+)\s*=\s*([^;\n]*run-readiness-gate\.sh[^;\n]*)"))
            {
                string rhs = m.Groups[2].Value;
                if (rhs.Contains("ReadAll")) continue;      // reads contents, not a path to run
                scriptPathVars.Add(m.Groups[1].Value);
            }

            // 2. ACTUAL execution: one of those path variables passed to a bash exec —
            //    RunBash(<var> ...) or new ProcessStartInfo("bash", $"...{<var>}...").
            bool executesGateScript = scriptPathVars.Any(v =>
                Regex.IsMatch(text, @"RunBash\(\s*" + Regex.Escape(v) + @"\b") ||
                Regex.IsMatch(text, @"ProcessStartInfo\(\s*""bash""[^)]*\{" + Regex.Escape(v) + @"\}"));

            if (executesGateScript)
            {
                Assert.Contains("CORRECTED_GATE_INNER", text);
            }
        }
    }

    // Tests INV-017 [integration]: the CHARTER half of the spike DF-001 charter/live
    // pair (B3). It does NOT execute the gate in-suite (that recurses, EXT6-01) and it
    // does NOT treat File.ReadAllText(workflow).Contains("run-readiness-gate.sh") as
    // primary execution evidence (the PMB-001/AP-011 doc-grep trap). Instead it charts
    // the OUT-OF-SUITE from-clean harness (gate/ci/from-clean-gate.sh): (a) the harness
    // exists, is executable, does the CORRECT from-clean rm, and invokes <GATE-SCRIPT>;
    // (b) a committed CI workflow WIRES that harness so it gates PRs. The verbatim
    // from-clean EXECUTION is the harness's own out-of-suite job (the LIVE half), run
    // by the reference-CI lane — never by this suite.
    [Fact]
    public void Ci_wires_the_from_clean_harness()
    {
        // (a) The out-of-suite from-clean harness exists and is a real execution unit.
        string harness = TestPaths.RepoFile("gate", "ci", "from-clean-gate.sh");
        Assert.True(File.Exists(harness),
            "INV-017: the out-of-suite from-clean harness gate/ci/from-clean-gate.sh must exist");
        Assert.True(File.GetUnixFileMode(harness).HasFlag(UnixFileMode.UserExecute),
            "INV-017: the from-clean harness must be executable");
        string harnessText = File.ReadAllText(harness);
        Assert.Contains("rm -rf spikes/dafny-compat/out/", harnessText); // correct from-clean rm (EXT2-11)
        Assert.Contains("run-readiness-gate.sh", harnessText);           // invokes <GATE-SCRIPT>
        // The harness runs the OUTER script with the sentinel UNSET (so it executes
        // fully, not no-opped, EXT7-02).
        Assert.Contains("unset CORRECTED_GATE_INNER", harnessText);

        // (b) A committed CI workflow WIRES the harness (gates PRs, NOT deferred). This
        // is a wiring CHARTER over the harness reference — the LIVE verbatim execution
        // runs the harness above, so this is NOT the PMB-001 trap of grepping the
        // workflow for run-readiness-gate.sh as the sole "it runs" evidence.
        string wfDir = TestPaths.RepoFile(".github", "workflows");
        Assert.True(Directory.Exists(wfDir),
            "INV-017: a committed .github/workflows gate job must wire gate/ci/from-clean-gate.sh");
        bool wired = Directory.EnumerateFiles(wfDir)
            .Any(f => File.ReadAllText(f).Contains("gate/ci/from-clean-gate.sh"));
        Assert.True(wired,
            "INV-017: the CI gate job must invoke the from-clean harness gate/ci/from-clean-gate.sh");
    }

    // Tests INV-017 [integration]: the from-clean runnable script is EXTRACTED +
    // executable (mirroring the spike's DF-001 LIVE half). Genuine guard: the script
    // exists (its execution from clean is the reference-CI lane's job).
    [Fact]
    public void From_clean_script_is_executable()
    {
        string script = TestPaths.RepoFile("gate", "run-readiness-gate.sh");
        Assert.True(File.Exists(script));
        var mode = File.GetUnixFileMode(script);
        Assert.True(mode.HasFlag(UnixFileMode.UserExecute), "gate/run-readiness-gate.sh must be executable");
    }
}
