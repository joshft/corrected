using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-022: Corrected.Provenance is the 5th, NON-SHIPPED gate project + a designed
/// shared CONTRACT; the exact-four→five migration is complete. Two enforcement guards
/// beyond the migrated INV-015 membership meta-test:
///  (1) a lockfile-registry test that discovers the aggregated project dirs FROM the
///      committed .slnx and asserts each carries a committed packages.lock.json, so a
///      from-clean `--locked-mode` restore cannot fail for a missing lockfile;
///  (2) the PRH-010 no-shipped-reference scan — a static ProjectReference-graph scan
///      asserting no shipped `src/` project references Corrected.Provenance as a
///      build/binary dependency (the substrate is reused only by the gate/test surface;
///      future release-provenance consumers INV-031/032/033 reuse by REIMPLEMENTATION
///      of the generic contracts, not by linking this gate project — INV-033
///      non-recursion). [integration].
/// </summary>
public class Inv022ProvenanceMigrationTests
{
    private const string ProvenanceProject = "Corrected.Provenance";

    // Tests INV-022 [integration]: the lockfile REGISTRY — every project the committed
    // .slnx aggregates has a packages.lock.json committed beside its csproj. Project
    // dirs are discovered FROM the .slnx (not a pinned literal set), so a 6th project
    // added to the aggregator without a lock, or the 5th (Corrected.Provenance) lock
    // going missing, RED-fails here — the guard that keeps the from-clean 5-project
    // `dotnet restore --locked-mode` from failing on an absent lockfile.
    [Fact]
    public void Every_aggregated_project_has_a_committed_lockfile()
    {
        string slnxPath = TestPaths.RepoFile("gate", "Corrected.Gate.slnx");
        string slnxDir = Path.GetDirectoryName(slnxPath)!;
        string slnx = File.ReadAllText(slnxPath);

        string[] projectRelPaths = Regex.Matches(slnx, "<Project\\s+Path=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value.Replace('\\', '/'))
            .ToArray();

        // INV-022 exact-four→five: the aggregator now enumerates EXACTLY five projects.
        Assert.Equal(5, projectRelPaths.Length);

        foreach (string rel in projectRelPaths)
        {
            string csproj = Path.GetFullPath(Path.Combine(slnxDir, rel));
            Assert.True(File.Exists(csproj), $"INV-022: aggregated project csproj missing: {rel}");
            string lockPath = Path.Combine(Path.GetDirectoryName(csproj)!, "packages.lock.json");
            Assert.True(File.Exists(lockPath),
                $"INV-022: {rel} has no committed packages.lock.json beside it — a from-clean " +
                "`dotnet restore --locked-mode` over the 5-project set would fail");
        }
    }

    // Tests INV-022 / PRH-010 [integration]: the NO-SHIPPED-REFERENCE scan. A real
    // static scan of every non-build-output `.csproj` <ProjectReference> edge in the
    // repo. Corrected.Provenance is a non-shipped substrate: (a) NO shipped `src/`
    // project references it (the PRH-010 claim; `src/` is the future shipped surface —
    // currently empty, so this is guarded ahead of the shipped consumers landing); (b)
    // every actual referrer lives under the exempt `gate/` surface (reused only by the
    // gate/test build); (c) it is genuinely consumed by the gate surface (non-vacuous —
    // at least one referrer); and (d) the substrate itself declares NO ProjectReference
    // (leaf — reinforces the non-recursive bootstrap, INV-033). Release-provenance
    // consumers INV-031/032/033 reuse the GENERIC contracts by reimplementation, never
    // by linking this gate project.
    [Fact]
    public void Provenance_is_a_non_shipped_substrate_no_shipped_reference()
    {
        string repoRoot = TestPaths.RepoRoot();
        string provenanceCsproj = Path.GetFullPath(
            TestPaths.RepoFile("gate", ProvenanceProject, ProvenanceProject + ".csproj"));

        var referrers = new List<string>();
        foreach (string csproj in Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories))
        {
            string norm = csproj.Replace('\\', '/');
            // Skip build outputs and the spike's scratch run trees (stale csproj copies).
            if (norm.Contains("/bin/") || norm.Contains("/obj/") || norm.Contains("/out/"))
            {
                continue;
            }
            string dir = Path.GetDirectoryName(csproj)!;
            foreach (Match m in Regex.Matches(File.ReadAllText(csproj), "<ProjectReference\\s+Include=\"([^\"]+)\""))
            {
                string include = m.Groups[1].Value.Replace('\\', '/');
                string target = Path.GetFullPath(Path.Combine(dir, include));
                if (string.Equals(target, provenanceCsproj, StringComparison.Ordinal))
                {
                    referrers.Add(Path.GetRelativePath(repoRoot, csproj).Replace('\\', '/'));
                }
            }
        }

        // (a) PRH-010: no SHIPPED (`src/`) project references the gate substrate.
        Assert.DoesNotContain(referrers, r => r.StartsWith("src/", StringComparison.Ordinal));

        // (b) every referrer is confined to the exempt, non-shipped `gate/` surface.
        Assert.All(referrers, r => Assert.StartsWith("gate/", r));

        // (c) non-vacuity: the substrate is actually wired into the gate/test surface
        //     (else this scan would pass on a dead, unreferenced project).
        Assert.NotEmpty(referrers);

        // (d) the substrate is a leaf — no outgoing ProjectReference (non-recursion).
        Assert.DoesNotContain("<ProjectReference", File.ReadAllText(provenanceCsproj));
    }
}
