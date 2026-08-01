using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-016: repo-root SDK pin, semantically synced, isolated NuGet restore; the
/// latestPatch band predicate; CPM opt-out + regression; the .gitattributes LF pin.
/// [integration].
/// </summary>
[Collection("Subprocess")]
public class Inv016SdkPinTests
{
    // The precise band predicate (EXT5-04/EXT6-03): major 10, minor 0, feature-band
    // 3xx (hundreds digit of patch == 3), resolved patch >= 10.0.302.
    private static bool InBand(string version)
    {
        var parts = version.Split('-')[0].Split('.');
        if (parts.Length < 3) return false;
        if (!int.TryParse(parts[0], out int major) || major != 10) return false;
        if (!int.TryParse(parts[1], out int minor) || minor != 0) return false;
        if (!int.TryParse(parts[2], out int patch)) return false;
        return (patch / 100) == 3 && patch >= 302;
    }

    // Tests INV-016 [integration]: a REPO-ROOT global.json pins the SDK so the muxer
    // selects it for the root invocation. STAGE-A NOTE: scope forbids creating
    // repo-root files, so this FAILS RED until GREEN adds the repo-root global.json.
    [Fact]
    public void Repo_root_global_json_exists()
    {
        Assert.True(TestPaths.RepoFileExists("global.json"),
            "INV-016: a repo-root global.json (rollForward: latestPatch, allowPrerelease: false) must exist");
    }

    // Tests INV-016 [integration]: the repo-root and spike global.json are
    // SEMANTICALLY synced on sdk.version (parse both, compare) — NOT byte-identical.
    // RED at Stage A (no repo-root file).
    [Fact]
    public void Global_json_semantically_synced_on_sdk_version()
    {
        string spike = File.ReadAllText(TestPaths.RepoFile("spikes", "dafny-compat", "global.json"));
        string spikeVer = JsonDocument.Parse(spike).RootElement.GetProperty("sdk").GetProperty("version").GetString()!;

        string rootPath = TestPaths.RepoFile("global.json");
        Assert.True(File.Exists(rootPath), "repo-root global.json missing (INV-016)");
        string root = File.ReadAllText(rootPath);
        string rootVer = JsonDocument.Parse(root).RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
        Assert.Equal(spikeVer, rootVer);
    }

    // Tests INV-016 [integration]: the repo-root global.json sets allowPrerelease:false
    // explicitly (EXT6-03) and rollForward: latestPatch. RED at Stage A.
    [Fact]
    public void Repo_root_global_json_sets_latestPatch_and_no_prerelease()
    {
        string rootPath = TestPaths.RepoFile("global.json");
        Assert.True(File.Exists(rootPath), "repo-root global.json missing (INV-016)");
        var sdk = JsonDocument.Parse(File.ReadAllText(rootPath)).RootElement.GetProperty("sdk");
        Assert.Equal("latestPatch", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());
    }

    // Tests INV-016 [integration]: the gate NuGet.Config has <clear/> + a single
    // nuget.org source (single-source-isolated restore, TB-004). Genuine guard.
    [Fact]
    public void Gate_nuget_config_clears_and_pins_single_source()
    {
        string cfg = File.ReadAllText(TestPaths.RepoFile("gate", "NuGet.Config"));
        Assert.Contains("<clear", cfg);
        Assert.Contains("api.nuget.org", cfg);
    }

    // Tests INV-016 [integration]: CPM opt-out — a dummy repo-root Directory.Packages.props
    // dropped into a temp copy does NOT capture the gate's inline versions (RS-UC-11).
    // Genuine guard: the gate's own Directory.Packages.props disables CPM (seals the
    // upward walk). RED-adjacent: also asserts ManagePackageVersionsCentrally=false.
    [Fact]
    public void Cpm_opt_out_seals_upward_inheritance()
    {
        string props = File.ReadAllText(TestPaths.RepoFile("gate", "Directory.Packages.props"));
        Assert.Contains("<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>", props);
    }

    // Tests INV-016 [integration]: a build-time NETCoreSdkVersion band-membership
    // assertion — the resolved SDK is in the 10.0.3xx band, >= 10.0.302 (NOT exact
    // equality; latestPatch). Runs `dotnet --version` from the repo root.
    [Fact]
    public void Resolved_sdk_is_in_the_pinned_band()
    {
        var psi = new ProcessStartInfo("dotnet", "--version")
        {
            WorkingDirectory = TestPaths.RepoRoot(),
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        string ver = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        Assert.True(InBand(ver), $"resolved SDK '{ver}' not in band 10.0.3xx >= 10.0.302");
    }

    // Tests INV-016 [integration]: a repo-root .gitattributes pins the parsed
    // specs/ADR to LF (INV-001 cross-ref). RED at Stage A (scope).
    [Fact]
    public void Repo_root_gitattributes_pins_lf()
    {
        Assert.True(TestPaths.RepoFileExists(".gitattributes"),
            "INV-016/INV-001: a repo-root .gitattributes pinning parsed specs/ADR to LF must exist");
    }
}
