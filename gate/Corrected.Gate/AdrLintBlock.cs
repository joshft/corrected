using System.Collections.Generic;
using System.Linq;

namespace Corrected.Gate;

/// <summary>A parsed route claim inside the ADR adr_lint block (INV-002/008).</summary>
public sealed class AdrRoute
{
    private AdrRoute(string route, string verdict, string? adjudicationRecordId, string? evidence)
    {
        Route = route;
        Verdict = verdict;
        AdjudicationRecordId = adjudicationRecordId;
        Evidence = evidence;
    }

    public string Route { get; }
    public string Verdict { get; }
    public string? AdjudicationRecordId { get; }
    public string? Evidence { get; }

    /// <summary>Route is NOT one of INV-003's protected trust types, so a public factory is permitted.</summary>
    public static AdrRoute Create(string route, string verdict, string? adjudicationRecordId, string? evidence)
        => new(route, verdict, adjudicationRecordId, evidence);
}

/// <summary>
/// The DISTINCT hardened ADR adr_lint DTO (INV-002/008). Type home is
/// Corrected.Gate (parsed + consumed by the P1 probe, not a kernel input;
/// EXT9-08). REQUIRED tier: boundary_decision, selected_route, routes[]. OPTIONAL
/// tier (DD-003 Stage B, carved out of EnforceRequiredMembers) with EXPLICIT
/// PRESENCE BITS distinguishing key-absent from explicit null: status/HasStatus,
/// supersedes/HasSupersedes, superseded_by/HasSupersededBy. Built only through the
/// public static validation-gated TryCreate (INV-003); private ctor.
/// </summary>
public sealed class AdrLintBlock
{
    private readonly AdrRoute[] _routes;

    private AdrLintBlock(
        string boundaryDecision, string? selectedRoute, AdrRoute[] routes,
        bool hasStatus, string? status,
        bool hasSupersedes, string? supersedes,
        bool hasSupersededBy, string? supersededBy)
    {
        BoundaryDecision = boundaryDecision;
        SelectedRoute = selectedRoute;
        _routes = routes;
        HasStatus = hasStatus;
        Status = status;
        HasSupersedes = hasSupersedes;
        Supersedes = supersedes;
        HasSupersededBy = hasSupersededBy;
        SupersededBy = supersededBy;
    }

    // REQUIRED tier (present today, under EnforceRequiredMembers).
    public string BoundaryDecision { get; }
    public string? SelectedRoute { get; }
    public IReadOnlyList<AdrRoute> Routes => _routes;

    // OPTIONAL tier (DD-003 Stage B). Presence bit + value.
    public bool HasStatus { get; }
    public string? Status { get; }

    public bool HasSupersedes { get; }
    public string? Supersedes { get; }

    public bool HasSupersededBy { get; }
    public string? SupersededBy { get; }

    /// <summary>Single public validation-gated factory (INV-003/EXT9-08).</summary>
    public static AdrLintBlock? TryCreate(
        string boundaryDecision,
        string? selectedRoute,
        IReadOnlyList<AdrRoute> routes,
        bool hasStatus, string? status,
        bool hasSupersedes, string? supersedes,
        bool hasSupersededBy, string? supersededBy)
    {
        if (string.IsNullOrEmpty(boundaryDecision))
        {
            return null;
        }

        if (routes is null || routes.Count == 0)
        {
            return null;
        }

        return new AdrLintBlock(
            boundaryDecision, selectedRoute, routes.ToArray(),
            hasStatus, status, hasSupersedes, supersedes, hasSupersededBy, supersededBy);
    }
}
