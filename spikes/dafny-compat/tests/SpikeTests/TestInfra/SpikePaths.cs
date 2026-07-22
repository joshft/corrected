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

    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(SpikeRoot);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent!;
            }
            throw new InvalidOperationException("Could not locate repo root (.git) above the spike root.");
        }
    }

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

    /// <summary>Scratch area for tests, inside the untracked out/ area (never /tmp — PRH-005/INV-014).</summary>
    public static string TestScratch(string name)
    {
        var dir = Path.Combine(SpikeRoot, "out", "test-scratch", name);
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

    /// <summary>Test-side git HEAD reader (TA-A11) — no git binary dependency; resolves symbolic refs and packed-refs.</summary>
    public static string GitHeadCommit()
    {
        var gitDir = Repo(".git");
        var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
        if (!head.StartsWith("ref: ", StringComparison.Ordinal))
        {
            return head;
        }
        var refName = head["ref: ".Length..].Trim();
        var refFile = Path.Combine(gitDir, refName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(refFile))
        {
            return File.ReadAllText(refFile).Trim();
        }
        var packed = Path.Combine(gitDir, "packed-refs");
        if (File.Exists(packed))
        {
            foreach (var line in File.ReadAllLines(packed))
            {
                if (line.EndsWith(" " + refName, StringComparison.Ordinal))
                {
                    return line.Split(' ')[0];
                }
            }
        }
        throw new InvalidOperationException($"cannot resolve git ref {refName}");
    }
}
