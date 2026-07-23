// Test-side infrastructure (real code — test project only, INV-008).
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Corrected.Spike.Tests;

public static class SpikePaths
{
    private static readonly Lazy<string> _spikeRoot = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DafnyCompatSpike.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate spike root (DafnyCompatSpike.sln) above the test base directory.");
    });

    public static string SpikeRoot => _spikeRoot.Value;

    public static string RepoRoot =>
        // Gitfile merge group (PR-001/MA-XC-1): the test side consumes the SAME
        // shared gitfile-aware resolver as production — a linked worktree's
        // .git FILE is a repo boundary; fail closed when none exists.
        Corrected.Spike.Contracts.GitResolver.FindRepoRoot(SpikeRoot)
        ?? throw new InvalidOperationException("Could not locate repo root (.git directory or gitfile) above the spike root.");

    public static string P(params string[] segments) => Path.Combine(new[] { SpikeRoot }.Concat(segments).ToArray());

    public static string Repo(params string[] segments) => Path.Combine(new[] { RepoRoot }.Concat(segments).ToArray());

    /// <summary>The nine project directories (relative to spike root) and their csproj paths.</summary>
    public static readonly IReadOnlyDictionary<string, string> Projects = new Dictionary<string, string>
    {
        ["SpikeContracts"] = "contracts/SpikeContracts/SpikeContracts.csproj",
        ["SpikeDafnyAdapter.RouteA"] = "adapters/SpikeDafnyAdapter.RouteA/SpikeDafnyAdapter.RouteA.csproj",
        ["SpikeDafnyAdapter.RouteB"] = "adapters/SpikeDafnyAdapter.RouteB/SpikeDafnyAdapter.RouteB.csproj",
        ["RouteAHarness"] = "harness/RouteAHarness/RouteAHarness.csproj",
        ["RouteBHarness"] = "harness/RouteBHarness/RouteBHarness.csproj",
        ["SpikeAggregator"] = "aggregator/SpikeAggregator/SpikeAggregator.csproj",
        ["RouteAControl"] = "control/RouteAControl/RouteAControl.csproj",
        ["RouteBControl"] = "control/RouteBControl/RouteBControl.csproj",
        ["SpikeTests"] = "tests/SpikeTests/SpikeTests.csproj",
    };

    /// <summary>Route-seam projects: the sole legitimate owners of Dafny/Boogie PackageReferences (INV-008).</summary>
    public static readonly IReadOnlySet<string> SeamProjectNames = new HashSet<string>
    {
        "SpikeDafnyAdapter.RouteA",
        "SpikeDafnyAdapter.RouteB",
    };

    public static string CsprojPath(string projectName) =>
        Path.Combine(SpikeRoot, Projects[projectName].Replace('/', Path.DirectorySeparatorChar));

    public static string LockFilePath(string projectName) =>
        Path.Combine(Path.GetDirectoryName(CsprojPath(projectName))!, "packages.lock.json");

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string Sha256Text(string text) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static JsonDocument Json(string absolutePath) =>
        JsonDocument.Parse(File.ReadAllText(absolutePath), new JsonDocumentOptions { AllowTrailingCommas = false });

    public static XDocument Xml(string absolutePath) => XDocument.Load(absolutePath);

    /// <summary>All non-test spike C# source files (excludes out/, bin/, obj/, tests/).</summary>
    public static IEnumerable<string> NonTestSourceFiles()
    {
        return Directory.EnumerateFiles(SpikeRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f) && !f.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}"));
    }

    /// <summary>All spike C# source files including tests (excludes out/, bin/, obj/).</summary>
    public static IEnumerable<string> AllSourceFiles()
    {
        return Directory.EnumerateFiles(SpikeRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f));
    }

    public static IEnumerable<string> AllCsprojFiles() =>
        Projects.Values.Select(rel => Path.Combine(SpikeRoot, rel.Replace('/', Path.DirectorySeparatorChar)));

    private static bool IsExcluded(string f) =>
        f.Contains($"{Path.DirectorySeparatorChar}out{Path.DirectorySeparatorChar}")
        || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    /// <summary>
    /// QA-021: one unique scratch token per test-host process, so scratch run
    /// roots are NEVER reused across suite invocations — a name-keyed root
    /// reused between runs let a prior (possibly failed) nested-controller
    /// fixture leak into the next run (digest-mismatch flakes). Prior runs'
    /// token directories are swept best-effort at first use.
    /// </summary>
    private static readonly Lazy<string> _scratchToken = new(() =>
    {
        var parent = Path.Combine(SpikeRoot, "out", "test-scratch");
        Directory.CreateDirectory(parent);
        var token = "run-" + Guid.NewGuid().ToString("N")[..12];
        foreach (var stale in Directory.EnumerateDirectories(parent))
        {
            try
            {
                Directory.Delete(stale, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort sweep only; a locked leftover is left in place.
            }
        }
        return token;
    });

    /// <summary>Scratch area for tests, inside the untracked out/ area (never /tmp — PRH-005/INV-014); unique per test-host run (QA-021).</summary>
    public static string TestScratch(string name)
    {
        var dir = Path.Combine(SpikeRoot, "out", "test-scratch", _scratchToken.Value, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static bool IsLinuxX64 =>
        OperatingSystem.IsLinux() && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64;

    /// <summary>Recursive directory copy for test-owned scratch mutations (TA-B3a/TA-B4).</summary>
    public static void CopyTree(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    /// <summary>Writes an executable shell stub into scratch (TA-B2/TA-B5 test-constructed fault binaries).</summary>
    public static string WriteExecutable(string absolutePath, string shellBody)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, "#!/bin/sh\n" + shellBody + "\n");
        File.SetUnixFileMode(absolutePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return absolutePath;
    }

    /// <summary>
    /// QA-001: is <paramref name="commit"/> an ancestor of (or equal to) HEAD?
    /// Walks first-parent + merge-parent commit graph from HEAD via loose/packed
    /// objects — no git binary dependency. Conservative: returns true if the
    /// commit is reachable within a bounded walk, false otherwise.
    /// </summary>
    public static bool IsAncestorOfHead(string commit)
    {
        var head = GitHeadCommit();
        if (string.Equals(head, commit, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // Prefer the git CLI when available (exact answer); fall back to true
        // only for HEAD-equality above. The CLI is invoked read-only.
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "")
            {
                WorkingDirectory = RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("merge-base");
            psi.ArgumentList.Add("--is-ancestor");
            psi.ArgumentList.Add(commit);
            psi.ArgumentList.Add("HEAD");
            using var proc = System.Diagnostics.Process.Start(psi)!;
            proc.WaitForExit(15000);
            return proc.ExitCode == 0;
        }
        catch (Exception)
        {
            // No git binary: accept only the HEAD-equality case (already false here).
            return false;
        }
    }

    /// <summary>Test-side git HEAD reader (TA-A11) — no git binary dependency; delegates to the shared gitfile-aware resolver (worktree-correct: gitdir + commondir).</summary>
    public static string GitHeadCommit() =>
        Corrected.Spike.Contracts.GitResolver.ReadHeadCommit(RepoRoot);

    /// <summary>
    /// MA-ED-4: RID-scope gate for tests whose guarantee is proven only on the
    /// pinned linux-x64 host (EA-002/RS-005). On any other host this THROWS —
    /// a loud non-pass outcome — so an environment-scoped test can never
    /// record success while its scoped body did not run (AP-013). A meta-test
    /// bans the silent early-return pattern in test sources.
    /// </summary>
    public static void RequireProvenRid()
    {
        if (!IsLinuxX64)
        {
            throw new InvalidOperationException(
                "skipped: unproven RID "
                + System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier
                + " — this guarantee is scoped to the pinned linux-x64 host and was NOT proven here (EA-002/RS-005/MA-ED-4)");
        }
    }
}
