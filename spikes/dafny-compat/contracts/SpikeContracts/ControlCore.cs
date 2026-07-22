// GREEN — shared net8 control-cell logic (BND-004/INV-013), Dafny-free.
// The control executables prove WHICH runtime actually ran them: the digest +
// location identity of the selected hostfxr and System.Private.CoreLib is
// recorded as concrete binding identity and re-verified test-side against the
// committed pin (config/net8-control-pin.json) — "ran on some 8.x" is
// insufficient (codex R3-3/TA-B12).

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Corrected.Spike.Contracts;

public static class ControlCore
{
    public static int Run(string routeId, ISpikeRouteAdapter adapter, string[] args)
    {
        var identityProbe = false;
        string? probe = null;
        var fixture = "";
        var solver = "";
        var outPath = "";
        var runRoot = Directory.GetCurrentDirectory();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"missing value for {args[i]}");
            switch (args[i])
            {
                case "--identity-probe": identityProbe = true; break;
                case "--probe": probe = Next(); break;
                case "--fixture": fixture = Next(); break;
                case "--solver": solver = Next(); break;
                case "--run-root": runRoot = Path.GetFullPath(Next()); break;
                case "--out": outPath = Next(); break;
                default:
                    Console.Error.WriteLine($"control: unknown argument '{args[i]}'");
                    return ExitCodes.Incomplete;
            }
        }
        if (outPath.Length == 0)
        {
            Console.Error.WriteLine("control: --out <report> is required");
            return ExitCodes.Incomplete;
        }

        var corelibPath = typeof(object).Assembly.Location;
        var corelibDir = Path.GetDirectoryName(corelibPath)!;
        var runtimeVersion = Path.GetFileName(corelibDir);
        var dotnetRoot = Path.GetFullPath(Path.Combine(corelibDir, "..", "..", ".."));
        var hostfxrPath = Path.Combine(dotnetRoot, "host", "fxr", runtimeVersion, "libhostfxr.so");
        if (!File.Exists(hostfxrPath))
        {
            var fxrRoot = Path.Combine(dotnetRoot, "host", "fxr");
            hostfxrPath = Directory.Exists(fxrRoot)
                ? Directory.EnumerateFiles(fxrRoot, "libhostfxr.so", SearchOption.AllDirectories)
                    .OrderBy(p => p, StringComparer.Ordinal).LastOrDefault() ?? hostfxrPath
                : hostfxrPath;
        }

        var probeResults = new List<Dictionary<string, object?>>();
        var exitCode = ExitCodes.RouteProbesPassed;
        if (!identityProbe && probe is not null)
        {
            try
            {
                // Three-cell adjudication leg: identical normalized seam source
                // (one shared file, both TFMs; conditional logic scan-prohibited).
                var options = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["--solver-path"] = solver,
                    ["--resource-limit"] = "10000000",
                    ["--verify-included-files"] = "false",
                    ["--standard-libraries"] = "false",
                };
                var run = adapter.Verify(new FixtureInput(fixture, options));
                var pass = run.Tasks.Count > 0 && run.Tasks.All(t => t.Outcome == SolverOutcome.Valid);
                probeResults.Add(new Dictionary<string, object?>
                {
                    ["probe"] = probe,
                    ["route"] = routeId,
                    ["status"] = pass ? "pass" : "fail",
                    ["detail"] = pass ? null : $"control cell: {run.Tasks.Count} tasks; stage {run.CompletedThroughStage}",
                });
                exitCode = pass ? ExitCodes.RouteProbesPassed : ExitCodes.ProbeFailure;
            }
            catch (Exception ex)
            {
                probeResults.Add(new Dictionary<string, object?>
                {
                    ["probe"] = probe,
                    ["route"] = routeId,
                    ["status"] = "fail",
                    ["detail"] = $"control cell typed failure fingerprint: {ex.GetType().FullName}: {ex.Message}",
                });
                exitCode = ExitCodes.ProbeFailure;
            }
        }

        var report = new Dictionary<string, object?>
        {
            ["evidence_schema_version"] = 2,
            ["evidence_schema_sha256"] = Sha256File(Path.Combine(FindSpikeRoot(), "schema", "evidence-schema.json")),
            ["probe_manifest_sha256"] = Sha256File(Path.Combine(FindSpikeRoot(), "manifest", "probe-manifest.json")),
            ["run_id"] = "control-" + Guid.NewGuid().ToString("N")[..16],
            ["kind"] = "control-report",
            ["binding_identity"] = new Dictionary<string, object?>
            {
                ["run_directory"] = runRoot,
                ["git_commit_id"] = "unbound-control-cell",
                ["git_dirty_flag"] = false,
                ["host_rid"] = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                ["sdk_version"] = ReadSdkPin(),
                ["actual_runtime_version"] = Environment.Version.ToString(),
                ["harness_target_framework"] = System.Reflection.Assembly.GetEntryAssembly()
                    ?.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false)
                    .OfType<System.Runtime.Versioning.TargetFrameworkAttribute>()
                    .FirstOrDefault()?.FrameworkName ?? "",
                ["hostfxr_identity_concrete"] = new Dictionary<string, object?>
                {
                    ["path"] = hostfxrPath,
                    ["sha256"] = File.Exists(hostfxrPath) ? Sha256File(hostfxrPath) : new string('0', 64),
                },
                ["corelib_identity_concrete"] = new Dictionary<string, object?>
                {
                    ["path"] = corelibPath,
                    ["sha256"] = Sha256File(corelibPath),
                },
            },
            ["deterministic"] = new Dictionary<string, object?>
            {
                ["per_probe_results"] = probeResults,
                ["final_suite_status"] = "unknown",
            },
            ["volatile"] = new Dictionary<string, object?>
            {
                ["timestamps"] = new Dictionary<string, object?>
                {
                    ["emitted_utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                },
            },
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        EvidenceSchema.ValidateReport(json, Path.Combine(FindSpikeRoot(), "schema", "evidence-schema.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        var temp = outPath + ".tmp-" + Environment.ProcessId;
        File.WriteAllText(temp, json);
        File.Move(temp, outPath, overwrite: true);

        Console.WriteLine($"control route {routeId}: runtime {Environment.Version} corelib {corelibPath}");
        return exitCode;
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string FindSpikeRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DafnyCompatSpike.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DafnyCompatSpike.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("cannot locate the spike root");
    }

    private static string ReadSdkPin()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindSpikeRoot(), "global.json")));
        return doc.RootElement.GetProperty("sdk").GetProperty("version").GetString() ?? "";
    }
}
