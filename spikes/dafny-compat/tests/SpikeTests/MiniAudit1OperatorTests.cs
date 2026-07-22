// Mini-audit round 1 (2026-07-22) — OPERATOR-HARDENING slice additive tests
// (scripts/docs/config; separate from Agent 1's INTEGRITY-CORE MiniAudit1Tests).
// Each behavioral test FAILS when its fix is reverted (QA-022 discipline):
//   MA-HI-1  — an inherited DOTNET_ROOT cannot hijack the toolchain
//   MA-UX-1/MA-RB-4 — operator SIGTERM stops the run (no orphan, typed report)
//   MA-UX-2/MA-RB-1 — clean-runs.sh keeps last N, never out/current
//   MA-UX-5/MA-RB-2 — package cache with no completion marker is ignored
//   PR-004  — a corrupt run-config is a FAST typed refusal, not a slow default
//   PR-005  — a duplicate config key resolves LAST-wins, not first
//   MA-ED-3 — no class-2 subtree value carries run_id/run_directory/a stray root
// plus source-/doc-anchored locks for the remaining shell/doc findings.
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

[Collection(SharedStateMutatingCollection.Name)]
public class MiniAudit1OperatorTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);
    private const int SIGTERM = 15;

    private static long Uptime() =>
        (long)double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0],
            System.Globalization.CultureInfo.InvariantCulture);

    // Tests PRH-004/TB-004/MA-HI-1 [integration]: an inherited DOTNET_ROOT (the
    // ambient toolchain-hijack vector — a version-spoofing wrapper would void the
    // AP-015 pin) cannot select the SDK. The clean-environment re-exec strips it,
    // so a planted fake `dotnet` is NEVER executed (its marker file never
    // appears). On reversion (no re-exec / DOTNET_ROOT honored) resolve_sdk picks
    // the fake and the first `dotnet` launch creates the marker → test fails.
    [Fact]
    public void Hi1_InheritedDotnetRoot_CannotHijackToolchain()
    {
        var scratch = SpikePaths.TestScratch("ma1-hi1-dotnetroot");
        var marker = Path.Combine(scratch, "HIJACK-MARKER");
        var fakeSdk = Path.Combine(scratch, "fakesdk");
        SpikePaths.WriteExecutable(Path.Combine(fakeSdk, "dotnet"),
            $"echo \"HIJACKED $$\" >> \"{marker}\"\n" +
            "if [ \"$1\" = \"--version\" ]; then echo \"99.99.99-fake\"; fi\n" +
            "exit 0");

        var runRoot = Path.Combine(scratch, "rr");
        var report = Path.Combine(runRoot, "run-report.json");
        var env = new Dictionary<string, string>
        {
            ["DOTNET_ROOT"] = fakeSdk,
            ["SPIKE_PARENT_DEADLINE"] = (Uptime() + 12).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var result = Launch.Script("scripts/run-spike.sh", env, "--run-root", runRoot, "--out", report);

        Assert.False(File.Exists(marker),
            "an inherited DOTNET_ROOT selected the SDK — the clean-environment contract failed to strip it (MA-HI-1). "
            + $"marker={marker} stderr(tail)={Tail(result.StdErr)}");
    }

    // Tests INV-014/RS-015/MA-UX-1/MA-RB-4 [integration]: an operator SIGTERM
    // mid-run STOPS the run — the outer watchdog sweeps the inner session (no
    // survivor), writes a typed OperatorCancelled synthetic INCOMPLETE, exits
    // nonzero, and leaves out/current untouched. On reversion (no trap) the
    // detached inner keeps running to the wall bound and this test's survivor
    // sweep fails.
    [Fact]
    public void SignalTrap_OperatorSigterm_SweepsInner_TypedReport_OutCurrentUntouched()
    {
        var runRoot = SpikePaths.TestScratch("ma1-signal");
        var report = Path.Combine(runRoot, "run-report.json");
        var currentPointer = SpikePaths.P("out", "current", "spike.runsettings");
        var currentBefore = File.Exists(currentPointer) ? File.ReadAllText(currentPointer) : null;

        var script = SpikePaths.P("scripts", "run-spike.sh");
        var psi = new System.Diagnostics.ProcessStartInfo("bash")
        {
            WorkingDirectory = SpikePaths.SpikeRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment.Clear();
        psi.Environment["PATH"] = "/usr/bin:/bin";
        // Safety net: if the trap were somehow missed, the inner supervisor
        // self-terminates the run at this deadline (we signal well before it).
        psi.Environment["SPIKE_PARENT_DEADLINE"] = (Uptime() + 70).ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var a in new[] { "-p", script, "--run-root", runRoot, "--out", report })
        {
            psi.ArgumentList.Add(a);
        }
        var stderr = new System.Text.StringBuilder();
        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (stderr) { stderr.AppendLine(e.Data); } } };
        proc.OutputDataReceived += (_, _) => { };
        try
        {
            proc.Start();
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();

            // Wait until the inner controller is genuinely running (its pid file
            // lands FIRST, before provisioning), then give it a few seconds to be
            // inside a real phase (restore/build) before cancelling.
            var innerPid = Path.Combine(runRoot, ".inner-pid");
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!File.Exists(innerPid) && DateTime.UtcNow < deadline)
            {
                Assert.False(proc.HasExited, $"controller exited before starting the inner: {Tail(stderr.ToString())}");
                Thread.Sleep(200);
            }
            Assert.True(File.Exists(innerPid), "inner controller never started (no .inner-pid file)");
            Thread.Sleep(8000);
            Assert.False(proc.HasExited, "controller completed before it could be cancelled — the run was unexpectedly fast");

            Assert.Equal(0, kill(proc.Id, SIGTERM));
            Assert.True(proc.WaitForExit(120_000), "controller did not exit after SIGTERM — the trap did not fire (MA-UX-1)");
            Assert.NotEqual(0, proc.ExitCode);

            Assert.True(File.Exists(report), "no synthetic report after operator cancel (MA-UX-1/AP-009)");
            var text = File.ReadAllText(report);
            Assert.Contains("OperatorCancelled", text);
            Assert.Contains("INCOMPLETE", text);

            // No process of the killed inner session survives (poll a few seconds).
            var survived = WaitForNoSurvivorReferencing(runRoot, TimeSpan.FromSeconds(20));
            Assert.True(survived.Count == 0,
                "inner-session processes survived the operator cancel (MA-UX-1/MA-RB-4): " + string.Join("; ", survived));

            // out/current is published only after the suite phase, which a
            // cancelled variance run never reaches — the shared pointer is untouched.
            var currentAfter = File.Exists(currentPointer) ? File.ReadAllText(currentPointer) : null;
            Assert.Equal(currentBefore, currentAfter);
        }
        finally
        {
            foreach (var pid in SurvivorsReferencing(runRoot)) { try { kill(pid, 9); } catch { /* best effort */ } }
            if (!proc.HasExited) { try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ } }
        }
    }

    // Tests INV-014/DD-008/MA-UX-2/MA-RB-1 [integration]: clean-runs.sh prunes
    // old run/regen roots keeping the newest N, and NEVER out/current or
    // out/cache. Operates on an ISOLATED --out-dir (never the shared out/, whose
    // live run root — the test host's own CWD during a canonical suite run — must
    // not be pruned). Fast: planted dirs only, no controller run.
    [Fact]
    public void CleanRuns_KeepsLastN_NeverOutCurrentOrCache()
    {
        var scratch = SpikePaths.TestScratch("ma1-cleanruns");
        var outDir = Path.Combine(scratch, "out");
        Directory.CreateDirectory(outDir);
        var planted = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var d = Path.Combine(outDir, $"runid-clean{i:D2}");
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "marker"), "x");
            File.SetLastWriteTimeUtc(d, DateTime.UtcNow.AddMinutes(-i)); // newest = i=0
            planted.Add(d);
        }
        var currentDir = Path.Combine(outDir, "current");
        var cacheDir = Path.Combine(outDir, "cache");
        Directory.CreateDirectory(currentDir);
        Directory.CreateDirectory(cacheDir);
        var cacheSentinel = Path.Combine(cacheDir, "keep-me");
        File.WriteAllText(cacheSentinel, "keep");

        var result = Launch.Script("scripts/clean-runs.sh", null, "--out-dir", outDir, "--keep", "2");
        Assert.True(result.ExitCode == 0, $"clean-runs failed: {Tail(result.StdErr)}");

        var survivingPlanted = planted.Where(Directory.Exists).ToList();
        Assert.True(survivingPlanted.Count == 2,
            $"clean-runs kept {survivingPlanted.Count} roots, expected 2 (--keep 2): {string.Join(", ", survivingPlanted.Select(Path.GetFileName))}");
        Assert.Contains(planted[0], survivingPlanted); // the two newest survive
        Assert.Contains(planted[1], survivingPlanted);
        Assert.True(Directory.Exists(currentDir), "clean-runs deleted out/current (forbidden)");
        Assert.True(File.Exists(cacheSentinel), "clean-runs deleted out/cache (forbidden)");
    }

    // Tests INV-014/AP-016/MA-UX-5/MA-RB-2 [unit] (source-anchored — the shared
    // out/cache is live during a canonical suite run and must NOT be mutated by
    // a test): the consume path trusts the cache ONLY with the completion marker,
    // the seed stages then swaps then writes the marker LAST, and the recovery is
    // documented. Reversion-failing if the marker guard or atomic staging is
    // removed.
    [Fact]
    public void PkgCache_ConsumeGuardsMarker_SeedIsAtomic_RecoveryDocumented()
    {
        var script = File.ReadAllText(SpikePaths.P("scripts", "run-spike.sh"));
        Assert.Contains("CACHE_MARKER=\"$CACHE_DIR/packages.complete\"", script);
        // Consume only with the marker present — via the shared guard predicate
        // (MA-ID-R2-1: production consume + the cache-consume test phase share
        // shared_cache_is_trusted so there is no drift).
        Assert.Contains("if shared_cache_is_trusted \"$CACHE_DIR\"; then", script);
        Assert.Contains("has no completion marker", script);
        // Seed: staged dir, then the marker written LAST (after the mv -T
        // no-nesting swap) inside publish_cache_staged (MA-RB-R2-3).
        Assert.Contains("CACHE_STAGED=\"$CACHE_DIR/packages.staged.$$\"", script);
        var markerWriteIdx = script.IndexOf("run_cmd mv -- \"$marker.tmp\" \"$marker\"", StringComparison.Ordinal);
        var swapIdx = script.IndexOf("run_cmd mv -T -- \"$staged\" \"$pkgs\"", StringComparison.Ordinal);
        Assert.True(swapIdx >= 0 && markerWriteIdx > swapIdx,
            "the completion marker must be written AFTER the staged cache is swapped into place with mv -T (MA-UX-5/MA-RB-2/MA-RB-R2-3)");
        Assert.Contains("recover with: rm -rf out/cache", script);
    }

    // Tests PR-004 [integration]: a corrupt/truncated run-config is a FAST typed
    // startup refusal (exit 20, no slow fall-open to the 1800s default and a
    // >9-min end-of-run suite failure). On reversion config_int falls open and
    // the run proceeds (slow, exit 0) → the fast-exit assertion fails.
    [Fact]
    public void Pr004_CorruptRunConfig_FastTypedRefusal()
    {
        var scratch = SpikePaths.TestScratch("ma1-pr004");
        var corrupt = Path.Combine(scratch, "run-config.json");
        // Valid prefix, but the closing brace is missing (truncated) — not a
        // JSON object. config_int would still read the value and fall open.
        File.WriteAllText(corrupt, "{\n  \"wall_clock_bound_seconds\": 1800,\n  \"monotonic_source\": \"/proc/uptime\"\n");
        var report = Path.Combine(scratch, "run-report.json");
        var result = Launch.Script("scripts/run-spike.sh", null, "--config", corrupt, "--run-root", scratch, "--out", report);

        Assert.True(result.ExitCode == 20, $"expected exit 20, got {result.ExitCode}: {Tail(result.StdErr)}");
        Assert.True(result.DurationMs < 60_000,
            $"corrupt-config refusal took {result.DurationMs}ms — it fell open to the default and failed slow (PR-004 reverted)");
        Assert.Contains("PR-004", result.StdErr);
    }

    // Tests PR-005 [integration]: a duplicate JSON key resolves LAST-wins (JSON
    // semantics), not first. First value 1800 (long), last value 4 (short): a
    // correct last-wins bound kills the run at ~4s; a reverted first-wins bound
    // runs the full variance pipeline and exits 0.
    [Fact]
    public void Pr005_DuplicateConfigKey_LastWins()
    {
        var scratch = SpikePaths.TestScratch("ma1-pr005");
        var cfg = Path.Combine(scratch, "run-config.json");
        File.WriteAllText(cfg,
            "{\n  \"wall_clock_bound_seconds\": 1800,\n  \"wall_clock_bound_seconds\": 4,\n  \"monotonic_source\": \"/proc/uptime\"\n}\n");
        var report = Path.Combine(scratch, "run-report.json");
        var result = Launch.Script("scripts/run-spike.sh", null, "--config", cfg, "--run-root", scratch, "--out", report);

        Assert.True(result.ExitCode != 0, $"expected nonzero exit, got {result.ExitCode}: {Tail(result.StdErr)}");
        Assert.True(result.DurationMs < 90_000,
            $"run took {result.DurationMs}ms — a duplicate key resolved first-wins (1800s) instead of last-wins (4s) (PR-005 reverted)");
        Assert.True(File.Exists(report), "no synthetic report on the wall-clock kill");
        Assert.Contains("wall-clock", File.ReadAllText(report));
    }

    // Tests INV-009/MA-ED-3 [integration] (producer-side): no class-2 subtree
    // value of a real route report carries the run_id, the run_directory string,
    // or a rooted host path — the "run_id appears nowhere in the projection"
    // guarantee is checked on the PRODUCER's raw artifact, not laundered by the
    // consumer's projection scrub. Reads the substrate run's route reports.
    [Fact]
    public void Ed3_RawRouteReport_Class2_HasNoRunIdOrStrayRoots()
    {
        var runRoot = RunContext.RunRoot();
        var runId = RunContext.RunId();
        foreach (var route in new[] { "route-a.json", "route-b.json" })
        {
            using var doc = Launch.Report(Path.Combine(runRoot, "reports", route));
            var det = doc.RootElement.GetProperty("deterministic").GetRawText();
            // The run_id, the run_directory key, and any CONCRETE rooted path
            // (the run root or the spike/repo root) are binding-class only —
            // none may appear in the class-2 (deterministic) subtree of the
            // PRODUCER's raw report, checked here rather than laundered by the
            // consumer's projection scrub (MA-ED-3).
            Assert.DoesNotContain(runId, det);
            Assert.DoesNotContain("run_directory", det);
            Assert.DoesNotContain(runRoot, det);
            Assert.DoesNotContain(SpikePaths.SpikeRoot, det);
        }
    }

    // --- helpers ---------------------------------------------------------------

    private static string Tail(string s)
    {
        var lines = s.Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToArray();
        return string.Join(" | ", lines.TakeLast(6));
    }

    private static List<int> SurvivorsReferencing(string fragment)
    {
        var hits = new List<int>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(dir);
            if (!name.All(char.IsAsciiDigit) || !int.TryParse(name, out var pid)) { continue; }
            foreach (var probe in new[] { "cmdline", "environ" })
            {
                try
                {
                    var content = File.ReadAllText(Path.Combine(dir, probe)).Replace('\0', ' ');
                    if (content.Contains(fragment, StringComparison.Ordinal))
                    {
                        hits.Add(pid);
                        break;
                    }
                }
                catch { /* not ours / gone */ }
            }
        }
        return hits;
    }

    private static List<int> WaitForNoSurvivorReferencing(string fragment, TimeSpan budget)
    {
        var end = DateTime.UtcNow + budget;
        List<int> last;
        do
        {
            last = SurvivorsReferencing(fragment);
            if (last.Count == 0) { return last; }
            Thread.Sleep(500);
        } while (DateTime.UtcNow < end);
        return last;
    }
}

