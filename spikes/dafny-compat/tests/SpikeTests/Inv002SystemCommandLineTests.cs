// Tests INV-002: System.CommandLine resolves to Dafny's required beta,
// structurally; P03 loaded-assembly + runtime identity armor.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Inv002SystemCommandLineTests
{
    // Tests INV-002 [unit]: lock-file version assert (P01 leg). Wherever
    // System.CommandLine appears in a resolved graph it is exactly the beta pin,
    // and the seam graphs (which carry DafnyCore) must contain it.
    // Source: spikes/dafny-compat/adapters/SpikeDafnyAdapter.RouteA/packages.lock.json
    [Fact]
    public void Locks_PinSystemCommandLineToDafnyRequiredBeta()
    {
        var seamSawIt = 0;
        foreach (var (name, _) in SpikePaths.Projects)
        {
            using var doc = SpikePaths.Json(SpikePaths.LockFilePath(name));
            foreach (var fw in doc.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                if (fw.Value.TryGetProperty("System.CommandLine", out var dep))
                {
                    Assert.Equal(SpecConstants.SystemCommandLinePin, dep.GetProperty("resolved").GetString());
                    if (SpikePaths.SeamProjectNames.Contains(name))
                    {
                        seamSawIt++;
                    }
                }
            }
        }
        Assert.True(seamSawIt >= 2, "both seam graphs must resolve System.CommandLine (DafnyCore dependency) at the pinned beta");
    }

    // Tests INV-002 [unit]: no spike code uses System.CommandLine APIs (source grep leg).
    [Fact]
    public void NoSpikeSourceUsesSystemCommandLineApis()
    {
        foreach (var file in SpikePaths.NonTestSourceFiles())
        {
            var text = File.ReadAllText(file);
            Assert.False(text.Contains("System.CommandLine"),
                $"{file} references System.CommandLine — spike code must not use its APIs (INV-002)");
        }
    }

    // Tests INV-002 [unit]: the committed per-route expected-loaded sets carry
    // the mandatory route anchors AND real (non-null) SHA-256 identities.
    // Fails in RED by design: placeholders force the GREEN capture via DD-008.
    [Theory]
    [InlineData("route-a.json", "DafnyDriver", "DafnyCore")]
    [InlineData("route-b.json", "DafnyCore", "DafnyPipeline")]
    public void ExpectedLoadedSets_CommitAnchors_AndRealSha256Identities(string file, string anchor1, string anchor2)
    {
        using var doc = SpikePaths.Json(SpikePaths.P("manifest", "expected-loaded", file));
        var anchors = doc.RootElement.GetProperty("anchors").EnumerateArray().Select(a => a.GetString()).ToList();
        Assert.Contains(anchor1, anchors);
        Assert.Contains(anchor2, anchors);

        // QA-004 class fix: the trust-relevant universe is declared
        // machine-readably in the oracle (consumed by BOTH harness and test);
        // narrowing it becomes a reviewable oracle diff, not a silent code edit.
        Assert.Equal("deps-json-package-runtime-assets",
            doc.RootElement.GetProperty("universe_predicate").GetProperty("id").GetString());

        var assemblies = doc.RootElement.GetProperty("assemblies").EnumerateArray().ToList();
        Assert.NotEmpty(assemblies);
        foreach (var asm in assemblies)
        {
            var sha = asm.GetProperty("sha256").GetString();
            Assert.False(string.IsNullOrEmpty(sha),
                $"expected-loaded set {file}: {asm.GetProperty("simple_name")} has no captured SHA-256 — runtime identity is by file digest (RS-008), and an uncaptured set cannot gate P03");
            Assert.Matches("^[0-9a-f]{64}$", sha);
        }
    }

    // Tests INV-002 [unit] (codex R4-03): report-mutation tests — P03's pass
    // predicate must FAIL on wrong TFM, wrong runtime major, wrong CoreLib location.
    [Fact]
    public void P03_Mutation_WrongTfm_Fails()
    {
        var (evidence, expected) = SyntheticP03();
        var mutated = evidence with { HarnessTargetFramework = ".NETCoreApp,Version=v8.0" };
        Assert.Equal(ProbeStatus.Fail, P03Evaluator.Evaluate(mutated, expected));
    }

    [Fact]
    public void P03_Mutation_WrongRuntimeMajor_Fails()
    {
        var (evidence, expected) = SyntheticP03();
        var mutated = evidence with { RuntimeMajorVersion = 8 };
        Assert.Equal(ProbeStatus.Fail, P03Evaluator.Evaluate(mutated, expected));
    }

    [Fact]
    public void P03_Mutation_WrongCoreLibLocation_Fails()
    {
        var (evidence, expected) = SyntheticP03();
        var mutated = evidence with { CoreLibPath = "<run-root>/wrong/System.Private.CoreLib.dll" };
        Assert.Equal(ProbeStatus.Fail, P03Evaluator.Evaluate(mutated, expected));
    }

    // Tests INV-002 [unit]: an expected-but-never-loaded assembly is a failure,
    // never a vacuous pass; set equality, not subset (RS-008).
    [Fact]
    public void P03_ExpectedButNeverLoaded_IsFailure_NeverVacuousPass()
    {
        var (evidence, expected) = SyntheticP03();
        var missingOne = evidence with
        {
            LoadedAssemblies = evidence.LoadedAssemblies.Where(a => a.SimpleName != "DafnyCore").ToList(),
        };
        Assert.Equal(ProbeStatus.Fail, P03Evaluator.Evaluate(missingOne, expected));
    }

    // Tests INV-002 [unit]: an extra Dafny/Boogie-family assembly outside the
    // committed mapping fails P03 (set equality).
    [Fact]
    public void P03_UnexpectedFamilyAssembly_Fails()
    {
        var (evidence, expected) = SyntheticP03();
        var extra = evidence with
        {
            LoadedAssemblies = evidence.LoadedAssemblies
                .Append(new LoadedAssemblyIdentity("DafnyLanguageServer", "4.11.0", "<run-root>/x/DafnyLanguageServer.dll", new string('b', 64)))
                .ToList(),
        };
        Assert.Equal(ProbeStatus.Fail, P03Evaluator.Evaluate(extra, expected));
    }

    // Tests INV-002 [integration]: P03 end-to-end — runtime-loaded assemblies
    // equal the committed per-route expected set, traced through .deps.json to
    // the isolated spike-local package asset and hash-compared.
    // Entry: child-process harness invocation via the allowlisted launcher.
    // Through: the real built route harness on the real .NET 10 host.
    // Exit: P03 evidence with set-equality pass and net10-on-net10 gate satisfied.
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void P03_RuntimeLoadedSet_EqualsCommittedExpectedSet(string route)
    {
        var scratch = SpikePaths.TestScratch($"inv002-p03-{route}");
        var report = Path.Combine(scratch, "p03-report.json");
        var result = Launch.Harness(route, "--probe", "P03", "--out", report);
        Assert.True(result.ExitCode == ExitCodes.RouteProbesPassed,
            $"P03 route {route} child failed (exit {result.ExitCode}): {result.StdErr}");
        using var doc = Launch.Report(report);
        var probe = doc.RootElement.GetProperty("deterministic").GetProperty("per_probe_results").EnumerateArray()
            .Single(p => p.GetProperty("probe").GetString() == "P03" && p.GetProperty("route").GetString() == route);
        Assert.Equal("pass", probe.GetProperty("status").GetString());

        // TA-A1: the TEST re-asserts set equality itself — reported identities vs
        // the committed expected-loaded oracle — never trusting status:pass alone.
        var expectedFile = route == "A" ? "route-a.json" : "route-b.json";
        using var expected = SpikePaths.Json(SpikePaths.P("manifest", "expected-loaded", expectedFile));
        var expectedSet = expected.RootElement.GetProperty("assemblies").EnumerateArray()
            .Select(a => (Name: a.GetProperty("simple_name").GetString()!, Sha: a.GetProperty("sha256").GetString()))
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        Assert.All(expectedSet, t => Assert.False(string.IsNullOrEmpty(t.Sha), $"expected-loaded {expectedFile}: {t.Name} has no captured digest"));

        // QA-004: the reported set is the FULL codex-F8 universe (every
        // non-framework .deps.json package runtime asset) — no prefix filter.
        // Set equality against the committed oracle catches substitution of ANY
        // package assembly (System.Reactive, LanguageServer deps, etc.), not
        // just the Dafny/Boogie/System.CommandLine subset.
        var reportedSet = doc.RootElement.GetProperty("deterministic").GetProperty("loaded_assembly_identities").EnumerateArray()
            .Select(a => (Name: a.GetProperty("simple_name").GetString()!, Sha: (string?)a.GetProperty("file_sha256").GetString()))
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(expectedSet, reportedSet); // set EQUALITY, not subset (RS-008)
        // The universe is genuinely widened: more than the old 3-prefix subset.
        Assert.True(reportedSet.Count > 14,
            $"P03 universe looks narrowed ({reportedSet.Count} assemblies) — QA-004 requires the full .deps.json package set");
        Assert.Contains(reportedSet, t => !t.Name.StartsWith("Dafny", StringComparison.Ordinal)
                                          && !t.Name.StartsWith("Boogie", StringComparison.Ordinal)
                                          && t.Name != "System.CommandLine");
    }

    // Tests INV-002/OQ-004 [integration] (codex R2-11, TA-B4): Route B anchor —
    // the named DafnyPipeline consumption (DafnyStandardLibraries.doo embedded
    // resource, loaded via Assembly.Load("DafnyPipeline")) with the removal/
    // differential proving the consumption matters. A bare Assembly.Load does
    // not satisfy the anchor. The REMOVAL fault is constructed BY THE TEST:
    // it copies the built Route B output directory and deletes DafnyPipeline.dll
    // from the copy — no SUT-interpreted --remove flag exists.
    // Entry: child-process Route B harness with fixtures/stdlib-anchor.dfy;
    //   --standard-libraries is a genuine Dafny option (variance in evidence).
    // Through: real DafnyCore resolution loading the .doo from DafnyPipeline.dll
    //   (dafny v4.11.0 Source/DafnyCore/DafnyFile.cs:214-222); no mocking.
    // Exit: verification passes with DafnyPipeline present; the differential
    //   (--standard-libraries=false) fails at stage=resolution; the test-
    //   mutilated copy fails with the typed missing-assembly/missing-resource
    //   error — never a pass.
    [Fact]
    public void RouteB_DafnyPipelineConsumption_TestConstructedRemoval_AndDifferential()
    {
        var scratch = SpikePaths.TestScratch("inv002-oq004");
        var with = Path.Combine(scratch, "with-stdlib.json");
        var without = Path.Combine(scratch, "without-stdlib.json");
        var removed = Path.Combine(scratch, "pipeline-removed.json");

        var r1 = Launch.Harness("B", "--probe", "anchor-stdlib", "--fixture", "fixtures/stdlib-anchor.dfy",
            "--standard-libraries", "true", "--out", with);
        Assert.Equal(ExitCodes.RouteProbesPassed, r1.ExitCode);

        var r2 = Launch.Harness("B", "--probe", "anchor-stdlib", "--fixture", "fixtures/stdlib-anchor.dfy",
            "--standard-libraries", "false", "--out", without);
        Assert.NotEqual(ExitCodes.RouteProbesPassed, r2.ExitCode);
        using var d2 = Launch.Report(without);
        Assert.Contains("resolution", d2.RootElement.GetRawText());

        // TA-B4: the TEST copies the built Route B output and removes DafnyPipeline.dll itself.
        var artifact = RunContext.Resolve("RouteBHarness");
        var sourceDir = Path.GetDirectoryName(artifact.AbsolutePath)!;
        var copyDir = Path.Combine(scratch, "routeb-copy");
        SpikePaths.CopyTree(sourceDir, copyDir);
        var pipelineDll = Path.Combine(copyDir, "DafnyPipeline.dll");
        Assert.True(File.Exists(pipelineDll), "DafnyPipeline.dll not in Route B output — the anchor premise is broken");
        File.Delete(pipelineDll); // the TEST constructs the removal fault

        var env = EnvProfiles.For("harness", RunContext.RunRoot());
        var r3 = Launch.Dll(Path.Combine(copyDir, Path.GetFileName(artifact.AbsolutePath)), env,
            "--probe", "anchor-stdlib", "--fixture", "fixtures/stdlib-anchor.dfy",
            "--standard-libraries", "true", "--out", removed);
        Assert.NotEqual(ExitCodes.RouteProbesPassed, r3.ExitCode);
        var observable = (r3.StdOut + r3.StdErr) + (File.Exists(removed) ? File.ReadAllText(removed) : "");
        Assert.Contains("DafnyPipeline", observable);
    }

    private static (P03Evidence Evidence, ExpectedLoadedSet Expected) SyntheticP03()
    {
        var sha = new string('a', 64);
        var loaded = new List<LoadedAssemblyIdentity>
        {
            new("DafnyDriver", "4.11.0", "<run-root>/pkg/DafnyDriver.dll", sha),
            new("DafnyCore", "4.11.0", "<run-root>/pkg/DafnyCore.dll", sha),
            new("System.CommandLine", SpecConstants.SystemCommandLinePin, "<run-root>/pkg/System.CommandLine.dll", sha),
        };
        var evidence = new P03Evidence(
            Route: "A",
            HarnessTargetFramework: ".NETCoreApp,Version=v10.0",
            RuntimeMajorVersion: 10,
            RuntimeConfigFrameworkName: "Microsoft.NETCore.App",
            RuntimeConfigRollForward: "LatestPatch",
            HostfxrPath: "<dotnet-root>/host/fxr/libhostfxr.so",
            HostfxrSha256: sha,
            CoreLibPath: "<dotnet-root>/shared/Microsoft.NETCore.App/System.Private.CoreLib.dll",
            CoreLibSha256: sha,
            LoadedAssemblies: loaded);
        var expected = new ExpectedLoadedSet("A", "deps-json-trust-relevant", new[] { "DafnyDriver", "DafnyCore" }, loaded);
        return (evidence, expected);
    }
}
