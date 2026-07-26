using System.Collections.Generic;

namespace Corrected.Gate;

/// <summary>
/// The typed outcome of the (a″) COMPATIBLE recompute (INV-008a″ / RS-201 / R3-B3).
/// <see cref="Compatible"/> is the only success; every other value is a fail-closed
/// reject reason. A vacuous always-<see cref="Compatible"/> recompute is caught by the
/// direct reject-branch tests, which the whole-file (a′) sha pin can never exercise
/// (any sample edit changes the file SHA and is rejected by the pin FIRST; a sample
/// that passes the pin is byte-identical to canonical and its multiset is correct).
/// </summary>
public enum RecomputeVerdict
{
    Compatible,
    MissingEntry,           // an expected (probe,route) is absent (plan-shrunk / route-B-only / empty)
    ExtraEntry,             // an unexpected (probe,route) is present
    DuplicateEntry,         // a (probe,route) appears twice (multiset-count breach; HashSet would dedup)
    RouteAVerdictInvalid,   // not exactly ONE Route-A route_verdict, or its state != COMPATIBLE
    ProbeNotPass,           // a Route-A+shared per-probe result has status != pass
}

/// <summary>
/// The (a″) cardinality-guarded, duplicate-safe COMPATIBLE recompute over ALREADY-PARSED
/// per-probe results — NOT a file — so the reject branch is testable IN ISOLATION,
/// bypassing the (a′) whole-file sha pin (which otherwise rejects any tampered sample
/// before the recompute is reached). It mirrors VerdictAggregator.ComputeRouteVerdict's
/// non-veto Route-A+shared partition and its <c>if(!seen.Add(key))</c> duplicate
/// rejection (R3-B3b) — a HashSet.SetEquals would silently dedup, so this is
/// count-aware multiset equality against the expected (probe,route) set derived from the
/// pinned probe manifest, plus exactly-one-Route-A-verdict == COMPATIBLE, plus all
/// Route-A+shared per-probe status == pass.
/// </summary>
public static class P1Recompute
{
    /// <summary>
    /// Recompute Route-A COMPATIBLE from the parsed Route-A+shared per-probe results,
    /// the expected (probe,route) partition, and the parsed route_verdicts.
    /// </summary>
    public static RecomputeVerdict RecomputeRouteACompatible(
        IReadOnlyList<(string Probe, string Route, string Status)> actualPerProbe,
        IReadOnlyList<(string Probe, string Route)> expectedRouteAShared,
        IReadOnlyList<(string Route, string State)> routeVerdicts)
    {
        // Exactly ONE Route-A route_verdict, whose state == COMPATIBLE (never trust the
        // declared state alone; a HashSet would silently dedup — R3-B3).
        var routeA = routeVerdicts.Where(rv => rv.Route == "A").ToList();
        if (routeA.Count != 1 || routeA[0].State != "COMPATIBLE")
        {
            return RecomputeVerdict.RouteAVerdictInvalid;
        }

        // Count-aware multiset equality against the expected (probe,route) set derived
        // from the pinned probe manifest (mirrors ComputeRouteVerdict's if(!seen.Add(key))).
        var actualCounts = new Dictionary<(string, string), int>();
        foreach (var (probe, route, _) in actualPerProbe)
        {
            var key = (probe, route);
            actualCounts.TryGetValue(key, out int c);
            actualCounts[key] = c + 1;
            if (c + 1 > 1)
            {
                return RecomputeVerdict.DuplicateEntry;
            }
        }

        var expectedSet = new HashSet<(string, string)>(expectedRouteAShared);

        // Every expected (probe,route) must be present (no missing / plan-shrunk).
        foreach (var exp in expectedSet)
        {
            if (!actualCounts.ContainsKey(exp))
            {
                return RecomputeVerdict.MissingEntry;
            }
        }

        // No extra (probe,route) outside the expected partition.
        foreach (var key in actualCounts.Keys)
        {
            if (!expectedSet.Contains(key))
            {
                return RecomputeVerdict.ExtraEntry;
            }
        }

        // Every Route-A+shared per-probe result must be status == pass.
        foreach (var (_, _, status) in actualPerProbe)
        {
            if (status != "pass")
            {
                return RecomputeVerdict.ProbeNotPass;
            }
        }

        return RecomputeVerdict.Compatible;
    }
}
