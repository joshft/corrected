using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Track 5c-i — INV-027 cross-doc consistency (RS-004 / AP-016: this project's escaped-bug
/// class is the *incomplete* cross-doc edit) + the no-standalone-consumer scan.
///
/// INV-027 re-keys parent INV-036's production-code ban from
/// <c>status ∈ {BLOCKED, indeterminate}</c> to <c>effective_lifecycle != ENTERED</c>. The
/// amendment MUST touch EVERY currently-status-keyed clause, or two contradictory predicates
/// for one invariant coexist — one of which (the unamended parent) fails OPEN at READY+BLOCKED.
/// These tests assert NO enumerated clause still uses the status ban predicate:
///   * parent INV-036 (Statement / Violated-when / Enforcement) in phase-0-1-worker.md
///   * ARCHITECTURE.md:111 (the CI-check clause) and :113 (the partition prose)
///   * the kernel ban-predicate comment (ReadinessGate.cs)
/// Each goes RED until GREEN edits the doc/source; the exact current wording is quoted in
/// the RED-phase RETURN for review of the edits GREEN will make.
///
/// AP-031 real-artifact clause is NOT triggered — these are committed PROJECT docs/source,
/// not `.correctless/artifacts/` producer outputs.
/// </summary>
public class Inv027CrossDocConsistencyTests
{
    private static string Read(params string[] parts) => File.ReadAllText(TestPaths.RepoFile(parts));

    /// <summary>Collapse ALL whitespace runs (incl. newlines) to a single space, so a
    /// line-wrap reflow by GREEN does not falsely satisfy an absence assertion.</summary>
    private static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    private static string Section(string text, string startHeading, string endMarker)
    {
        int start = text.IndexOf(startHeading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"section start '{startHeading}' not found (fixture drift?)");
        int end = text.IndexOf(endMarker, start + startHeading.Length, StringComparison.Ordinal);
        if (end < 0) { end = text.Length; }
        return text.Substring(start, end - start);
    }

    // ---- The finite enumerated STALE-LITERAL set (the status-keyed ban predicates). Each
    // MUST be absent after the GREEN re-key. Verbatim from the current committed wording. ----

    private const string ParentStmtStale =
        "while `implementation_readiness.status = BLOCKED`, the feature's";
    private const string ParentViolatedStale =
        "an implementation PR merges while `status ≠ READY`";
    private const string ParentEnforcementStale =
        "the build/PR when `status = BLOCKED` and the production surface is non-empty";
    private const string ArchLine111Stale =
        "fail a PR that lands production code while `implementation_readiness.status = BLOCKED`";
    private const string ArchLine113Stale =
        "non-trivial content here while BLOCKED trips PRH-008";
    private const string KernelCommentStale =
        "stays active while status in {BLOCKED, indeterminate}";

    // ===================================================================================
    // PARENT INV-036 (phase-0-1-worker.md) — Statement / Violated-when / Enforcement.
    // ===================================================================================

    // Tests INV-027 [integration]: the parent INV-036 STATEMENT ban clause is re-keyed off
    // status. RED now (the stale literal is present); GREEN re-keys to effective_lifecycle.
    [Fact]
    public void Parent_inv036_statement_no_longer_keys_the_ban_off_status()
    {
        string norm = Norm(Read(".correctless", "specs", "phase-0-1-worker.md"));
        Assert.DoesNotContain(Norm(ParentStmtStale), norm);
    }

    // Tests INV-027 [integration]: the parent INV-036 VIOLATED-WHEN clause "an implementation
    // PR merges while `status ≠ READY`" is FALSE at READY+BLOCKED and MUST become
    // `effective_lifecycle != ENTERED`. RED now.
    [Fact]
    public void Parent_inv036_violated_when_no_longer_uses_status_ne_ready()
    {
        string norm = Norm(Read(".correctless", "specs", "phase-0-1-worker.md"));
        Assert.DoesNotContain(Norm(ParentViolatedStale), norm);
    }

    // Tests INV-027 [integration]: the parent INV-036 ENFORCEMENT clause no longer fires the
    // gate off `status = BLOCKED`. RED now.
    [Fact]
    public void Parent_inv036_enforcement_no_longer_keys_off_status_blocked()
    {
        string norm = Norm(Read(".correctless", "specs", "phase-0-1-worker.md"));
        Assert.DoesNotContain(Norm(ParentEnforcementStale), norm);
    }

    // Tests INV-027 [integration]: the parent INV-036 SECTION positively references
    // `effective_lifecycle` after the amendment (proving the clauses were re-keyed, not merely
    // deleted). RED now (the token is absent from the section). Scoped to the INV-036 section
    // so an unrelated mention elsewhere cannot falsely satisfy it.
    [Fact]
    public void Parent_inv036_section_references_effective_lifecycle()
    {
        string section = Section(
            Read(".correctless", "specs", "phase-0-1-worker.md"), "### INV-036:", "### INV-037");
        Assert.Contains("effective_lifecycle", section);
    }

    // ===================================================================================
    // ARCHITECTURE.md — the CI-check clause (:111) and the partition prose (:113).
    // ===================================================================================

    // Tests INV-027 [integration]: ARCHITECTURE.md:111 no longer fires the path-scoped CI
    // check off `implementation_readiness.status = BLOCKED`. RED now.
    [Fact]
    public void Architecture_111_no_longer_keys_the_ci_check_off_status()
    {
        string norm = Norm(Read(".correctless", "ARCHITECTURE.md"));
        Assert.DoesNotContain(Norm(ArchLine111Stale), norm);
    }

    // Tests INV-027 [integration]: ARCHITECTURE.md:113 partition prose no longer trips the ban
    // "while BLOCKED". RED now.
    [Fact]
    public void Architecture_113_partition_prose_no_longer_trips_while_blocked()
    {
        string norm = Norm(Read(".correctless", "ARCHITECTURE.md"));
        Assert.DoesNotContain(Norm(ArchLine113Stale), norm);
    }

    // Tests INV-027 [integration]: the ARCHITECTURE production-surface PARTITION section
    // positively references `effective_lifecycle` after the amendment. RED now.
    [Fact]
    public void Architecture_partition_section_references_effective_lifecycle()
    {
        string section = Section(
            Read(".correctless", "ARCHITECTURE.md"), "### Production-surface partition", "### Design decisions");
        Assert.Contains("effective_lifecycle", section);
    }

    // ===================================================================================
    // KERNEL ban-predicate comment (ReadinessGate.cs).
    // ===================================================================================

    // Tests INV-027 [integration]: the kernel ban-predicate COMMENT is re-keyed — it no longer
    // reads "stays active while status in {BLOCKED, indeterminate}". RED now; GREEN re-keys it
    // to effective_lifecycle. (The kernel-purity scan ignores comments, so a comment edit is
    // safe.)
    [Fact]
    public void Kernel_ban_predicate_comment_no_longer_keys_off_status()
    {
        string norm = Norm(Read("gate", "Corrected.Gate.Kernel", "ReadinessGate.cs"));
        Assert.DoesNotContain(Norm(KernelCommentStale), norm);
    }

    // ===================================================================================
    // NO-STANDALONE-CONSUMER SCAN — the ban and the entry_integrity verdict are FUSED.
    // ===================================================================================

    // Tests INV-027 [integration]: NO production file in Corrected.Gate consults
    // `effective_lifecycle` to lift the src/ ban OUTSIDE the fused gate — the ONLY impure
    // consumer of effective_lifecycle is LifecycleGate.cs (which co-requires the entry_integrity
    // verdict in the SAME method). A standalone consumer that lifted the ban without the fused
    // verdict would defeat the RS-001 forged-ENTERED defense. Guard test: passes on the stub,
    // goes RED if a future GREEN adds a standalone consumer elsewhere.
    [Fact]
    public void No_standalone_effective_lifecycle_consumer_outside_the_fused_gate()
    {
        string gateDir = TestPaths.RepoFile("gate", "Corrected.Gate");
        var consumers = Directory.EnumerateFiles(gateDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => { var n = f.Replace('\\', '/'); return !n.Contains("/obj/") && !n.Contains("/bin/"); })
            .Where(f =>
            {
                string lower = File.ReadAllText(f).ToLowerInvariant();
                return lower.Contains("effectivelifecycle") || lower.Contains("effective_lifecycle");
            })
            .Select(Path.GetFileName)
            .ToList();

        // The fused gate itself IS a consumer (non-vacuous), and it is the ONLY one.
        Assert.Contains("LifecycleGate.cs", consumers);
        Assert.All(consumers, name =>
            Assert.True(name == "LifecycleGate.cs",
                $"standalone effective_lifecycle consumer outside the fused gate: {name}"));
    }

    // Tests INV-027 [integration]: the fused gate SOURCE co-requires the entry_integrity verdict
    // — LifecycleGate.cs references EntryIntegrity in the same file that decides the ban. Pins
    // that the src/-ban check and the entry_integrity verdict are FUSED (not two separate
    // consumers). Passes on the stub.
    [Fact]
    public void Fused_gate_source_co_requires_entry_integrity()
    {
        string src = Read("gate", "Corrected.Gate", "LifecycleGate.cs");
        Assert.Contains("EntryIntegrity", src);
        Assert.Contains("EffectiveLifecycle", src.Replace("effectiveLifecycle", "EffectiveLifecycle"));
    }
}
