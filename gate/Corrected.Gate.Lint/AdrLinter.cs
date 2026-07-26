using System.Collections.Generic;

namespace Corrected.Gate.Lint;

// The extracted, verifier-free linter surface (INV-018/DD-001). Carves
// AdrLinter + its transitive type closure (AdjudicationRecord -> RouteState /
// IncompatibleClass / ThreeCellOutcome / ProbeStatus, RouteClaim) out of the
// spike's Components.cs, WITHOUT dragging ManagedLauncher/Process.Start.

public enum RouteState
{
    COMPATIBLE,
    INCOMPLETE,
    INCOMPATIBLE,
    UPSTREAM_DEFECT,
    pending,
}

public enum IncompatibleClass
{
    None,
    Source,
    Binary,
    Behavioral,
}

public enum ThreeCellOutcome
{
    consistent,
    inconsistent,
    unknown,
}

public enum ProbeStatus
{
    pass,
    fail,
    skipped,
}

/// <summary>A single route claim as seen by the permissive linter (INV-008a redundant cross-check).</summary>
public sealed record RouteClaim(string Route, string Verdict, string? AdjudicationRecordId, string? Evidence);

/// <summary>A terminal adjudication record (transitive closure of AdrLinter).</summary>
public sealed record AdjudicationRecord(
    string RecordId,
    RouteState State,
    IncompatibleClass IncompatibleClass,
    ThreeCellOutcome Outcome,
    ProbeStatus ProbeStatus);

/// <summary>
/// The extracted spike ADR linter. INV-008(a) runs it ONLY as a redundant
/// cross-check ANDed with the authoritative hardened decision — never as the sole
/// trust source (PRH-005). Source-digest pinned by an append-only registry
/// (INV-008c).
/// </summary>
public static class AdrLinter
{
    /// <summary>
    /// Returns findings (empty list == pass for an all-pass COMPATIBLE ADR). This is
    /// the spike's permissive line-scanner, retained ONLY as the redundant cross-check
    /// (PRH-005) — it is never the sole trust source; the authoritative decision is the
    /// hardened AdrLintBlockParser in Corrected.Gate. Self-contained (no dependency on
    /// Corrected.Gate) so the Lint lib stays a leaf.
    /// </summary>
    public static IReadOnlyList<string> Lint(string adrPath, IReadOnlyList<AdjudicationRecord> records)
    {
        var findings = new List<string>();
        if (!System.IO.File.Exists(adrPath))
        {
            findings.Add("adr file not found: " + adrPath);
            return findings;
        }

        string text = System.IO.File.ReadAllText(adrPath);

        // selected_route must be A (the redundant view of the decision field).
        var routeMatch = System.Text.RegularExpressions.Regex.Match(
            text, @"(?m)^\s*selected_route:\s*(\S+)");
        if (routeMatch.Success && routeMatch.Groups[1].Value != "A")
        {
            findings.Add("selected_route is not A: " + routeMatch.Groups[1].Value);
        }

        // Every route verdict (a `verdict:` key line) must be COMPATIBLE for an all-pass ADR.
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     text, @"(?m)^\s*verdict:\s*(\w+)"))
        {
            string verdict = m.Groups[1].Value;
            if (verdict != "COMPATIBLE")
            {
                findings.Add("non-COMPATIBLE route verdict: " + verdict);
            }
        }

        return findings;
    }
}