// Fast source-/doc-anchored locks for the remaining shell/doc findings (no
// controller run needed).
public class MiniAudit1DocTests
{
    private static string Script(string name) => File.ReadAllText(SpikePaths.P("scripts", name));
    private static string Readme => File.ReadAllText(SpikePaths.P("README.md"));

    // Tests MA-UX-4 [unit] (class fix): every literal `exit N` in scripts/*.sh
    // is documented in the README exit-code contract, so a CI wrapper/operator
    // can map any exit code to a report-presence statement.
    [Fact]
    public void Ux4_EveryScriptExitCode_IsDocumentedInReadme()
    {
        var readme = Readme;
        var undocumented = new List<string>();
        foreach (var script in Directory.EnumerateFiles(SpikePaths.P("scripts"), "*.sh"))
        {
            var codes = File.ReadAllLines(script)
                .Where(l => !l.TrimStart().StartsWith("#"))
                .SelectMany(l => Regex.Matches(l, @"(?<![\w-])exit\s+([0-9]+)").Cast<Match>())
                .Select(m => m.Groups[1].Value)
                .ToHashSet();
            foreach (var code in codes)
            {
                if (!readme.Contains($"| {code} |", StringComparison.Ordinal))
                {
                    undocumented.Add($"{Path.GetFileName(script)}:exit {code}");
                }
            }
        }
        Assert.True(undocumented.Count == 0,
            "script exit codes missing from the README exit-code contract (MA-UX-4): " + string.Join(", ", undocumented));
    }

