using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Corrected.Gate.Tests;

// Test helper (real logic, under gate/Corrected.Gate.Tests/ so the workflow gate
// classifies it as test infrastructure). Builds a SYNTHESIZED repo tree for driving
// MigrationManifest.CheckConsistency(repoRoot) over a controlled parent + the real
// committed manifest — the DD-003 analogue of P1Tree. Reading the real committed
// manifest (never a prior-run root) matches the INV-013 degraded-env pattern.

/// <summary>
/// The canonical current-state spans for the three DD-003 digest anchors. These are
/// the exact bytes the committed manifest's stage_before_sha256 (A6: also
/// stage_after_sha256) is the SHA-256 of, kept here so the synthetic-tree tests bind
/// the same grammar as production without re-hashing.
/// </summary>
public static class MigrationSpans
{
    // A2 is a genuine before != after P1-flip transition.
    public const string A2Before =
        "land the carrier and INV-002's positive READY-rejection fixture table before any real precondition discharges";
    public const string A2After =
        "carrier + reject corpus proven green at/before the discharge commit";

    // A6 is an A-site: corrected when the carrier landed, unchanged at the P1 flip (before == after).
    public const string A6Before =
        "the entrypoint YAML exists at ARCHITECTURE.md:61 (since /carchitect 2026-07-24); the carrier is now homed";
    public const string A6After = A6Before;

    // B5 is a genuine before != after P1-flip transition.
    public const string B5Before =
        "A SEPARATE test asserts the committed file currently parses to BLOCKED-all-false.";
    public const string B5After =
        "a separate test asserts the committed file currently parses to P1=true, P2/P3 false -> BLOCKED (post-flip)";
}

/// <summary>
/// A synthesized repo tree: a controlled parent spec (readiness block + chosen anchors
/// + optional extra body) plus a verbatim copy of the real committed migration manifest,
/// laid out at the exact relative paths CheckConsistency reads.
/// </summary>
internal sealed class MigrationTree : IDisposable
{
    private const string ParentRel = ".correctless/specs/phase-0-1-worker.md";
    private const string ManifestRel = "gate/Corrected.Gate.Tests/manifests/readiness-migration-manifest.json";

    public string Root { get; }

    private MigrationTree(string root) => Root = root;

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }

    /// <param name="p1Satisfied">true => stage resolves to B; false => A.</param>
    /// <param name="anchors">id -> the exact bytes to place between that id's paired markers (omit an id to leave its anchor absent).</param>
    /// <param name="extraBody">extra normative-body prose (e.g. a stale literal) appended before the appendix.</param>
    public static MigrationTree Build(
        bool p1Satisfied,
        IReadOnlyDictionary<string, string> anchors,
        string extraBody = "")
    {
        var tree = new MigrationTree(Path.Combine(Path.GetTempPath(), "gate-migtree-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(tree.Root);

        // Verbatim copy of the real committed manifest — the SOLE digest authority.
        string realManifest = File.ReadAllText(TestPaths.RepoFile(ManifestRel.Split('/')));
        tree.Write(ManifestRel, realManifest);

        tree.Write(ParentRel, BuildParent(p1Satisfied, anchors, extraBody));
        return tree;
    }

    private static string BuildParent(bool p1Satisfied, IReadOnlyDictionary<string, string> anchors, string extraBody)
    {
        string p1Sat = p1Satisfied ? "true" : "false";
        string p1Ev = p1Satisfied ? "readiness-gate-carrier::P1Probe" : "null";

        var sb = new StringBuilder();
        sb.Append("# Synthetic parent spec (DD-003 test fixture)\n\n");
        sb.Append("```yaml\n");
        sb.Append("implementation_readiness:\n");
        sb.Append("  schema_version: 1\n");
        sb.Append("  status: BLOCKED\n");
        sb.Append("  ready_predicate: \"P1 AND P2 AND P3\"\n");
        sb.Append("  preconditions:\n");
        sb.Append("    - id: P1\n      name: p1\n      satisfied: ").Append(p1Sat)
          .Append("\n      evidence: ").Append(p1Ev).Append("\n      discharges: []\n");
        sb.Append("    - id: P2\n      name: p2\n      satisfied: false\n      evidence: null\n      discharges: []\n");
        sb.Append("    - id: P3\n      name: p3\n      satisfied: false\n      evidence: null\n      discharges: []\n");
        sb.Append("```\n\n");
        sb.Append("## Synthetic normative body\n\n");

        foreach (var kv in anchors)
        {
            sb.Append("<!-- correctless:readiness-current-state:start id=\"").Append(kv.Key).Append("\" -->\n");
            sb.Append(kv.Value).Append('\n');
            sb.Append("<!-- correctless:readiness-current-state:end id=\"").Append(kv.Key).Append("\" -->\n\n");
        }

        if (extraBody.Length > 0)
        {
            sb.Append(extraBody).Append('\n');
        }

        // A non-normative appendix that DOES carry a stale literal, to prove the scan
        // stops at the "Notes for review" boundary (history is not flagged).
        sb.Append("\n## Notes for review (not invariants)\n");
        sb.Append("- historical: clone + `rm -rf out` because committed out/ shipped (RS-004).\n");
        return sb.ToString();
    }

    private void Write(string rel, string text)
    {
        string dst = Path.Combine(Root, Path.Combine(rel.Split('/')));
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.WriteAllText(dst, text);
    }
}
