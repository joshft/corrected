// Tests PRH-001..PRH-005 (PRH-001's deletion/induced armor lives in
// Inv006VerdictTests; PRH-005's hygiene grep lives in Inv009EvidenceTests —
// direct structural legs here).
using System.Text.Json;
using System.Text.RegularExpressions;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class ProhibitionTests
{
    // Tests PRH-001 [unit]: no error path may fall back to the optimistic
    // verdict — a completed set of all-FAIL results must never yield COMPATIBLE,
    // and a set with a single Incomplete probe must not either.
    [Fact]
    public void Prh001_ErrorPaths_NeverFallBackToCompatible()
    {
        var manifest = ProbeManifest.Load(SpikePaths.P("manifest", "probe-manifest.json"));
        var allFail = manifest.Entries
            .Select(e => new ProbeResult(new ProbeKey(e.ProbeId, e.Route), ProbeStatus.Fail)).ToList();
        Assert.NotEqual(RouteState.Compatible,
            VerdictAggregator.ComputeRouteVerdict(manifest, "A", allFail, SuiteStatus.Success).State);

        var oneIncomplete = manifest.Entries
            .Select((e, i) => new ProbeResult(new ProbeKey(e.ProbeId, e.Route), i == 0 ? ProbeStatus.Incomplete : ProbeStatus.Pass))
            .ToList();
        Assert.NotEqual(RouteState.Compatible,
            VerdictAggregator.ComputeRouteVerdict(manifest, "A", oneIncomplete, SuiteStatus.Success).State);
    }

    // Tests PRH-002 [unit]: version-syntax scan over BOTH csproj files and the
    // authoritative Directory.Packages.props (codex R2-6) — no floating,
    // non-singleton range, or nightly versions for the Dafny/Boogie family.
    [Fact]
    public void Prh002_NoFloatingRangeOrNightlyVersions()
    {
        var files = SpikePaths.AllCsprojFiles().Append(SpikePaths.P("Directory.Packages.props")).ToList();
        foreach (var file in files)
        {
            var doc = SpikePaths.Xml(file);
            foreach (var el in doc.Descendants().Where(e => e.Name.LocalName is "PackageVersion" or "PackageReference"))
            {
                var id = el.Attribute("Include")?.Value ?? "";
                var version = el.Attribute("Version")?.Value;
                if (version is null)
                {
                    continue; // versionless PackageReference under CPM — INV-001
                }
                Assert.False(version.Contains('*'), $"{file}: floating version {id}={version}");
                Assert.False(version.Contains("nightly", StringComparison.OrdinalIgnoreCase), $"{file}: nightly version {id}={version}");
                Assert.Matches(@"^\[[^,\[\]]+\]$", version); // exact singleton range only
                if (id.StartsWith("Dafny") || id.StartsWith("Boogie."))
                {
                    Assert.Contains(version, new[] { "[4.11.0]", "[3.5.5]" });
                }
            }
        }
    }

    // Tests PRH-002 [unit]: the committed lock files' requested ranges for the
    // Dafny/Boogie family are singleton brackets (locked-mode restore contexts).
    // Source: spikes/dafny-compat/adapters/SpikeDafnyAdapter.RouteB/packages.lock.json
    [Fact]
    public void Prh002_LockRequestedRanges_AreSingletonBrackets_ForFamilyDirects()
    {
        foreach (var (name, _) in SpikePaths.Projects)
        {
            using var doc = SpikePaths.Json(SpikePaths.LockFilePath(name));
            foreach (var fw in doc.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                foreach (var dep in fw.Value.EnumerateObject())
                {
                    if (!(dep.Name.StartsWith("Dafny") || dep.Name.StartsWith("Boogie.")))
                    {
                        continue;
                    }
                    var type = dep.Value.GetProperty("type").GetString();
                    if (type is "Direct" or "CentralTransitive")
                    {
                        var requested = dep.Value.GetProperty("requested").GetString()!;
                        var m = Regex.Match(requested, @"^\[([^,\]]+)(?:,\s*([^\]]+))?\]$");
                        Assert.True(m.Success, $"{name}: {dep.Name} requested range '{requested}' is not a singleton bracket");
                        if (m.Groups[2].Success)
                        {
                            Assert.Equal(m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim());
                        }
                    }
                }
            }
        }
    }

    // Tests PRH-003 [unit]: production src/ stays empty. (The authoritative gate
    // is the review-time git diff path check — an advisory human gate; this is
    // the suite-level backstop.)
    [Fact]
    public void Prh003_ProductionSrcStaysEmpty()
    {
        var srcDir = SpikePaths.Repo("src");
        if (Directory.Exists(srcDir))
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(srcDir));
        }
    }

    // Tests PRH-004 [unit]: Process.Start/ProcessStartInfo usage outside the
    // launcher component is a finding — the managed launcher (contracts
    // Components.cs) is the sole sanctioned site.
    [Fact]
    public void Prh004_ProcessStart_OnlyInManagedLauncherComponent()
    {
        var launcherFile = Path.Combine("contracts", "SpikeContracts", "Components.cs");
        foreach (var file in SpikePaths.NonTestSourceFiles())
        {
            var rel = Path.GetRelativePath(SpikePaths.SpikeRoot, file);
            if (rel.Replace('\\', '/') == launcherFile.Replace('\\', '/'))
            {
                continue;
            }
            var text = File.ReadAllText(file);
            Assert.False(text.Contains("Process.Start") || text.Contains("ProcessStartInfo"),
                $"{rel} launches processes outside the allowlisted managed launcher (PRH-004)");
        }
    }

    // Tests PRH-004 [unit]: no `dafny` CLI invocation anywhere — an in-process
    // API failure is recorded as evidence, never papered over by shelling out.
    [Fact]
    public void Prh004_NoDafnyCliInvocation_InSourceOrScripts()
    {
        var scriptFiles = Directory.EnumerateFiles(SpikePaths.P("scripts"), "*.sh");
        foreach (var file in SpikePaths.NonTestSourceFiles().Concat(scriptFiles))
        {
            foreach (var (line, idx) in File.ReadAllLines(file).Select((l, i) => (l, i)))
            {
                var code = line.Split('#')[0].Split("//")[0];
                Assert.False(Regex.IsMatch(code, @"(^|[\s""/])dafny(\.exe)?\s"),
                    $"{file}:{idx + 1} appears to invoke a dafny CLI binary (PRH-004)");
            }
        }
    }

    // Tests PRH-004 [unit] (codex R3-9, TA-B8, TA-A12): the STATIC allowlist
    // test over EVERY spike script — parses each script's ACTUAL bash ALLOWLIST
    // array (not a self-declared comment), asserts it equals the committed
    // constant, asserts run_cmd is the sole dispatch (no bare curl/dotnet
    // command lines), audits every run_cmd call site's first argument,
    // deny-scans for fetch/interpreter binaries, and checks --disable /
    // -noAutoResponse ADJACENCY on the actual invocation lines.
    public static IEnumerable<object[]> SpikeScripts() =>
        Directory.EnumerateFiles(SpikePaths.P("scripts"), "*.sh")
            .Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(SpikeScripts))]
    public void Prh004_RunCmdDispatch_AllowlistArray_CallSites_DenySet_Adjacency(string scriptName)
    {
        var script = File.ReadAllText(SpikePaths.P("scripts", scriptName));

        // 1. The REAL dispatch structures exist.
        var arrayMatch = Regex.Match(script, @"^ALLOWLIST=\(([^)]*)\)", RegexOptions.Multiline);
        Assert.True(arrayMatch.Success, "run-spike.sh has no ALLOWLIST=() bash array (TA-B8)");
        Assert.True(Regex.IsMatch(script, @"^run_cmd\(\)\s*\{", RegexOptions.Multiline),
            "run-spike.sh has no run_cmd() dispatch function (TA-B8)");

        // 2. The array equals the committed constant.
        var declared = arrayMatch.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(c => c, StringComparer.Ordinal).ToList();
        var expected = BootstrapAllowlist.Commands.OrderBy(c => c, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, declared);

        // Non-comment code lines (drop the shebang and trailing comments).
        var codeLines = script.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => !l.TrimStart().StartsWith("#") && l.Trim().Length > 0)
            .ToList();

        // 3. Every run_cmd call site's first argument is allowlisted.
        var callSites = codeLines.SelectMany(l => Regex.Matches(l, @"run_cmd\s+([A-Za-z0-9_.\-]+)").Cast<Match>()).ToList();
        foreach (var site in callSites)
        {
            Assert.Contains(site.Groups[1].Value, BootstrapAllowlist.Commands);
        }

        // 4. Deny-set scan: no direct fetch/interpreter invocations anywhere in code.
        foreach (var line in codeLines.Where(l => !l.Contains("ALLOWLIST=")))
        {
            Assert.False(Regex.IsMatch(line, @"(^|[\s|;&]|\$\()(wget|python3?|perl|ruby|node|nc)\b"),
                $"deny-set binary invoked directly: {line.Trim()} (PRH-004/TA-B8)");
            // 5. No bare curl/dotnet outside run_cmd dispatch.
            var bare = Regex.Matches(line, @"(^|[\s|;&]|\$\()(curl|dotnet)\b");
            foreach (Match m in bare)
            {
                Assert.True(Regex.IsMatch(line, @"run_cmd\s+" + m.Groups[2].Value),
                    $"bare {m.Groups[2].Value} invocation outside run_cmd: {line.Trim()} (TA-B8)");
            }
        }

        // 6. Adjacency on actual invocation lines: curl --disable first; dotnet
        //    build-verb lines carry -noAutoResponse.
        foreach (var line in codeLines)
        {
            if (Regex.IsMatch(line, @"run_cmd\s+curl\b"))
            {
                Assert.Matches(@"run_cmd\s+curl\s+--disable\b", line);
            }
            if (Regex.IsMatch(line, @"run_cmd\s+dotnet\s+(restore|build|test|msbuild)\b"))
            {
                Assert.Contains("-noAutoResponse", line);
            }
        }

        // 7. The monotonic-deadline and supervision contract stays pinned in
        //    the CONTROLLER script (the other scripts do not own the deadline).
        if (scriptName == "run-spike.sh")
        {
            Assert.Contains("/proc/uptime", script);
            Assert.Contains("setsid", script);
            Assert.Contains("MSBUILDDISABLENODEREUSE", script);
        }
    }

    // Tests PRH-004 [integration] (codex R4-08, TA-B9): BASH_ENV poisoning with
    // a launch that GENUINELY lets BASH_ENV fire — no -p flag (simulating a
    // careless operator). The committed contract: the controller re-execs under
    // env -i bash -p or refuses fail-closed; either way the poison must not
    // reach any launch environment, verified from the env-audit RECEIPT FILE
    // the test reads (never a script-emitted marker).
    [Fact]
    public void Prh004_BashEnvPoisoning_UnhardenedInvocation_PoisonNeverReachesLaunchEnv()
    {
        var runRoot = SpikePaths.TestScratch("prh004-poison-bashenv");
        var poison = Path.Combine(runRoot, "poison.sh");
        File.WriteAllText(poison, "export SPIKE_POISONED=1\n");
        var env = new Dictionary<string, string> { ["BASH_ENV"] = poison };

        // Deliberately UNhardened: bash without -p, so noninteractive bash
        // executes $BASH_ENV before the first script line.
        var result = Launch.ScriptUnhardened("scripts/run-spike.sh", env,
            "--run-root", runRoot, "--out", Path.Combine(runRoot, "run-report.json"));

        if (result.ExitCode == 0)
        {
            // Contract branch (a): self re-exec under env -i bash -p — prove the
            // poison is absent from every recorded launch environment.
            using var audit = Launch.Receipt(runRoot, RunLayout.EnvAuditReceiptRelativePath);
            Assert.DoesNotContain("SPIKE_POISONED", audit.RootElement.GetRawText());
        }
        else
        {
            // Contract branch (b): fail-closed refusal naming the hardened invocation.
            Assert.Contains("bash -p", result.StdOut + result.StdErr);
        }
    }

    // Tests PRH-004 [integration] (TA-B9): .curlrc poisoning — a REAL curl
    // --disable run through the launcher against a local file target; the
    // hostile curlrc tries to hijack output placement, and --disable must
    // defeat it. Observables are files the TEST creates and checks.
    [Fact]
    public void Prh004_CurlrcPoisoning_DisableDefeatsOutputHijack()
    {
        var scratch = SpikePaths.TestScratch("prh004-poison-curlrc");
        var source = Path.Combine(scratch, "source.txt");
        File.WriteAllText(source, "curl-disable-proof");
        var hijack = Path.Combine(scratch, "hijacked-output.txt");
        File.WriteAllText(Path.Combine(scratch, ".curlrc"), $"output=\"{hijack}\"\n");
        var expected = Path.Combine(scratch, "expected-output.txt");

        var env = new Dictionary<string, string> { ["HOME"] = scratch, ["CURL_HOME"] = scratch };
        var result = Launch.Tool("curl", env, "--disable", "-s", "-o", expected, "file://" + source);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(expected), "curl --disable did not write to the explicit -o target");
        Assert.Equal("curl-disable-proof", File.ReadAllText(expected));
        Assert.False(File.Exists(hijack), ".curlrc hijacked curl output despite --disable (PRH-004/codex R4-08)");
    }

    // Tests PRH-004 [unit] (TA-A10): no second-parser semantics — beyond the
    // Regex API ban, the SEAM projects must not read fixture files at all
    // (fixture bytes reach semantics only through DafnyCore's typed pipeline),
    // and harness sources stay regex-free. Best-effort source scan: it cannot
    // catch every string-analysis form (e.g. custom char loops); PRH-004 code
    // review remains the backstop for those.
    [Fact]
    public void Prh004_NoSecondParser_SeamsDoNotReadFixtureBytes_NoRegexInClassificationPaths()
    {
        var seamFiles = new[] { "adapters" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(SpikePaths.SpikeRoot, d), "*.cs", SearchOption.AllDirectories))
            .Where(NotBuildDir);
        foreach (var file in seamFiles)
        {
            var text = File.ReadAllText(file);
            Assert.False(text.Contains("using System.Text.RegularExpressions") || text.Contains("new Regex("),
                $"{file} uses regex — semantic facts must come from resolved-AST/typed APIs (PRH-004/PROHIBIT-002)");
            foreach (var forbidden in new[] { "File.ReadAllText", "File.ReadAllLines", "File.ReadAllBytes", "File.OpenRead", "File.OpenText" })
            {
                Assert.False(text.Contains(forbidden),
                    $"{file} reads files directly ({forbidden}) — fixture content flows only through DafnyCore's typed pipeline (TA-A10)");
            }
        }

        var harnessFiles = new[] { "harness" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(SpikePaths.SpikeRoot, d), "*.cs", SearchOption.AllDirectories))
            .Where(NotBuildDir);
        foreach (var file in harnessFiles)
        {
            var text = File.ReadAllText(file);
            Assert.False(text.Contains("using System.Text.RegularExpressions") || text.Contains("new Regex("),
                $"{file} uses regex in a classification-adjacent path (PRH-004)");
        }

        static bool NotBuildDir(string f) =>
            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");
    }

    // Tests PRH-005 [unit]: scripts/ci/README carry no host-system details —
    // the full committed-artifact grep lives in Inv009EvidenceTests.
    [Fact]
    public void Prh005_ScriptsCiReadme_NoAbsoluteLocalPaths()
    {
        var files = Directory.EnumerateFiles(SpikePaths.P("scripts"))
            .Concat(Directory.EnumerateFiles(SpikePaths.P("ci")))
            .Append(SpikePaths.P("README.md"));
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in new[] { "/home/", "/Users/", "C:\\" })
            {
                Assert.False(text.Contains(pattern), $"{file} contains banned host path pattern '{pattern}' (PRH-005)");
            }
        }
    }
}
