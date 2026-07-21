// Test-side launch helpers (real code — test project only).
//
// TA-B13 (DD-008): harness/control artifacts are NEVER resolved from repo-local
// bin/Debug. They come from the controller-provided run context
// (SPIKE_RUN_CONTEXT -> run-context.json receipt), and the launched dll's
// digest is verified test-side against the receipt before launch. Tests run
// outside a controller run fail loudly.
//
// TA-B11 (EA-008): child environments are constructed from the committed
// per-launch profiles (config/env-profiles.json), never empty and never
// ambient-inherited.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public static class RunContext
{
    public sealed record Artifact(string Name, string AbsolutePath, string Sha256);

    public static string RequirePath()
    {
        var path = Environment.GetEnvironmentVariable(RunLayout.RunContextEnvVar);
        Assert.False(string.IsNullOrEmpty(path),
            $"no controller run context: {RunLayout.RunContextEnvVar} is unset. Integration tests launch only " +
            "controller-built artifacts from the run root (DD-008/TA-B13) — run the suite via scripts/run-spike.sh.");
        Assert.True(File.Exists(path), $"run-context receipt missing at {path} (TA-B13)");
        return path!;
    }

    public static string RunRoot()
    {
        using var doc = SpikePaths.Json(RequirePath());
        return doc.RootElement.GetProperty("run_root").GetString()!;
    }

    public static string RunId()
    {
        using var doc = SpikePaths.Json(RequirePath());
        return doc.RootElement.GetProperty("run_id").GetString()!;
    }

    /// <summary>Resolves a named artifact and verifies its on-disk digest equals the build receipt's claim TEST-SIDE.</summary>
    public static Artifact Resolve(string name)
    {
        using var doc = SpikePaths.Json(RequirePath());
        var root = doc.RootElement.GetProperty("run_root").GetString()!;
        Assert.True(doc.RootElement.GetProperty("artifacts").TryGetProperty(name, out var entry),
            $"run-context receipt has no artifact '{name}' (TA-B13)");
        var abs = Path.Combine(root, entry.GetProperty("path").GetString()!);
        var claimed = entry.GetProperty("sha256").GetString()!;
        Assert.True(File.Exists(abs), $"artifact {name} missing at {abs} — launched artifacts must come from the run root (DD-008)");
        Assert.True(SpikePaths.Sha256File(abs) == claimed,
            $"artifact {name} digest mismatch vs run-context receipt — refusing to launch an unattested binary (TA-B13)");
        return new Artifact(name, abs, claimed);
    }
}

public static class EnvProfiles
{
    /// <summary>Materializes a committed launch-class profile (EA-008), substituting the run-root token; template placeholders resolve minimally test-side (GREEN's controller owns full construction).</summary>
    public static IReadOnlyDictionary<string, string> For(string launchClass, string runRoot)
    {
        using var doc = SpikePaths.Json(SpikePaths.P("config", "env-profiles.json"));
        Assert.True(doc.RootElement.GetProperty("launch_classes").TryGetProperty(launchClass, out var profile),
            $"config/env-profiles.json has no launch class '{launchClass}' (EA-008/TA-B11)");
        var result = new Dictionary<string, string>();
        foreach (var kv in profile.EnumerateObject())
        {
            var template = kv.Value.GetString() ?? "";
            var value = template switch
            {
                "<constructed:decoy-first>" => Path.Combine(runRoot, "decoys") + Path.PathSeparator + "/usr/bin:/bin",
                "<passthrough>" => Environment.GetEnvironmentVariable(kv.Name) ?? "",
                _ => template.Replace("<run-root>", runRoot),
            };
            result[kv.Name] = value;
        }
        return result;
    }

    public static IReadOnlySet<string> KeysFor(string launchClass)
    {
        using var doc = SpikePaths.Json(SpikePaths.P("config", "env-profiles.json"));
        return doc.RootElement.GetProperty("launch_classes").GetProperty(launchClass)
            .EnumerateObject().Select(p => p.Name).ToHashSet();
    }

    public static IReadOnlySet<string> DocumentedSdkInjected()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("config", "env-profiles.json"));
        return doc.RootElement.GetProperty("documented_sdk_injected")
            .EnumerateArray().Select(k => k.GetString()!).ToHashSet();
    }
}

public static class Launch
{
    /// <summary>Launches a route harness artifact resolved (and digest-verified) from the controller run context.</summary>
    public static LaunchResult Harness(string route, params string[] args)
    {
        var artifact = RunContext.Resolve(route == "A" ? "RouteAHarness" : "RouteBHarness");
        var runRoot = RunContext.RunRoot();
        var argv = new List<string> { "exec", artifact.AbsolutePath };
        argv.AddRange(args);
        return ManagedLauncher.Launch(new LaunchRequest(
            ExecutablePath: "dotnet",
            Argv: argv,
            WorkingDirectory: SpikePaths.SpikeRoot,
            EnvironmentProfile: EnvProfiles.For("harness", runRoot),
            TimeoutSeconds: 600));
    }

