using System;
using System.IO;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Test-only path helpers (named *Tests.cs so it is recognized as test
/// infrastructure, not a production stub). Resolves the repo root via the named
/// committed sentinel (the directory containing `.correctless/`) by walking up from
/// the test assembly — NOT the dotnet test cwd (INV-001 RS-A-04). Fixture files are
/// CopyToOutputDirectory, so they resolve beside the test assembly.
/// </summary>
public static class TestPaths
{
    public static string OutputDir => AppContext.BaseDirectory;

    public static string Fixture(params string[] parts)
        => Path.Combine(OutputDir, Path.Combine(Prepend("fixtures", parts)));

    public static string Manifest(params string[] parts)
        => Path.Combine(OutputDir, Path.Combine(Prepend("manifests", parts)));

    private static string[] Prepend(string head, string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = head;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return all;
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".correctless")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo-root sentinel (.correctless/) not found");
    }

    public static string RepoFile(params string[] parts)
        => Path.Combine(RepoRoot(), Path.Combine(parts));

    public static bool RepoFileExists(params string[] parts)
        => File.Exists(RepoFile(parts));
}

/// <summary>
/// Sanity checks that the fixture corpus + real producer artifacts the RED suite
/// depends on are actually present (so a genuine rule test that fails does so for
/// the intended reason, not a missing-file artifact). These are infra assertions,
/// not rule tests.
/// </summary>
public class TestInfraPathsTests
{
    [Fact]
    public void Repo_root_sentinel_resolves()
    {
        Assert.True(Directory.Exists(Path.Combine(TestPaths.RepoRoot(), ".correctless")));
    }

    [Fact]
    public void Real_producer_artifacts_present_for_live_coverage()
    {
        Assert.True(TestPaths.RepoFileExists("docs", "adr", "ADR-0001-dafny-integration-boundary.md"));
        Assert.True(TestPaths.RepoFileExists(".correctless", "specs", "phase-0-1-worker.md"));
        Assert.True(TestPaths.RepoFileExists("spikes", "dafny-compat", "evidence", "samples", "run-report.canonical.sample.json"));
        Assert.True(TestPaths.RepoFileExists("spikes", "dafny-compat", "manifest", "expected-loaded", "route-a.json"));
        Assert.True(TestPaths.RepoFileExists("spikes", "dafny-compat", "manifest", "probe-manifest.json"));
    }

    [Fact]
    public void Verbatim_fixtures_present()
    {
        Assert.True(File.Exists(TestPaths.Fixture("adr", "pre-migration-adr-lint.md")));
        Assert.True(File.Exists(TestPaths.Fixture("readiness", "real-parent-readiness-block.md")));
        Assert.True(File.Exists(TestPaths.Manifest("readiness-migration-manifest.json")));
    }
}
