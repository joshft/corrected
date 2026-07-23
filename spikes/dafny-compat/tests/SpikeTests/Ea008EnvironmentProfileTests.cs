// Tests EA-008 (TA-B11): the evidentiary-run environment is CONSTRUCTED from
// committed per-launch environment profiles, not sanitized by prohibition.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Ea008EnvironmentProfileTests
{
    // Tests EA-008 [unit]: the committed profile file exists, covers every
    // launch class, and the build-facing classes carry the EA-008 minimum key
    // set with the exact pinned values (build-server disables IN-profile).
    [Fact]
    public void CommittedProfiles_ExistPerLaunchClass_WithMinimumKeySetAndPinnedValues()
    {
        var path = SpikePaths.P("config", "env-profiles.json");
        Assert.True(File.Exists(path), "missing committed config/env-profiles.json (EA-008/TA-B11)");
        using var doc = SpikePaths.Json(path);
        var classes = doc.RootElement.GetProperty("launch_classes");

        // MA-UC-2/MA-XC-6: provision and fetch are enumerated — the BND-004
        // curl/tar launches must never run under an unconstructed environment.
        foreach (var launchClass in new[] { "controller", "provision", "fetch", "restore", "build", "test", "harness", "control", "solver-identity", "sentinel" })
        {
            Assert.True(classes.TryGetProperty(launchClass, out _), $"missing launch class '{launchClass}'");
        }

        // MA-UC-2: the provision/fetch profiles carry the minimum key set —
        // decoy-first PATH (INV-003), run-root TMPDIR, and SSL passthrough
        // (the fetch leg performs the BND-002/BND-004 downloads).
        foreach (var networkClass in new[] { "provision", "fetch" })
        {
            var profile = classes.GetProperty(networkClass);
            string Value(string key)
            {
                Assert.True(profile.TryGetProperty(key, out var v), $"launch class '{networkClass}' missing EA-008 key {key}");
                return v.GetString()!;
            }
            Assert.Equal("<constructed:decoy-first>", Value("PATH"));
            Assert.StartsWith("<run-root>/", Value("TMPDIR"));
            Assert.Equal("<passthrough>", Value("SSL_CERT_FILE"));
            Assert.Equal("<passthrough>", Value("SSL_CERT_DIR"));
        }

        foreach (var buildClass in new[] { "controller", "restore", "build", "test" })
        {
            var profile = classes.GetProperty(buildClass);
            string Value(string key)
            {
                Assert.True(profile.TryGetProperty(key, out var v), $"launch class '{buildClass}' missing EA-008 key {key}");
                return v.GetString()!;
            }
            Assert.Equal("<constructed:decoy-first>", Value("PATH")); // decoy-first per INV-003
            Assert.StartsWith("<run-root>/", Value("NUGET_PACKAGES")); // spike-local packages folder (BND-001)
            Assert.StartsWith("<run-root>/", Value("TMPDIR"));
            Assert.StartsWith("<run-root>/", Value("DOTNET_CLI_HOME"));
            Assert.Equal("C.UTF-8", Value("LC_ALL")); // invariant per EA-006
            Assert.Equal("C.UTF-8", Value("LANG"));
            Assert.Equal("<passthrough>", Value("SSL_CERT_FILE"));
            Assert.Equal("<passthrough>", Value("SSL_CERT_DIR"));
            Assert.Equal("1", Value("DOTNET_CLI_TELEMETRY_OPTOUT"));
            Assert.Equal("1", Value("MSBUILDDISABLENODEREUSE"));
            Assert.Equal("false", Value("UseSharedCompilation"));
        }

        // Documented SDK-injected exceptions are enumerated, not left implicit.
        var injected = doc.RootElement.GetProperty("documented_sdk_injected")
            .EnumerateArray().Select(k => k.GetString()).ToList();
        Assert.Contains("DOTNET_HOST_PATH", injected);
    }

    // Tests EA-008 [unit] (MA-UC-2/MA-XC-6 class fix): the expected class list
    // DERIVES from the scripts' actual run_with_env call sites (like the
    // PRH-004 allowlist test does for commands) — a launch class invoked by
    // any committed script must be profiled, so adding a new launch call site
    // fails this test until config/env-profiles.json covers it.
    [Fact]
    public void EveryRunWithEnvCallSiteClass_IsProfiled()
    {
        var invoked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var script in Directory.EnumerateFiles(SpikePaths.P("scripts"), "*.sh"))
        {
            foreach (var line in File.ReadAllLines(script))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"run_with_env\s+([a-z][a-z0-9-]*)\b");
                if (match.Success)
                {
                    invoked.Add(match.Groups[1].Value);
                }
            }
        }
        Assert.Contains("provision", invoked);
        Assert.Contains("fetch", invoked);
        var profiled = SpikePaths.Json(SpikePaths.P("config", "env-profiles.json"))
            .RootElement.GetProperty("launch_classes").EnumerateObject().Select(p => p.Name).ToHashSet();
        foreach (var cls in invoked)
        {
            Assert.True(profiled.Contains(cls),
                $"scripts invoke run_with_env class '{cls}' but config/env-profiles.json does not profile it — the launch would run under an unconstructed environment (EA-008/MA-UC-2)");
        }
    }

    // Tests EA-008 [unit]: contracts constants and the committed file agree —
    // the profile keys named by EA-008 all appear in the harness-facing classes.
    [Fact]
    public void ContractsProfileKeyList_CoveredByCommittedProfiles()
    {
        var controllerKeys = EnvProfiles.KeysFor("controller");
        foreach (var key in EnvironmentProfiles.ProfileKeys)
        {
            Assert.Contains(key, controllerKeys);
        }
        foreach (var key in EnvironmentProfiles.DocumentedSdkInjectedKeys)
        {
            Assert.Contains(key, EnvProfiles.DocumentedSdkInjected());
        }
    }

    // Tests EA-008 [integration] (TA-B11): the child's ACTUAL received
    // environment (recorded in the report's binding identity in root-kind +
    // relative-path form) equals the committed harness profile — every profile
    // key present, no undocumented extras beyond the enumerated SDK-injected
    // exceptions, and the recorded values use structured root-kind form.
    [Fact]
    public void ChildActualEnvironment_EqualsCommittedProfile_ModuloDocumentedInjections()
    {
        var scratch = SpikePaths.TestScratch("ea008-env-audit");
        var reportPath = Path.Combine(scratch, "report.json");
        var result = Launch.Harness("A", "--probe", "P03", "--out", reportPath);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);

        using var doc = Launch.Report(reportPath);
        var recorded = doc.RootElement.GetProperty("binding_identity").GetProperty("environment_profile_values")
            .EnumerateArray().ToList();
        Assert.NotEmpty(recorded);

        var recordedKeys = recorded.Select(e => e.GetProperty("key").GetString()!).ToHashSet();
        var profileKeys = EnvProfiles.KeysFor("harness");
        var documented = EnvProfiles.DocumentedSdkInjected();

        foreach (var key in profileKeys)
        {
            Assert.Contains(key, recordedKeys); // every profile key reached the child
        }
        foreach (var key in recordedKeys)
        {
            Assert.True(profileKeys.Contains(key) || documented.Contains(key),
                $"child received undocumented environment key '{key}' — only profile keys plus enumerated SDK injections are permitted (EA-008)");
        }

        // Structured root-kind + relative-path serialization (codex R3-9/R4-08).
        foreach (var entry in recorded)
        {
            Assert.Contains(entry.GetProperty("root").GetString(), new[] { "run-root", "home", "system" });
            Assert.False(entry.GetProperty("path").GetString()!.StartsWith('/'),
                "recorded environment value is a raw absolute path, not root-kind + relative form (EA-008)");
        }
    }
}
