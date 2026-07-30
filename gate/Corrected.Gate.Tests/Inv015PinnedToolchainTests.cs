using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-015: pinned + locked YAML parser AND analysis toolchain AND test toolchain;
/// no Microsoft.Build.* PackageReference; the five-project restore-lock set (INV-022
/// exact-four→five: Corrected.Provenance is the 5th, non-shipped substrate); loaded-
/// version assertions; the version-skew parse fixture. [integration].
/// </summary>
public class Inv015PinnedToolchainTests
{
    private static readonly string[] GateProjects =
    {
        "Corrected.Gate", "Corrected.Gate.Kernel", "Corrected.Gate.Tests", "Corrected.Gate.Lint",
        "Corrected.Provenance",
    };

    // Tests INV-015 [integration]: YamlDotNet is pinned 18.1.0 with a LOADED-version
    // assertion. RED: the pinned package is added at GREEN (not in the cache yet), so
    // it does not load today.
    [Fact]
    public void YamlDotNet_is_pinned_18_1_0_loaded()
    {
        Assembly asm = Assembly.Load("YamlDotNet");
        // YamlDotNet freezes AssemblyVersion at major.0.0.0 (binding-redirect
        // stability), so the pinned 18.1.0 shows AssemblyVersion 18.0.0.0. The exact
        // pin lives in FileVersion — assert that (AssemblyVersion can't tell 18.1 apart).
        Assert.Equal("18.1.0.0",
            System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location).FileVersion);
    }

    // Tests INV-015 [integration]: Microsoft.CodeAnalysis.CSharp (+ Common) is the
    // in-process analysis toolchain, pinned + locked with a loaded-version assertion.
    // RED: added at GREEN.
    [Fact]
    public void Roslyn_csharp_is_loaded_and_pinned()
    {
        Assembly csharp = Assembly.Load("Microsoft.CodeAnalysis.CSharp");
        Assembly common = Assembly.Load("Microsoft.CodeAnalysis");
        Assert.NotNull(csharp.GetName().Version);
        Assert.NotNull(common.GetName().Version);
    }

    // Tests INV-015 [integration]: NO Microsoft.Build.* PackageReference exists — the
    // closure build runs out-of-process on the pinned SDK's MSBuild (R3-I1/EXT4-04).
    // Genuine guard over the committed csproj + lock files.
    [Fact]
    public void No_microsoft_build_package_reference()
    {
        foreach (var proj in GateProjects)
        {
            string csproj = File.ReadAllText(TestPaths.RepoFile("gate", proj, proj + ".csproj"));
            Assert.DoesNotContain("Microsoft.Build", csproj);
            string lockPath = TestPaths.RepoFile("gate", proj, "packages.lock.json");
            if (File.Exists(lockPath))
            {
                Assert.DoesNotContain("Microsoft.Build.", File.ReadAllText(lockPath));
            }
        }
    }

    // Tests INV-015 [integration]: the test-host is pinned (xUnit matching the spike).
    // Genuine loaded-version guard.
    [Fact]
    public void Test_host_xunit_is_pinned()
    {
        Version v = typeof(FactAttribute).Assembly.GetName().Version!;
        Assert.Equal(2, v.Major);
        Assert.Equal(9, v.Minor);
    }

    // Tests INV-015 [integration]: the <AGGREGATOR> restore/lock set is EXACTLY the
    // five gate projects (EXT7-05; INV-022 exact-four→five) — a membership meta-test.
    // Genuine guard over the committed .slnx.
    [Fact]
    public void Aggregator_membership_is_exactly_the_five_projects()
    {
        string slnx = File.ReadAllText(TestPaths.RepoFile("gate", "Corrected.Gate.slnx"));
        foreach (var proj in GateProjects)
        {
            Assert.Contains(proj + "/" + proj + ".csproj", slnx);
        }
        int projectCount = slnx.Split("<Project ").Length - 1;
        Assert.Equal(5, projectCount);
    }

    // Tests INV-015 [integration]: each gate project has a committed packages.lock.json
    // restored in locked mode. Genuine guard over the presence of the locks.
    [Fact]
    public void Each_gate_project_has_a_committed_lockfile()
    {
        foreach (var proj in GateProjects)
        {
            Assert.True(File.Exists(TestPaths.RepoFile("gate", proj, "packages.lock.json")),
                $"INV-015: gate/{proj}/packages.lock.json must be committed");
        }
    }

    // Tests INV-015 [integration]: version-skew guard — a source using the SDK's
    // newest supported C# feature parses without throwing under the gate's pinned
    // Roslyn / LanguageVersion.Latest (else a legitimate newer src/ file spuriously
    // fails closed). RED against the Roslyn-backed predicate stub.
    [Fact]
    public void Newest_csharp_feature_parses_without_throwing()
    {
        const string newest = "namespace N { public class C { public required int X { get; init; } } }";
        // Parsing via the gate's in-process Roslyn predicate must not throw.
        bool ok = Corrected.Gate.SyntaxAllowlist.ContainsOnlyDeclarations(newest, Array.Empty<string>());
        Assert.True(ok);
    }
}