    // Tests MA-UX-3 [unit]: the SDK-not-found remedy names the explicit
    // --dotnet-root argument (an ambient DOTNET_ROOT is stripped) and states
    // system-wide installs are not auto-discovered.
    [Fact]
    public void Ux3_SdkDiscovery_DocumentsExplicitDotnetRoot()
    {
        Assert.Contains("--dotnet-root", Script("run-spike.sh"));
        Assert.Contains("--dotnet-root", Readme);
        Assert.Contains("auto-discovered", Readme);
    }

    // Tests MA-UX-6 [unit]: the direct-suite command documents the working
    // directory so spikes/dafny-compat/global.json governs the test host.
    [Fact]
    public void Ux6_DirectSuite_DocumentsWorkingDirectory()
    {
        Assert.Contains("cd spikes/dafny-compat && dotnet test", Readme);
    }

    // Tests PR-006 [unit]: the provision AND fetch launch classes carry runtime
    // require_env_key guards so neither can launch under an unconstructed
    // environment. Source-anchored (reversion-failing if the guards are removed).
    [Fact]
    public void Pr006_ProvisionAndFetch_HaveRuntimeEnvGuards()
    {
        var script = Script("run-spike.sh");
        // provision and fetch branches each guard PATH + TMPDIR.
        var provisionBlock = Section(script, "provision)", "esac");
        var fetchBlock = Section(script, "fetch)", "esac");
        Assert.Contains("require_env_key PATH", provisionBlock);
        Assert.Contains("require_env_key TMPDIR", provisionBlock);
        Assert.Contains("require_env_key PATH", fetchBlock);
        Assert.Contains("require_env_key TMPDIR", fetchBlock);
    }

