using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Corrected.Gate;

/// <summary>
/// The REAL out-of-process pinned-SDK build behind INV-011's shipped-closure ban
/// (QA-002 full-enforcement). A closure project is restored and BUILT out of process
/// on the pinned SDK (`dotnet build -t:Rebuild`) so source generators actually run,
/// EmitCompilerGeneratedFiles emits their output, and the post-build Compile/Analyzer
/// item graph + evaluated DefineConstants are captured via MSBuild `-getItem`/
/// `-getProperty`. A naive csproj-XML + *.cs glob is blind to generated sources and
/// to executable code inside live `#if` branches — this makes them visible.
///
/// PATH-LEAK DISCIPLINE: this type consumes absolute paths internally (compile item
/// FullPaths, the SDK analyzer dir, the temp generated-files dir) but NOTHING that it
/// hands back to <see cref="ProductionSurfaceScanner"/> for an operator-facing
/// offending item is ever an absolute path. Callers report bare identities/filenames
/// via <see cref="SafeIdentity"/>.
/// </summary>
internal static class ClosureBuildRunner
{
    /// <summary>A captured, evaluated snapshot of one real closure build.</summary>
    internal sealed class ClosureProbe
    {
        public required string SdkVersion { get; init; }

        /// <summary>The build's evaluated DefineConstants, split on ';' (e.g. TRACE;DEBUG;SHIP;NET10_0;...).</summary>
        public required IReadOnlyList<string> DefineConstants { get; init; }

        /// <summary>Bare assembly filenames of every resolved Analyzer/source-generator item (default set + injected).</summary>
        public required IReadOnlyList<string> AnalyzerFilenames { get; init; }

        /// <summary>Absolute FullPaths of the post-build hand-written Compile source files (obj/bin excluded).</summary>
        public required IReadOnlyList<string> CompileSourceFiles { get; init; }

        /// <summary>Absolute paths of the *.cs emitted by generators into the CompilerGeneratedFilesOutputPath.</summary>
        public required IReadOnlyList<string> GeneratedSourceFiles { get; init; }
    }

    // Process-lifetime cache: each closure/baseline csproj is really built at most once
    // per test/gate process (keyed by absolute path). Real builds are slow; the build
    // result is a pure function of the committed project + pinned SDK, so caching is safe.
    // Lazy value so the real build runs at most ONCE per csproj even if GetOrAdd's factory
    // is invoked concurrently for the same key (ConcurrentDictionary does not guarantee a
    // single factory call). Losing Lazy instances are discarded without their factory ever
    // running (.Value is only called on the stored winner). QA mini-audit (ProbeCache atomicity).
    private static readonly ConcurrentDictionary<string, Lazy<ClosureProbe?>> ProbeCache =
        new(StringComparer.Ordinal);

    private const int BuildTimeoutMs = 240_000;

    // Every emitted-generated-files temp dir created this process, deleted at process
    // exit (QA r2 F1). Cleanup is deferred to exit — NOT to end-of-build — because the
    // ProbeCache hands the caller live paths INTO these dirs for the process lifetime, so
    // an eager per-build delete would race the scanner's read of the generated sources.
    private static readonly ConcurrentBag<string> GenDirs = new();