    /// <summary>Launches a route harness with a TEST-CONSTRUCTED environment profile (TA-B2 startup-gate leg).</summary>
    public static LaunchResult HarnessWithEnv(string route, IReadOnlyDictionary<string, string> env, params string[] args)
    {
        var artifact = RunContext.Resolve(route == "A" ? "RouteAHarness" : "RouteBHarness");
        var argv = new List<string> { "exec", artifact.AbsolutePath };
        argv.AddRange(args);
        return ManagedLauncher.Launch(new LaunchRequest(
            "dotnet", argv, SpikePaths.SpikeRoot, env, 600));
    }

    /// <summary>Same as Harness but the LAUNCHER kills the child with SIGKILL after the delay (TA-A9 external fault).</summary>
    public static LaunchResult HarnessKilledAfter(string route, double killAfterMs, params string[] args)
    {
        var artifact = RunContext.Resolve(route == "A" ? "RouteAHarness" : "RouteBHarness");
        var runRoot = RunContext.RunRoot();
        var argv = new List<string> { "exec", artifact.AbsolutePath };
        argv.AddRange(args);
        return ManagedLauncher.Launch(new LaunchRequest(
            "dotnet", argv, SpikePaths.SpikeRoot, EnvProfiles.For("harness", runRoot), 600, KillAfterMs: killAfterMs));
    }

    /// <summary>Launches an ARBITRARY dll copy (e.g. a test-mutilated Route B output copy — TA-B4) through the launcher.</summary>
    public static LaunchResult Dll(string dllAbsolutePath, IReadOnlyDictionary<string, string> env, params string[] args)
    {
        var argv = new List<string> { "exec", dllAbsolutePath };
        argv.AddRange(args);
        return ManagedLauncher.Launch(new LaunchRequest(
            "dotnet", argv, SpikePaths.SpikeRoot, env, 600));
    }

    /// <summary>Launches a script under the hardened contract (bash -p).</summary>
    public static LaunchResult Script(string scriptRelPath, IReadOnlyDictionary<string, string>? env = null, params string[] args)
        => ScriptCore(scriptRelPath, hardened: true, env, args);

    /// <summary>TA-B9: launches a script WITHOUT -p, simulating a careless operator invocation, so BASH_ENV genuinely fires.</summary>
    public static LaunchResult ScriptUnhardened(string scriptRelPath, IReadOnlyDictionary<string, string>? env = null, params string[] args)
        => ScriptCore(scriptRelPath, hardened: false, env, args);

    private static LaunchResult ScriptCore(string scriptRelPath, bool hardened, IReadOnlyDictionary<string, string>? env, string[] args)
    {
        var script = SpikePaths.P(scriptRelPath.Split('/'));
        Assert.True(File.Exists(script), $"missing committed script: {scriptRelPath}");
        var argv = new List<string>();
        if (hardened)
        {
            argv.Add("-p");
        }
        argv.Add(script);
        argv.AddRange(args);
        return ManagedLauncher.Launch(new LaunchRequest(
            ExecutablePath: "bash",
            Argv: argv,
            WorkingDirectory: SpikePaths.SpikeRoot,
            EnvironmentProfile: env ?? new Dictionary<string, string>(),
            TimeoutSeconds: 600));
    }

    /// <summary>Launches an arbitrary allowlisted executable through the managed launcher (e.g. the real curl for TA-B9).</summary>
    public static LaunchResult Tool(string executable, IReadOnlyDictionary<string, string>? env = null, params string[] args)
    {
        return ManagedLauncher.Launch(new LaunchRequest(
            executable, args.ToList(), SpikePaths.SpikeRoot, env ?? new Dictionary<string, string>(), 120));
    }

    /// <summary>Parses an evidence report emitted at <paramref name="reportPath"/> (Exit contracts).</summary>
    public static JsonDocument Report(string reportPath)
    {
        Assert.True(File.Exists(reportPath),
            $"evidence report not emitted at {reportPath} — a missing report is its own state, never pass (AP-009).");
        return SpikePaths.Json(reportPath);
    }

    /// <summary>Reads the sentinel nonce LEDGER FILE from a run root — the test-side observable (TA-B1), never a report echo.</summary>
    public static JsonDocument Ledger(string runRoot)
    {
        var path = Path.Combine(runRoot, RunLayout.SentinelLedgerRelativePath);
        Assert.True(File.Exists(path),
            $"sentinel ledger missing at {path} — it must be PRE-CREATED at count zero before the run (RS-003d/codex F6)");
        return SpikePaths.Json(path);
    }

    /// <summary>Reads a receipt file from a run root (TA-B9/TA-A4/TA-A9 test-side observables).</summary>
    public static JsonDocument Receipt(string runRoot, string relativePath)
    {
        var path = Path.Combine(runRoot, relativePath);
        Assert.True(File.Exists(path), $"receipt missing at {path} — receipts are the test-side observable, not report echoes");
        return SpikePaths.Json(path);
    }
}