    // Tests MA-XC-5 [unit]: phase_guard classifies a timeout by the out-of-band
    // SPIKE_PHASE_TIMED_OUT flag, never by overloading exit code 124.
    [Fact]
    public void Xc5_PhaseGuard_UsesTimeoutFlag_NotExit124()
    {
        var script = Script("run-spike.sh");
        var guard = Section(script, "phase_guard() {", "}");
        Assert.Contains("SPIKE_PHASE_TIMED_OUT", guard);
        Assert.DoesNotContain("= 124", guard);
    }

    // Tests MA-XC-7 [unit]: write_failed_report emits the schema version read
    // from the schema file, not a duplicated literal a bump would leave stale.
    [Fact]
    public void Xc7_WriteFailedReport_UsesSchemaVersionVariable()
    {
        var script = Script("run-spike.sh");
        var body = Section(script, "write_failed_report() {", "run_cmd mv -- \"$target.tmp\" \"$target\"");
        Assert.Contains("\"evidence_schema_version\": %s", body);
        Assert.DoesNotContain("\"evidence_schema_version\": 2", body);
    }

    // Tests MA-HI-3 [unit]: json_escape escapes JSON control characters (newline
    // at minimum), not only backslash and quote.
    [Fact]
    public void Hi3_JsonEscape_HandlesControlCharacters()
    {
        var body = Section(Script("run-spike.sh"), "json_escape() {", "printf '%s' \"$s\"");
        Assert.Contains("\\\\n", body);   // newline -> \n substitution present
        Assert.Contains("\\\\t", body);   // tab -> \t
        Assert.Contains("\\\\r", body);   // CR -> \r
    }