    static ClosureBuildRunner()
    {
        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            foreach (var d in GenDirs)
            {
                try { if (Directory.Exists(d)) { Directory.Delete(d, recursive: true); } }
                catch { /* best effort — OS /tmp cleanup is the backstop */ }
            }
        };
    }

    /// <summary>
    /// Restore + real out-of-process build of <paramref name="csprojAbsPath"/>, returning
    /// the captured probe, or <c>null</c> on ANY restore/build/parse failure (the caller
    /// maps null to ClosureUncomputable — a DISTINCT fail-closed state, never a vacuous pass).
    /// </summary>
    public static ClosureProbe? TryProbe(string csprojAbsPath)
        => ProbeCache.GetOrAdd(csprojAbsPath, k => new Lazy<ClosureProbe?>(() => BuildAndCapture(k))).Value;

    private static ClosureProbe? BuildAndCapture(string csprojAbsPath)
    {
        string projDir = Path.GetDirectoryName(csprojAbsPath)!;
        string genDir = Path.Combine(Path.GetTempPath(), "inv011-gen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(genDir);
        GenDirs.Add(genDir); // tracked for process-exit cleanup (F1), incl. the restore-fail path below

        // 1) Restore under the committed packages.lock.json (locked, single-source per NuGet.Config <clear/>).
        var restore = RunDotnet(new[] { "restore", csprojAbsPath, "--nologo", "--locked-mode" }, projDir);
        if (restore is null || restore.Value.ExitCode != 0)
        {
            return null;
        }

        // 2) REAL build. -t:Rebuild forces a fresh CoreCompile regardless of any warm obj
        //    (so generators re-run and emit deterministically); -getProperty/-getItem then
        //    report the POST-BUILD evaluated graph; EmitCompilerGeneratedFiles + a fresh
        //    CompilerGeneratedFilesOutputPath capture generated sources out of the source tree.
        var build = RunDotnet(new[]
        {
            "build", csprojAbsPath, "--no-restore", "--nologo", "-noAutoResponse", "-t:Rebuild",
            "-getProperty:NETCoreSdkVersion", "-getProperty:DefineConstants",
            "-getItem:Compile", "-getItem:Analyzer",
            "-p:EmitCompilerGeneratedFiles=true",
            "-p:CompilerGeneratedFilesOutputPath=" + genDir,
        }, projDir);

        if (build is null || build.Value.ExitCode != 0)
        {
            return null; // nonzero build/-getItem => ClosureUncomputable
        }

        ClosureProbe? probe = TryParse(build.Value.StdOut, genDir);
        return probe; // unparseable JSON => null => ClosureUncomputable
    }

    private static ClosureProbe? TryParse(string stdout, string genDir)
    {
        // MSBuild -getItem/-getProperty prints a single JSON object to stdout. Be tolerant of
        // any leading banner text by seeking the first '{'.
        int brace = stdout.IndexOf('{');
        if (brace < 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout.Substring(brace));
            JsonElement rootEl = doc.RootElement;
            if (!rootEl.TryGetProperty("Properties", out var props)
                || !rootEl.TryGetProperty("Items", out var items))
            {
                return null;
            }

            string sdk = props.TryGetProperty("NETCoreSdkVersion", out var sdkEl) ? (sdkEl.GetString() ?? "") : "";
            if (sdk.Length == 0)
            {
                return null;
            }

            string defines = props.TryGetProperty("DefineConstants", out var dEl) ? (dEl.GetString() ?? "") : "";
            var defineList = defines
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

            var analyzers = new List<string>();
            if (items.TryGetProperty("Analyzer", out var aArr) && aArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in aArr.EnumerateArray())
                {
                    // Filename = bare assembly name (NO absolute path). NEVER read Identity here
                    // for surfacing: it is the analyzer dll's ABSOLUTE path.
                    if (a.TryGetProperty("Filename", out var fn) && fn.GetString() is { Length: > 0 } name)
                    {
                        analyzers.Add(name);
                    }
                }
            }

            var compileSources = new List<string>();
            if (items.TryGetProperty("Compile", out var cArr) && cArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cArr.EnumerateArray())
                {
                    if (c.TryGetProperty("FullPath", out var fp) && fp.GetString() is { Length: > 0 } full)
                    {
                        string norm = full.Replace('\\', '/');
                        string fname = norm.Substring(norm.LastIndexOf('/') + 1);
                        // Exclude ONLY the known SDK-emitted boilerplate by FILENAME (AssemblyInfo /
                        // GlobalUsings / AssemblyAttributes) — NOT an over-broad /obj//bin/ path skip,
                        // which would let a malicious <Compile Include="obj/Payload.cs"> evade the ban
                        // (QA mini-audit H-F3). Generator output is read separately from genDir.
                        bool sdkBoilerplate =
                            fname.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
                            || fname.EndsWith(".GlobalUsings.g.cs", StringComparison.OrdinalIgnoreCase)
                            || fname.EndsWith(".AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase);
                        if (norm.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                            && !sdkBoilerplate
                            && File.Exists(full))
                        {
                            compileSources.Add(full);
                        }
                    }
                }
            }

            // A successful build of a non-empty project always resolves >=1 hand-written
            // (non-boilerplate) Compile item; zero means the Compile set could not be
            // extracted (e.g. a future -getItem shape change) -> fail closed (uncomputable),
            // never a silent Pass (QA mini-audit R-F2).
            if (compileSources.Count == 0)
            {
                return null;
            }

            var generated = Directory.Exists(genDir)
                ? Directory.EnumerateFiles(genDir, "*.cs", SearchOption.AllDirectories).ToList()
                : new List<string>();

            return new ClosureProbe
            {
                SdkVersion = sdk,
                DefineConstants = defineList,
                AnalyzerFilenames = analyzers,
                CompileSourceFiles = compileSources,
                GeneratedSourceFiles = generated,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// One-shot `dotnet restore` (locked, under the committed lock) purely to populate the
    /// NuGet global-packages cache so a build-asset presence check can inspect a package's
    /// on-disk assets. Returns true on exit 0.
    /// </summary>
    public static bool TryRestore(string csprojAbsPath)
    {
        string projDir = Path.GetDirectoryName(csprojAbsPath)!;
        var r = RunDotnet(new[] { "restore", csprojAbsPath, "--nologo", "--locked-mode" }, projDir);
        return r is { ExitCode: 0 };
    }

    private readonly struct DotnetResult
    {
        public DotnetResult(int exit, string stdout)
        {
            ExitCode = exit;
            StdOut = stdout;
        }

        public int ExitCode { get; }
        public string StdOut { get; }
    }

    private static DotnetResult? RunDotnet(IReadOnlyList<string> args, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveDotnet(),
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        // Deny any interactive/telemetry surprises; keep the child build hermetic.
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        // Disable MSBuild node reuse: a reusable worker node inherits the redirected stdout
        // pipe's write end and can hold it open past the child's exit, hanging the drain
        // UNBOUNDED past BuildTimeoutMs. One-shot gate builds gain nothing from reuse.
        // (QA mini-audit — portability hang.)
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            // Drain stdout/stderr on background tasks to avoid pipe-buffer deadlock.
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();

            if (!proc.WaitForExit(BuildTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }
            // Bound the post-exit drain too — a lingering child holding the pipe would
            // otherwise block a bare ReadToEnd()/WaitForExit() forever. Kill + fail closed.
            if (!Task.WaitAll(new Task[] { outTask, errTask }, BuildTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }
            string stdout = outTask.GetAwaiter().GetResult();
            _ = errTask.GetAwaiter().GetResult();
            return new DotnetResult(proc.ExitCode, stdout);
        }
        catch (Exception)
        {
            return null; // any launch failure => ClosureUncomputable (fail-closed)
        }
    }

    private static string ResolveDotnet()
    {
        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(root))
        {
            string candidate = Path.Combine(root, "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return "dotnet"; // resolved on PATH
    }

    /// <summary>The NuGet global-packages root (env override, else ~/.nuget/packages). Absolute — internal use only.</summary>
    public static string NuGetGlobalPackages()
    {
        string? env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(env))
        {
            return env;
        }
        string home = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".nuget", "packages");
    }

    /// <summary>
    /// Reduce any candidate offending string to a PATH-FREE identity: a bare filename with
    /// no directory component, no home dir, no username. Applied to everything surfaced to
    /// an operator, so a `-getItem` FullPath (which contains /home/&lt;user&gt;/...) can
    /// never leak through ScanResult.OffendingItem.
    /// </summary>
    public static string SafeIdentity(string candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return candidate;
        }
        string norm = candidate.Replace('\\', '/');
        return norm.Contains('/') ? norm.Substring(norm.LastIndexOf('/') + 1) : norm;
    }
}