    // Tests MA-RB-7 [unit]: regen-sample.sh glob-cleans crashed-regen staging
    // (samples.staged.* / .old.*) BEFORE the dirty-tree check, so leftovers a
    // crash left cannot block every future regen behind a dirty refusal.
    [Fact]
    public void Rb7_Regen_GlobCleansStagingBeforeDirtyCheck()
    {
        var script = Script("regen-sample.sh");
        var cleanIdx = script.IndexOf("samples.staged.*", StringComparison.Ordinal);
        var dirtyIdx = script.IndexOf("REFUSING to regenerate from a dirty tree", StringComparison.Ordinal);
        Assert.True(cleanIdx >= 0, "regen-sample.sh does not glob-clean samples.staged.* (MA-RB-7)");
        Assert.True(dirtyIdx >= 0, "regen-sample.sh has no dirty-tree refusal");
        Assert.True(cleanIdx < dirtyIdx, "the staged-dir glob-clean must run BEFORE the dirty-tree check (MA-RB-7)");
    }

    // Tests MA-RB-5 [unit]: the preprocess-audit mktemp is trapped for removal
    // so the ~1-3MB dump does not leak under out/.
    [Fact]
    public void Rb5_PreprocessAudit_TrapsMktempRemoval()
    {
        var script = Script("run-spike.sh");
        var block = Section(script, "preprocess-audit\" ]; then", "exit 0");
        Assert.Contains("trap 'run_cmd rm -f \"$PP_FILE\"", block);
        Assert.Contains("EXIT", block);
    }

    private static string Section(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"marker not found: {startMarker}");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end >= 0, $"end marker not found after {startMarker}: {endMarker}");
        return text.Substring(start, end - start + endMarker.Length);
    }
}
